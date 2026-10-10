using System;
using System.Collections.Generic;
using System.Text.Json;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for binary files (stored as JSON with {data: ENC[...]} format).
/// Read-only in MVP - encryption not supported.
/// </summary>
public static class BinaryStore
{
    /// <summary>
    /// Load a SOPS-encrypted binary file (JSON format with data field).
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var jsonDoc = JsonDocument.Parse(fileBytes);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty("data", out var dataElement))
            throw new InvalidOperationException("Binary file must have 'data' property");

        var branches = new List<TreeBranch>();
        var branch = new TreeBranch();

        // The encrypted data is stored as ENC[...] string
        var encValue = dataElement.GetString() ?? "";
        branch.Add(new TreeItem("data", encValue));

        branches.Add(branch);

        // Extract .sops metadata if present
        SopsMetadata metadata;
        if (root.TryGetProperty("sops", out var sopsElement))
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in sopsElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ToString();
            }
            metadata = SopsMetadataParser.Parse(dict);
        }
        else
        {
            metadata = new SopsMetadata(
                null, Array.Empty<AgeEntry>(), Array.Empty<PgpEntry>(),
                Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
                Array.Empty<object>(), Array.Empty<object>(),
                0, null, null, null, null, null, null, false, null, null);
        }

        return new SopsTree(branches, metadata, filePath);
    }

    /// <summary>
    /// Encrypt is not supported in MVP (encryption is a CI/CD concern).
    /// </summary>
    public static void Encrypt(SopsTree tree, byte[] plaintext)
    {
        throw new NotSupportedException("Binary encryption is not supported in MVP. Use 'sops -e' in CI/CD.");
    }
}
