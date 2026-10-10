using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nops.Core.Keys;

namespace Nops.Core.Keys;

/// <summary>
/// Represents a Vault Transit secrets engine key entry from .sops metadata.
/// </summary>
public sealed record VaultTransitEntry(
    string Address,
    string MountPath,
    string KeyName,
    string? Version,
    string Enc,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Master key provider for HashiCorp Vault Transit secrets engine.
/// Uses the Vault HTTP API to decrypt the encrypted data key share.
/// </summary>
public class VaultTransitMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("hc_vault").
    /// </summary>
    public string KeyType => "hc_vault";

    /// <summary>
    /// The Vault server address (e.g., https://vault.example.com:8200).
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// The transit secrets engine mount path (e.g., transit).
    /// </summary>
    public string MountPath { get; }

    /// <summary>
    /// The name of the transit key.
    /// </summary>
    public string KeyName { get; }

    /// <summary>
    /// Optional specific version of the key. If null, uses the latest version.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// The encrypted data key share (base64-encoded).
    /// </summary>
    public string EncryptedShare { get; }

    /// <summary>
    /// The Vault token for authentication.
    /// </summary>
    public string? Token { get; }

    /// <summary>
    /// Custom HttpClient for making requests. If null, uses a default client.
    /// </summary>
    public HttpClient? HttpClient { get; }

    /// <summary>
    /// Creates a new Vault Transit master key provider.
    /// </summary>
    public VaultTransitMasterKey(
        string address,
        string mountPath,
        string keyName,
        string encryptedShare,
        string? version = null,
        string? token = null,
        HttpClient? httpClient = null)
    {
        Address = address;
        MountPath = mountPath;
        KeyName = keyName;
        EncryptedShare = encryptedShare;
        Version = version;
        Token = token;
        HttpClient = httpClient;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using Vault Transit secrets engine.
    /// Returns null if decryption fails (key not found, access denied, etc.).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        var httpClient = HttpClient ?? CreateDefaultClient();

        // Build the decrypt endpoint URL
        var versionSegment = !string.IsNullOrEmpty(Version) ? $"/v1/{MountPath}/decrypt/{KeyName}/{Version}"
                                                            : $"/v1/{MountPath}/decrypt/{KeyName}";
        var url = Address.TrimEnd('/') + versionSegment;

        var payload = new Dictionary<string, object>
        {
            ["ciphertext"] = enc
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // Set authentication header
        if (!string.IsNullOrEmpty(Token))
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("X-Vault-Token", Token);
        }

        try
        {
            var response = await httpClient.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var jsonResponse = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            // Extract the plaintext ciphertext from the response
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("ciphertext", out var ctProp) &&
                ctProp.GetString() is string ciphertext)
            {
                return Convert.FromBase64String(ciphertext);
            }

            return null;
        }
        catch (HttpRequestException)
        {
            // Vault HTTP errors → null means "try next key"
            return null;
        }
        catch (Exception)
        {
            // Other exceptions → also try next key
            return null;
        }
    }

    private HttpClient CreateDefaultClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        return client;
    }
}

/// <summary>
/// Represents a HashiCorp Cloud KMS (HCKMS) key entry from .sops metadata.
/// HCKMS uses a different key format than Vault Transit.
/// </summary>
public sealed record HckmsEntry(
    string ProjectId,
    string Location,
    string KeyRing,
    string CryptoKey,
    string Enc,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Master key provider for HashiCorp Cloud KMS (HCKMS) keys used by SOPS.
/// HCKMS is HashiCorp's managed KMS service, similar to GCP KMS but with Vault integration.
/// </summary>
public class HckmsMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("hckms").
    /// </summary>
    public string KeyType => "hckms";

    /// <summary>
    /// The GCP project ID where the HCKMS key resides.
    /// </summary>
    public string ProjectId { get; }

    /// <summary>
    /// The GCP location (e.g., global, us-central1).
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// The key ring name.
    /// </summary>
    public string KeyRing { get; }

    /// <summary>
    /// The crypto key name.
    /// </summary>
    public string CryptoKey { get; }

    /// <summary>
    /// The encrypted data key share (base64-encoded).
    /// </summary>
    public string EncryptedShare { get; }

    /// <summary>
    /// Optional Vault token for authentication if HCKMS is accessed via Vault.
    /// </summary>
    public string? VaultToken { get; }

    /// <summary>
    /// Creates a new Vault HCKMS master key provider.
    /// </summary>
    public HckmsMasterKey(
        string projectId,
        string location,
        string keyRing,
        string cryptoKey,
        string encryptedShare,
        string? vaultToken = null)
    {
        ProjectId = projectId;
        Location = location;
        KeyRing = keyRing;
        CryptoKey = cryptoKey;
        EncryptedShare = encryptedShare;
        VaultToken = vaultToken;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using HCKMS.
    /// Returns null if decryption fails.
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        // HCKMS decrypts via GCP KMS API when accessed directly,
        // or via Vault Transit when accessed through a Vault connection.
        // For now, return null - real implementation would depend on auth method.

        if (VaultToken != null)
        {
            // Decrypt via Vault Transit endpoint that proxies to HCKMS
            var vaultAddress = Environment.GetEnvironmentVariable("VAULT_ADDR")
                ?? "https://hckms.hashicorp.cloud";

            var url = $"{vaultAddress.TrimEnd('/')}/v1/transit/decrypt/hckms/{CryptoKey}";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var payload = new Dictionary<string, object>
            {
                ["ciphertext"] = enc
            };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            httpClient.DefaultRequestHeaders.Add("X-Vault-Token", VaultToken);

            try
            {
                var response = await httpClient.PostAsync(url, content, ct);

                if (!response.IsSuccessStatusCode)
                    return null;

                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("ciphertext", out var ctProp) &&
                    ctProp.GetString() is string ciphertext)
                {
                    return Convert.FromBase64String(ciphertext);
                }

                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Direct GCP KMS path would go here if no Vault token
        return null;
    }
}

/// <summary>
/// Helper to parse opaque HashiCorp entries from .sops metadata.
/// </summary>
public static class VaultEntryParser
{
    /// <summary>
    /// Parse a single Vault Transit dict entry (from SopsMetadata.HcVault) into a VaultTransitEntry.
    /// </summary>
    public static VaultTransitEntry ParseVaultTransit(object entry)
    {
        if (entry is not Dictionary<string, object> dict)
            throw new ArgumentException("Vault Transit entry must be a dictionary", nameof(entry));

        string address = ExtractString(dict, "address") ?? throw new ArgumentException("Vault Transit entry missing 'address'", nameof(entry));
        string mountPath = ExtractString(dict, "mount_path") ?? ExtractString(dict, "mountpath") ?? "transit";
        string keyName = ExtractString(dict, "key") ?? throw new ArgumentException("Vault Transit entry missing 'key'", nameof(entry));
        string enc = ExtractString(dict, "enc") ?? throw new ArgumentException("Vault Transit entry missing 'enc'", nameof(entry));

        var version = ExtractString(dict, "version");
        var createdAt = ExtractDateTime(dict, "created_at");

        return new VaultTransitEntry(address, mountPath, keyName, version, enc, createdAt);
    }

    /// <summary>
    /// Parse a single HCKMS dict entry (from SopsMetadata.Hckms) into an HckmsEntry.
    /// </summary>
    public static HckmsEntry ParseHckms(object entry)
    {
        if (entry is not Dictionary<string, object> dict)
            throw new ArgumentException("HCKMS entry must be a dictionary", nameof(entry));

        string projectId = ExtractString(dict, "project_id") ?? throw new ArgumentException("HCKMS entry missing 'project_id'", nameof(entry));
        string location = ExtractString(dict, "location") ?? "global";
        string keyRing = ExtractString(dict, "key_ring") ?? ExtractString(dict, "keyring") ?? throw new ArgumentException("HCKMS entry missing 'key_ring'", nameof(entry));
        string cryptoKey = ExtractString(dict, "crypto_key") ?? ExtractString(dict, "cryptokey") ?? throw new ArgumentException("HCKMS entry missing 'crypto_key'", nameof(entry));
        string enc = ExtractString(dict, "enc") ?? throw new ArgumentException("HCKMS entry missing 'enc'", nameof(entry));

        var createdAt = ExtractDateTime(dict, "created_at");

        return new HckmsEntry(projectId, location, keyRing, cryptoKey, enc, createdAt);
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
