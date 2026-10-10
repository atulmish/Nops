using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Nops.Core.Keys;

namespace Nops.Core.Keys;

/// <summary>
/// Represents a KMS key entry from .sops metadata with its encryption context.
/// </summary>
public sealed record AwsKmsEntry(
    string Arn,
    string Enc,
    IReadOnlyList<KeyValuePair<string, string>> Context,
    DateTimeOffset? CreatedAt);

/// <summary>
/// Master key provider for AWS KMS keys used by SOPS.
/// Wraps AWSSDK.KMS Decrypt to unwrap the encrypted data key share.
/// </summary>
public class AwsKmsMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("aws_kms").
    /// </summary>
    public string KeyType => "aws_kms";

    /// <summary>
    /// The ARN of the KMS key (e.g., arn:aws:kms:us-east-1:123456789012:key/abcd-1234-5678-efgh)
    /// </summary>
    public string Arn { get; }

    /// <summary>
    /// The encrypted data key share (base64-encoded).
    /// </summary>
    public string EncryptedShare { get; }

    /// <summary>
    /// Optional encryption context key-value pairs. Must match what was used during encryption.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Context { get; }

    /// <summary>
    /// The KMS client used for decryption. If null, uses the default credential chain.
    /// </summary>
    public IAmazonKeyManagementService? Client { get; }

    /// <summary>
    /// Creates a new AWS KMS master key provider.
    /// </summary>
    public AwsKmsMasterKey(string arn, string encryptedShare, IReadOnlyList<KeyValuePair<string, string>>? context = null, IAmazonKeyManagementService? client = null)
    {
        Arn = arn;
        EncryptedShare = encryptedShare;
        Context = context ?? Array.Empty<KeyValuePair<string, string>>();
        Client = client;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using AWS KMS.
    /// Returns null if this key cannot decrypt (ARN/context mismatch handled by caller).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        // The 'enc' parameter is the encrypted data key from a specific master key entry.
        // We only decrypt if this entry's ARN matches (caller filters by ARN before calling).

        var client = Client ?? CreateDefaultClient();

        try
        {
            var request = new DecryptRequest
            {
                KeyId = Arn,
                CiphertextBlob = new System.IO.MemoryStream(Convert.FromBase64String(enc))
            };

            // Add encryption context if present (must match what was used during encryption)
            if (Context.Any())
            {
                request.EncryptionContext = Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            var response = await client.DecryptAsync(request, ct);

            if (response.Plaintext != null)
            {
                using var ms = new System.IO.MemoryStream();
                response.Plaintext.CopyTo(ms);
                return ms.ToArray();
            }

            return null;
        }
        catch (AmazonKeyManagementServiceException)
        {
            // KMS errors (access denied, key not found, etc.) → null means "try next key"
            return null;
        }
        catch (Exception)
        {
            // Other exceptions (network, serialization) → also try next key
            return null;
        }
    }

    private IAmazonKeyManagementService CreateDefaultClient()
    {
        // Use default credential chain and region resolution
        return new AmazonKeyManagementServiceClient();
    }
}

/// <summary>
/// Helper to parse opaque KMS entries from .sops metadata into typed AwsKmsEntry objects.
/// </summary>
public static class AwsKmsEntryParser
{
    /// <summary>
    /// Parse a single opaque KMS dict entry (from SopsMetadata.Kms) into an AwsKmsEntry.
    /// Each entry is a Dictionary&lt;string, object&gt; with keys: arn, enc, context, created_at.
    /// </summary>
    public static AwsKmsEntry Parse(object entry)
    {
        if (entry is not Dictionary<string, object> dict)
            throw new ArgumentException("KMS entry must be a dictionary", nameof(entry));

        string arn = ExtractString(dict, "arn") ?? throw new ArgumentException("KMS entry missing 'arn'", nameof(entry));
        string enc = ExtractString(dict, "enc") ?? throw new ArgumentException("KMS entry missing 'enc'", nameof(entry));

        var context = ParseContext(dict);
        var createdAt = ExtractDateTime(dict, "created_at");

        return new AwsKmsEntry(arn, enc, context, createdAt);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseContext(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("context", out var ctxObj) || ctxObj == null)
            return Array.Empty<KeyValuePair<string, string>>();

        if (ctxObj is Dictionary<string, object> ctxDict)
        {
            return ctxDict.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value?.ToString() ?? ""))
                         .ToList();
        }

        // Handle nested map structure: {"context": [{"key": "k", "value": "v"}, ...]}
        if (ctxObj is List<object> ctxList)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (var item in ctxList)
            {
                if (item is Dictionary<string, object> m)
                {
                    var key = ExtractString(m, "key") ?? "";
                    var value = ExtractString(m, "value") ?? "";
                    pairs.Add(new KeyValuePair<string, string>(key, value));
                }
            }
            return pairs;
        }

        return Array.Empty<KeyValuePair<string, string>>();
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
