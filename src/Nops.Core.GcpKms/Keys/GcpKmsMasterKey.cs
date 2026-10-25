using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Nops.Core.Keys;

namespace Nops.Core.Keys;

/// <summary>
/// Represents a GCP KMS key entry from .sops metadata.
/// </summary>
internal sealed record GcpKmsEntry(
    string ResourceName,
    string Enc,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Master key provider for Google Cloud KMS keys used by SOPS.
/// Uses Google.Cloud.Kms.V1 to decrypt the encrypted data key share.
/// </summary>
public class GcpKmsMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("gcp_kms").
    /// </summary>
    public string KeyType => "gcp_kms";

    /// <summary>
    /// The full resource name of the KMS key (e.g., projects/my-project/locations/global/keyRings/my-kr/cryptoKeys/my-key).
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// Informational only: decryption uses the encrypted share read from the SOPS file
    /// metadata, so this can be left empty.
    /// </summary>
    public string EncryptedShare { get; }

    /// <summary>
    /// The CloudKMS client used for decryption. If null, uses Application Default Credentials.
    /// </summary>
    public KeyManagementServiceClient? Client { get; }

    /// <summary>
    /// Creates a new GCP KMS master key provider.
    /// </summary>
    public GcpKmsMasterKey(string resourceName, string encryptedShare = "", KeyManagementServiceClient? client = null)
    {
        ResourceName = resourceName;
        EncryptedShare = encryptedShare;
        Client = client;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using Google Cloud KMS.
    /// Returns null if this key cannot decrypt (resource name mismatch handled by caller).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        try
        {
            var client = Client ?? CreateDefaultClient();
            var request = new DecryptRequest
            {
                Name = ResourceName,
                Ciphertext = ByteString.CopyFrom(Convert.FromBase64String(enc))
            };


            var response = await client.DecryptAsync(request, cancellationToken: ct);

            if (response.Plaintext != null)
                return response.Plaintext.ToByteArray();

            return null;
        }
        catch (Grpc.Core.RpcException)
        {
            // GCP KMS errors (permission denied, key not found, etc.) → null means "try next key"
            return null;
        }
        catch (Exception)
        {
            // Other exceptions (network, serialization) → also try next key
            return null;
        }
    }

    private KeyManagementServiceClient CreateDefaultClient()
    {
        // Uses Application Default Credentials (ADC)
        return KeyManagementServiceClient.Create();
    }
}

/// <summary>
/// Helper to parse opaque GCP KMS entries from .sops metadata into typed GcpKmsEntry objects.
/// </summary>
internal static class GcpKmsEntryParser
{
    /// <summary>
    /// Parse a single opaque GCP KMS dict entry (from SopsMetadata.GcpKms) into a GcpKmsEntry.
    /// Each entry is a Dictionary&lt;string, object&gt; with keys as written by sops: resource_id, enc, created_at.
    /// </summary>
    public static GcpKmsEntry Parse(object entry)
    {
        if (entry is not Dictionary<string, object> dict)
            throw new ArgumentException("GCP KMS entry must be a dictionary", nameof(entry));

        string resourceName = ExtractString(dict, "resource_id") ?? throw new ArgumentException("GCP KMS entry missing 'resource_id'", nameof(entry));
        string enc = ExtractString(dict, "enc") ?? throw new ArgumentException("GCP KMS entry missing 'enc'", nameof(entry));

        var createdAt = ExtractDateTime(dict, "created_at");

        return new GcpKmsEntry(resourceName, enc, createdAt);
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
