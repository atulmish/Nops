using Nops.Core.Keys;
using Nops.Core.Stores;

namespace Nops.Core.Tests.Keys;

/// <summary>How the resolver reports flat (non-age/PGP) key entries and the ways a key can fail.</summary>
public class DataKeyResolverFailureTests
{
    private sealed class Provider(string keyType, Func<string, byte[]?> decrypt) : IMasterKeyProvider
    {
        public string KeyType => keyType;
        public Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default) => Task.FromResult(decrypt(enc));
    }

    private static Dictionary<string, object> Entry(string enc) => new() { ["enc"] = enc };

    private static SopsMetadata Flat(params string[] encs) =>
        SopsMetadataParser.Empty() with { Kms = [.. encs.Select(Entry)] };

    private static SopsMetadata Groups(int threshold, params string[] encs) =>
        SopsMetadataParser.Empty() with
        {
            ShamirThreshold = threshold,
            KeyGroups = [.. encs.Select(e => new KeyGroup([new KeyGroupEntry("aws_kms", e)]))],
        };

    private static Task<byte[]> Resolve(SopsMetadata metadata, params IMasterKeyProvider[] providers) =>
        DataKeyResolver.ResolveAsync(metadata, providers);

    [Fact]
    public async Task NoKeysInTheFile_IsReported()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Resolve(SopsMetadataParser.Empty()));
        Assert.Contains("No master keys", ex.Message);
    }

    [Fact]
    public async Task EntriesWithoutEnc_AreIgnored()
    {
        var metadata = SopsMetadataParser.Empty() with { Kms = [new Dictionary<string, object> { ["arn"] = "x" }, "not a dictionary"] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Resolve(metadata));
    }

    [Fact]
    public async Task FlatKmsEntries_AreMatchedToProvidersByKeyType()
    {
        var key = new byte[32];
        key[0] = 7;

        var result = await Resolve(Flat("e1", "e2"), new Provider("aws_kms", enc => enc == "e2" ? key : null));

        Assert.Equal(key, result);
    }

    [Fact]
    public async Task ProviderOfAnotherType_IsNotAsked()
    {
        var asked = false;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolve(Flat("e1"), new Provider("gcp_kms", _ => { asked = true; return new byte[32]; })));

        Assert.False(asked);
        Assert.Contains("aws_kms x1", ex.Message);
    }

    [Fact]
    public async Task ShareOfTheWrongLength_IsNotAccepted()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolve(Flat("e1"), new Provider("aws_kms", _ => new byte[31])));

        Assert.Contains("Cannot decrypt", ex.Message);
    }

    [Fact]
    public async Task ProviderFailure_IsNamedInTheMessage()
    {
        var provider = new Provider("aws_kms", _ => throw new InvalidDataException("age blob is tampered"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Resolve(Flat("e1"), provider));

        Assert.Contains("Last error: InvalidDataException: age blob is tampered", ex.Message);
    }

    [Fact]
    public async Task UnsupportedKey_IsSurfacedWhenNothingElseWorks()
    {
        var provider = new Provider("aws_kms", _ => throw new NotSupportedException("needs a passphrase"));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Resolve(Flat("e1"), provider));

        Assert.Equal("needs a passphrase", ex.Message);
    }

    [Fact]
    public async Task UnsupportedKey_DoesNotStopAnotherKeyFromWorking()
    {
        var key = new byte[32];

        var result = await Resolve(Flat("e1"),
            new Provider("aws_kms", _ => throw new NotSupportedException("no passphrase")),
            new Provider("aws_kms", _ => key));

        Assert.Equal(key, result);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var provider = new Provider("aws_kms", _ => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolve(Flat("e1"), provider));
    }

    [Fact]
    public async Task Shamir_TooFewShares_ReportsHowManyWereRecovered()
    {
        var shares = Nops.Core.Crypto.Shamir.Split(new byte[32], numShares: 2, threshold: 2);
        var provider = new Provider("aws_kms", enc => enc == "e1" ? shares[0] : null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Resolve(Groups(2, "e1", "e2"), provider));

        Assert.Contains("need 2 shares but only got 1", ex.Message);
    }

    [Fact]
    public async Task Shamir_UnsupportedKey_IsSurfacedWhenTheThresholdIsNotMet()
    {
        var provider = new Provider("aws_kms", _ => throw new NotSupportedException("no passphrase"));

        await Assert.ThrowsAsync<NotSupportedException>(() => Resolve(Groups(2, "e1", "e2"), provider));
    }
}
