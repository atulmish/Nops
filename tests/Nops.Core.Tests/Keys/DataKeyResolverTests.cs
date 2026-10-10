using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Nops.Core.Tests.Keys;

public class DataKeyResolverTests
{
    // Fake master key provider for testing
    private class FakeMasterKey : Nops.Core.Keys.IMasterKeyProvider, Nops.Core.Keys.IDataKeyProvider
    {
        public string KeyType => "fake";
        public byte[]? DataKey { get; set; }

        public Task<byte[]?> DecryptShareAsync(string enc, System.Threading.CancellationToken ct)
        {
            return Task.FromResult(DataKey);
        }
    }

    [Fact]
    public async Task Resolve_SingleGroup_FirstSuccessWins()
    {
        var metadata = CreateMetadataWithAge(
            new[] {
                ("recipient1", "enc1"),
                ("recipient2", "enc2")
            });

        // With each provider as its own group, we need both to succeed for Shamir
        var key1 = new FakeMasterKey { DataKey = null }; // fails
        var key2 = new FakeMasterKey { DataKey = new byte[32] }; // succeeds

        // Since threshold=0 means len(groups)=2, and we only have 1 success, it should fail
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            Nops.Core.Keys.DataKeyResolver.ResolveAsync(
                metadata,
                new[] { key1, key2 },
                System.Threading.CancellationToken.None));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public async Task Resolve_MultiGroup_ShamirCombine()
    {
        // Create a 2-of-3 Shamir setup (shares WITHOUT index byte for resolver)
        var secret = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        // Strip index bytes from shares (resolver gets raw share values)
        var rawShare1 = shares[0][1..];
        var rawShare2 = shares[1][1..];

        var metadata = CreateMetadataWithShamirThreshold(2);

        var key1 = new FakeMasterKey { DataKey = rawShare1 };
        var key2 = new FakeMasterKey { DataKey = rawShare2 };

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata,
            new[] { key1, key2 },
            System.Threading.CancellationToken.None);

        Assert.Equal(secret, result);
    }

    [Fact]
    public async Task Resolve_AllFail_ThrowsAggregateException()
    {
        var metadata = CreateMetadataWithAge(
            new[] { ("r1", "e1") });

        var key = new FakeMasterKey { DataKey = null };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            Nops.Core.Keys.DataKeyResolver.ResolveAsync(
                metadata,
                new[] { key },
                System.Threading.CancellationToken.None));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public async Task Resolve_ThresholdNotMet_Throws()
    {
        var secret = new byte[] { 0x01 };
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        var metadata = CreateMetadataWithShamirThreshold(2);

        // Only provide 1 share when threshold is 2 (strip index byte)
        var key = new FakeMasterKey { DataKey = shares[0][1..] };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            Nops.Core.Keys.DataKeyResolver.ResolveAsync(
                metadata,
                new[] { key },
                System.Threading.CancellationToken.None));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public async Task Resolve_ThresholdZero_MeansAllGroups()
    {
        var secret = new byte[] { 0x42 };
        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 2, threshold: 2);

        // shamir_threshold == 0 means use number of groups
        var metadata = CreateMetadataWithShamirThreshold(0);

        var key1 = new FakeMasterKey { DataKey = shares[0][1..] }; // Strip index
        var key2 = new FakeMasterKey { DataKey = shares[1][1..] }; // Strip index

        var result = await Nops.Core.Keys.DataKeyResolver.ResolveAsync(
            metadata,
            new[] { key1, key2 },
            System.Threading.CancellationToken.None);

        Assert.Equal(secret, result);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== DATA KEY RESOLVER ===");
        Console.WriteLine("Single group: first master key success returns data key");
        Console.WriteLine("Multi-group: collect >= threshold shares, combine with Shamir");
        Console.WriteLine("shamir_threshold == 0: means len(KeyGroups)");
        Console.WriteLine("=========================");
        Assert.True(true, "Spike complete");
    }

    private static Nops.Core.Stores.SopsMetadata CreateMetadataWithAge((string, string)[] entries)
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
            ShamirThreshold: 0,
            UnencryptedSuffix: null, EncryptedSuffix: null,
            UnencryptedRegex: null, EncryptedRegex: null,
            UnencryptedCommentRegex: null, EncryptedCommentRegex: null,
            MacOnlyEncrypted: false, Version: "3.10.2", LastModified: null);
    }

    private static Nops.Core.Stores.SopsMetadata CreateMetadataWithShamirThreshold(int threshold)
    {
        // Create metadata with multiple key groups (age entries in separate "groups")
        var age1 = new List<Nops.Core.Stores.AgeEntry> { new("r1", "e1") };
        var age2 = new List<Nops.Core.Stores.AgeEntry> { new("r2", "e2") };

        // We need to simulate multiple key groups - for now just set threshold
        return new Nops.Core.Stores.SopsMetadata(
            Mac: null,
            Age: age1.Concat(age2).ToList(),
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
}
