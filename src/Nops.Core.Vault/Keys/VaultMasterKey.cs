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
internal sealed record VaultTransitEntry(
    string Address,
    string MountPath,
    string KeyName,
    string Enc,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Master key provider for HashiCorp Vault Transit secrets engine.
/// Uses the Vault HTTP API to decrypt the encrypted data key share.
/// </summary>
public class VaultTransitMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("hc_vault"), as written by sops.
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
    /// Informational only: decryption uses the encrypted share read from the SOPS file
    /// metadata, so this can be left empty.
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
        string encryptedShare = "",
        string? token = null,
        HttpClient? httpClient = null)
    {
        Address = address;
        MountPath = mountPath;
        KeyName = keyName;
        EncryptedShare = encryptedShare;
        Token = token;
        HttpClient = httpClient;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using Vault Transit secrets engine.
    /// Returns null if decryption fails (key not found, access denied, etc.).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        // A client we create is ours to dispose; a caller-supplied one is never modified or disposed.
        var ownsClient = HttpClient == null;
        var httpClient = HttpClient ?? CreateDefaultClient();

        // POST /v1/:mount/decrypt/:name. The key version is part of the ciphertext ("vault:v1:...").
        var url = $"{Address.TrimEnd('/')}/v1/{MountPath.Trim('/')}/decrypt/{Uri.EscapeDataString(KeyName)}";

        var payload = new Dictionary<string, object>
        {
            ["ciphertext"] = enc
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"),
        };

        // Per-request header: never touches DefaultRequestHeaders of a shared HttpClient.
        if (!string.IsNullOrEmpty(Token))
            request.Headers.Add("X-Vault-Token", Token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var jsonResponse = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            // Vault returns the decrypted data key as base64 in data.plaintext.
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("plaintext", out var ptProp) &&
                ptProp.GetString() is string plaintext)
            {
                return Convert.FromBase64String(plaintext);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Vault unreachable, malformed response, ...: null means "try the next key".
            return null;
        }
        finally
        {
            if (ownsClient)
                httpClient.Dispose();
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
/// Helper to parse opaque HashiCorp entries from .sops metadata.
/// </summary>
internal static class VaultEntryParser
{
    /// <summary>
    /// Parse a single Vault Transit dict entry (from SopsMetadata.HcVault) into a VaultTransitEntry.
    /// </summary>
    public static VaultTransitEntry ParseVaultTransit(object entry)
    {
        if (entry is not Dictionary<string, object> dict)
            throw new ArgumentException("Vault Transit entry must be a dictionary", nameof(entry));

        // Field names as written by sops: vault_address, engine_path, key_name, enc, created_at.
        string address = ExtractString(dict, "vault_address") ?? throw new ArgumentException("Vault Transit entry missing 'vault_address'", nameof(entry));
        string mountPath = ExtractString(dict, "engine_path") ?? "transit";
        string keyName = ExtractString(dict, "key_name") ?? throw new ArgumentException("Vault Transit entry missing 'key_name'", nameof(entry));
        string enc = ExtractString(dict, "enc") ?? throw new ArgumentException("Vault Transit entry missing 'enc'", nameof(entry));

        var createdAt = ExtractDateTime(dict, "created_at");

        return new VaultTransitEntry(address, mountPath, keyName, enc, createdAt);
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
