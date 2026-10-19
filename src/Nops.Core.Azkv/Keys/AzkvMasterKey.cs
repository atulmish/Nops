using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Nops.Core.Keys;

namespace Nops.Core.Keys;

/// <summary>
/// Represents a Key Vault key entry from .sops metadata.
/// </summary>
public sealed record AzkvEntry(
    string VaultUrl,
    string KeyName,
    string? KeyVersion,
    string Enc,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Master key provider for Azure Key Vault keys used by SOPS.
/// Uses Azure.Security.KeyVault.Keys to unwrap the encrypted data key share.
/// </summary>
public class AzkvMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("azure_kv").
    /// </summary>
    public string KeyType => "azure_kv";

    /// <summary>
    /// The URL of the Key Vault (e.g., https://my-vault.vault.azure.net).
    /// </summary>
    public string VaultUrl { get; }

    /// <summary>
    /// The name of the key in the vault.
    /// </summary>
    public string KeyName { get; }

    /// <summary>
    /// Optional specific version of the key. If null, uses the latest version.
    /// </summary>
    public string? KeyVersion { get; }

    /// <summary>
    /// Informational only: decryption uses the encrypted share read from the SOPS file
    /// metadata, so this can be left empty.
    /// </summary>
    public string EncryptedShare { get; }

    /// <summary>
    /// The KeyClient used for decryption. If null, uses DefaultAzureCredential.
    /// </summary>
    public KeyClient? Client { get; }

    /// <summary>
    /// Optional token credential used to build the unwrap client. If null, uses DefaultAzureCredential.
    /// </summary>
    public TokenCredential? Credential { get; }

    /// <summary>
    /// Creates a new Azure Key Vault master key provider.
    /// </summary>
    public AzkvMasterKey(string vaultUrl, string keyName, string encryptedShare = "", string? keyVersion = null, KeyClient? client = null, TokenCredential? credential = null)
    {
        VaultUrl = vaultUrl;
        KeyName = keyName;
        KeyVersion = keyVersion;
        EncryptedShare = encryptedShare;
        Client = client;
        Credential = credential;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using Azure Key Vault.
    /// Returns null if this key cannot decrypt (key not found, access denied, etc.).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        var credential = Credential ?? new DefaultAzureCredential();
        var client = Client ?? new KeyClient(new Uri(VaultUrl), credential);

        try
        {
            KeyVaultKey key = string.IsNullOrEmpty(KeyVersion)
                ? await client.GetKeyAsync(KeyName, cancellationToken: ct)
                : await client.GetKeyAsync(KeyName, KeyVersion, cancellationToken: ct);

            var crypto = new CryptographyClient(key.Id, credential);
            var result = await crypto.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, Convert.FromBase64String(enc), ct);

            return result.Key;
        }
        catch (Azure.RequestFailedException)
        {
            // Azure KV errors (access denied, key not found, etc.) → null means "try next key"
            return null;
        }
        catch (Exception)
        {
            // Other exceptions (network, serialization) → also try next key
            return null;
        }
    }

}

/// <summary>
/// Helper to parse opaque Azure KV entries from .sops metadata into typed AzkvEntry objects.
/// </summary>
public static class AzkvEntryParser
{
    /// <summary>
    /// Parse a single opaque Azure KV dict entry (from SopsMetadata.AzureKv) into an AzkvEntry.
    /// Each entry is a Dictionary&lt;string, object&gt; with keys: vault, name, version, enc, created_at.
    /// </summary>
    public static AzkvEntry Parse(object entry)
    {
        if (entry is not Dictionary<string, object> dict)
            throw new ArgumentException("Azure KV entry must be a dictionary", nameof(entry));

        string vault = ExtractString(dict, "vault") ?? throw new ArgumentException("Azure KV entry missing 'vault'", nameof(entry));
        string name = ExtractString(dict, "name") ?? throw new ArgumentException("Azure KV entry missing 'name'", nameof(entry));
        string enc = ExtractString(dict, "enc") ?? throw new ArgumentException("Azure KV entry missing 'enc'", nameof(entry));

        var version = ExtractString(dict, "version");
        var createdAt = ExtractDateTime(dict, "created_at");

        return new AzkvEntry(vault, name, version, enc, createdAt);
    }

    private static string? ExtractString(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var val))
            return val?.ToString();
        return null;
    }

    private static DateTimeOffset? ExtractDateTime(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && val != null)
        {
            if (val is DateTimeOffset dt) return dt;
            if (DateTimeOffset.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return null;
    }
}
