using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Nops.Core.Keys;

namespace Nops.Core.Azkv.Tests;

/// <summary>Unwrapping through fake Key Vault clients: no Azure subscription or credentials needed.</summary>
public class AzkvMasterKeyTests
{
    private sealed class FakeCrypto(Func<byte[], UnwrapResult> onUnwrap) : CryptographyClient
    {
        public KeyWrapAlgorithm? Algorithm { get; private set; }

        public override Task<UnwrapResult> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken cancellationToken = default)
        {
            Algorithm = algorithm;
            return Task.FromResult(onUnwrap(encryptedKey));
        }
    }

    private sealed class FakeKeys(Func<string?, KeyVaultKey> onGetKey, FakeCrypto crypto) : KeyClient
    {
        public List<(string Name, string? Version)> GetKeyCalls { get; } = [];
        public (string Name, string? Version)? CryptoRequested { get; private set; }

        public override Task<Response<KeyVaultKey>> GetKeyAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            GetKeyCalls.Add((name, version));
            return Task.FromResult(Response.FromValue(onGetKey(version), null!));
        }

        public override CryptographyClient GetCryptographyClient(string keyName, string? keyVersion = null)
        {
            CryptoRequested = (keyName, keyVersion);
            return crypto;
        }
    }

    private static KeyVaultKey Key(string? version) => KeyModelFactory.KeyVaultKey(
        KeyModelFactory.KeyProperties(
            id: new Uri($"https://v.vault.azure.net/keys/sops/{version ?? "latest"}"),
            name: "sops",
            version: version ?? "latest"),
        key: null!);

    private static UnwrapResult Unwrapped(byte[] key) =>
        CryptographyModelFactory.UnwrapResult("https://v.vault.azure.net/keys/sops/1", key, KeyWrapAlgorithm.RsaOaep256);

    [Fact]
    public void Properties_ReflectTheConstructorArguments()
    {
        var keys = new FakeKeys(Key, new FakeCrypto(Unwrapped));
        var credential = new FakeCredential();

        var key = new AzkvMasterKey("https://v.vault.azure.net", "sops", "share", "v1", keys, credential);

        Assert.Equal("azure_kv", key.KeyType);
        Assert.Equal("https://v.vault.azure.net", key.VaultUrl);
        Assert.Equal("sops", key.KeyName);
        Assert.Equal("v1", key.KeyVersion);
        Assert.Equal("share", key.EncryptedShare);
        Assert.Same(keys, key.Client);
        Assert.Same(credential, key.Credential);
    }

    [Fact]
    public async Task DecryptShare_LatestVersion_UnwrapsWithRsaOaep256()
    {
        var crypto = new FakeCrypto(enc => Unwrapped([.. enc, 99]));
        var keys = new FakeKeys(Key, crypto);
        var key = new AzkvMasterKey("https://v.vault.azure.net", "sops", client: keys);

        var data = await key.DecryptShareAsync(Convert.ToBase64String([1, 2, 3]));

        Assert.Equal(new byte[] { 1, 2, 3, 99 }, data);
        Assert.Equal(KeyWrapAlgorithm.RsaOaep256, crypto.Algorithm);
        Assert.Equal(("sops", null), Assert.Single(keys.GetKeyCalls));
        Assert.Equal(("sops", "latest"), keys.CryptoRequested);
    }

    [Fact]
    public async Task DecryptShare_PinnedVersion_AsksForThatVersion()
    {
        var keys = new FakeKeys(Key, new FakeCrypto(Unwrapped));
        var key = new AzkvMasterKey("https://v.vault.azure.net", "sops", keyVersion: "v7", client: keys);

        await key.DecryptShareAsync(Convert.ToBase64String([1]));

        Assert.Equal(("sops", "v7"), Assert.Single(keys.GetKeyCalls));
    }

    [Fact]
    public async Task DecryptShare_AccessDenied_ReturnsNull()
    {
        var keys = new FakeKeys(_ => throw new RequestFailedException(403, "forbidden"), new FakeCrypto(Unwrapped));

        Assert.Null(await new AzkvMasterKey("https://v", "sops", client: keys).DecryptShareAsync(Convert.ToBase64String([1])));
    }

    [Fact]
    public async Task DecryptShare_OtherFailure_ReturnsNull()
    {
        var keys = new FakeKeys(Key, new FakeCrypto(_ => throw new InvalidOperationException("boom")));

        Assert.Null(await new AzkvMasterKey("https://v", "sops", client: keys).DecryptShareAsync(Convert.ToBase64String([1])));
    }

    [Fact]
    public async Task DecryptShare_InvalidBase64_ReturnsNull()
    {
        var keys = new FakeKeys(Key, new FakeCrypto(Unwrapped));

        Assert.Null(await new AzkvMasterKey("https://v", "sops", client: keys).DecryptShareAsync("not base64 !!"));
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
