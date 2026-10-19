using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Nops.Core.Tests.Keys;

public class DataKeyResolverTests
{
    // Fake master key provider for testing. When an enc filter is supplied the
    // provider only succeeds for that encrypted value (models real recipient matching).
    private class FakeMasterKey : Nops.Core.Keys.IMasterKeyProvider
    {
        private readonly string? _enc;

        public FakeMasterKey(string keyType, string? enc = null)
        {
            KeyType = keyType;
            _enc = enc;
        }

        public string KeyType { get; }
        public byte[]? DataKey { get; set; }

        public Task<byte[]?> DecryptShareAsync(string enc, System.Threading.CancellationToken ct)
        {
            return Task.FromResult(_enc == null || _enc == enc ? DataKey : null);
        }
    }

    [Fact]
    public async Task Resolve_SingleGroup_FirstSuccessWins()
    {
        // threshold 0 (unset) → single group: any master key can decrypt the data key.
        var metadata = CreateMetadataWithAge(new[] { ("recipient1", "enc1"), ("recipient2", "enc2") }, threshold: 0);

        var key1 = new FakeMasterKey("age") { DataKey = null }; // fails
        var key2 = new FakeMasterKey("age") { DataKey = new byte[32] }; // succeeds

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata, new[] { key1, key2 }, System.Threading.CancellationToken.None);

        Assert.Equal(key2.DataKey, result);
    }

    [Fact]
    public async Task Resolve_MultiGroup_ShamirCombine()
    {
        // 2-of-3 Shamir shares over a 32-byte data key (each share is 33 bytes).
        var secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        // Two key groups (age + pgp) → Shamir.
        var metadata = CreateMetadataWithAgeAndPgp(threshold: 2);

        var ageKey = new FakeMasterKey("age", "e1") { DataKey = shares[0] };
        var pgpKey = new FakeMasterKey("pgp", "enc1") { DataKey = shares[1] };

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata, new[] { ageKey, pgpKey }, System.Threading.CancellationToken.None);

        Assert.Equal(secret, result);
    }

    [Fact]
    public async Task Resolve_ThreeGroups_TwoSharesCombine()
    {
        var secret = Enumerable.Range(0, 32).Select(i => (byte)(i * 3)).ToArray();
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        var metadata = CreateMetadataWithThreeGroups(threshold: 2);

        // Provide only two of the three groups (2-of-3).
        var share1 = new FakeMasterKey("age", "e1") { DataKey = shares[0] };
        var share2 = new FakeMasterKey("age", "e2") { DataKey = shares[1] };

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata, new[] { share1, share2 }, System.Threading.CancellationToken.None);

        Assert.Equal(secret, result);
    }

    [Fact]
    public async Task Resolve_AllFail_Throws()
    {
        var metadata = CreateMetadataWithAge(new[] { ("r1", "e1") }, threshold: 0);
        var key = new FakeMasterKey("age") { DataKey = null };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            Nops.Core.Keys.DataKeyResolver.ResolveAsync(
                metadata, new[] { key }, System.Threading.CancellationToken.None));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public async Task Resolve_ThresholdNotMet_Throws()
    {
        var secret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        var metadata = CreateMetadataWithAgeAndPgp(threshold: 2);

        // Only the age group succeeds; pgp group has no provider.
        var ageKey = new FakeMasterKey("age", "e1") { DataKey = shares[0] };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            Nops.Core.Keys.DataKeyResolver.ResolveAsync(
                metadata, new[] { ageKey }, System.Threading.CancellationToken.None));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public async Task Resolve_ThresholdZero_MeansSingleGroup()
    {
        var metadata = CreateMetadataWithAge(new[] { ("r1", "e1") }, threshold: 0);

        var key1 = new FakeMasterKey("age") { DataKey = new byte[32] };
        var key2 = new FakeMasterKey("age") { DataKey = new byte[32] };

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata, new[] { key1, key2 }, System.Threading.CancellationToken.None);

        Assert.Equal(key1.DataKey, result);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== DATA KEY RESOLVER ===");
        Console.WriteLine("Single group: first master key success returns data key");
        Console.WriteLine("Multi-group: collect >= threshold shares, combine with Shamir");
        Console.WriteLine("shamir_threshold == 0: single group (no Shamir)");
        Console.WriteLine("=========================");
        Assert.True(true, "Spike complete");
    }

    // Provider that fails with an explicit "unsupported" error (e.g. locked PGP key).
    private class UnsupportedMasterKey : Nops.Core.Keys.IMasterKeyProvider
    {
        public UnsupportedMasterKey(string keyType) => KeyType = keyType;

        public string KeyType { get; }

        public Task<byte[]?> DecryptShareAsync(string enc, System.Threading.CancellationToken ct) =>
            throw new NotSupportedException("PGP private key is encrypted but no passphrase was supplied.");
    }

    private class CountingMasterKey : Nops.Core.Keys.IMasterKeyProvider
    {
        private readonly Dictionary<string, byte[]> _shares;

        public CountingMasterKey(Dictionary<string, byte[]> shares) => _shares = shares;

        public string KeyType => "age";
        public int Calls { get; private set; }

        public Task<byte[]?> DecryptShareAsync(string enc, System.Threading.CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_shares.TryGetValue(enc, out var share) ? share : null);
        }
    }

    [Fact]
    public async Task Resolve_UnsupportedProvider_DoesNotBlockOtherKeys()
    {
        var metadata = CreateMetadataWithAgeAndPgp(threshold: 0);
        // Collapse to one group holding pgp first, then age.
        metadata = metadata with
        {
            KeyGroups = new List<Nops.Core.Stores.KeyGroup>
            {
                new(new[]
                {
                    new Nops.Core.Stores.KeyGroupEntry("pgp", "enc1"),
                    new Nops.Core.Stores.KeyGroupEntry("age", "e1"),
                }),
            },
        };

        var dataKey = Enumerable.Repeat((byte)7, 32).ToArray();
        var providers = new Nops.Core.Keys.IMasterKeyProvider[]
        {
            new UnsupportedMasterKey("pgp"),
            new FakeMasterKey("age", "e1") { DataKey = dataKey },
        };

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata, providers, System.Threading.CancellationToken.None);

        Assert.Equal(dataKey, result);
    }

    [Fact]
    public async Task Resolve_OnlyUnsupportedProvider_SurfacesNotSupported()
    {
        var metadata = CreateMetadataWithAgeAndPgp(threshold: 0) with
        {
            KeyGroups = new List<Nops.Core.Stores.KeyGroup>
            {
                new(new[] { new Nops.Core.Stores.KeyGroupEntry("pgp", "enc1") }),
            },
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata, new[] { new UnsupportedMasterKey("pgp") }, System.Threading.CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_MultiGroup_StopsOnceThresholdReached()
    {
        var secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);
        var provider = new CountingMasterKey(new Dictionary<string, byte[]>
        {
            ["e1"] = shares[0],
            ["e2"] = shares[1],
            ["e3"] = shares[2],
        });

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            CreateMetadataWithThreeGroups(threshold: 2), new[] { provider }, System.Threading.CancellationToken.None);

        Assert.Equal(secret, result);
        Assert.Equal(2, provider.Calls);
    }

    private static Nops.Core.Stores.SopsMetadata CreateMetadataWithAge((string, string)[] entries, int threshold)
    {
        var ageEntries = entries.Select(e => new Nops.Core.Stores.AgeEntry(e.Item1, e.Item2)).ToList();

        return new Nops.Core.Stores.SopsMetadata(
            Mac: null,
            Age: ageEntries,
            Pgp: Array.Empty<Nops.Core.Stores.PgpEntry>(),
            Kms: Array.Empty<object>(),
            GcpKms: Array.Empty<object>(),
            AzureKv: Array.Empty<object>(),
            Hckms: Array.Empty<object>(),
            HcVault: Array.Empty<object>(),
            ShamirThreshold: threshold,
            UnencryptedSuffix: null, EncryptedSuffix: null,
            UnencryptedRegex: null, EncryptedRegex: null,
            UnencryptedCommentRegex: null, EncryptedCommentRegex: null,
            MacOnlyEncrypted: false, Version: "3.10.2", LastModified: null);
    }

    private static Nops.Core.Stores.SopsMetadata CreateMetadataWithAgeAndPgp(int threshold)
    {
        var ageEntries = new List<Nops.Core.Stores.AgeEntry> { new("r1", "e1") };
        var pgpEntries = new List<Nops.Core.Stores.PgpEntry> { new("fp1", "enc1", null) };

        // Explicit key groups: one age group and one pgp group (Shamir).
        var keyGroups = new List<Nops.Core.Stores.KeyGroup>
        {
            new(new[] { new Nops.Core.Stores.KeyGroupEntry("age", "e1") }),
            new(new[] { new Nops.Core.Stores.KeyGroupEntry("pgp", "enc1") }),
        };

        return new Nops.Core.Stores.SopsMetadata(
            Mac: null,
            Age: ageEntries,
            Pgp: pgpEntries,
            Kms: Array.Empty<object>(),
            GcpKms: Array.Empty<object>(),
            AzureKv: Array.Empty<object>(),
            Hckms: Array.Empty<object>(),
            HcVault: Array.Empty<object>(),
            ShamirThreshold: threshold,
            UnencryptedSuffix: null, EncryptedSuffix: null,
            UnencryptedRegex: null, EncryptedRegex: null,
            UnencryptedCommentRegex: null, EncryptedCommentRegex: null,
            MacOnlyEncrypted: false, Version: "3.10.2", LastModified: null,
            KeyGroups: keyGroups);
    }

    private static Nops.Core.Stores.SopsMetadata CreateMetadataWithThreeGroups(int threshold)
    {
        var keyGroups = new List<Nops.Core.Stores.KeyGroup>
        {
            new(new[] { new Nops.Core.Stores.KeyGroupEntry("age", "e1") }),
            new(new[] { new Nops.Core.Stores.KeyGroupEntry("age", "e2") }),
            new(new[] { new Nops.Core.Stores.KeyGroupEntry("age", "e3") }),
        };

        return new Nops.Core.Stores.SopsMetadata(
            Mac: null,
            Age: new List<Nops.Core.Stores.AgeEntry> { new("r1", "e1"), new("r2", "e2"), new("r3", "e3") },
            Pgp: Array.Empty<Nops.Core.Stores.PgpEntry>(),
            Kms: Array.Empty<object>(),
            GcpKms: Array.Empty<object>(),
            AzureKv: Array.Empty<object>(),
            Hckms: Array.Empty<object>(),
            HcVault: Array.Empty<object>(),
            ShamirThreshold: threshold,
            UnencryptedSuffix: null, EncryptedSuffix: null,
            UnencryptedRegex: null, EncryptedRegex: null,
            UnencryptedCommentRegex: null, EncryptedCommentRegex: null,
            MacOnlyEncrypted: false, Version: "3.10.2", LastModified: null,
            KeyGroups: keyGroups);
    }
}
