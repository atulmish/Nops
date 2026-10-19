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

        var branch = new TreeBranch(isSequence: false);

        if (!root.TryGetProperty("data", out var dataElement))
            throw new InvalidOperationException("Binary file must have 'data' property");

        branch.Add(new TreeItem("data", dataElement.GetString() ?? ""));

        // The metadata.format field is also encrypted and part of the MAC.
        if (root.TryGetProperty("metadata", out var metaElement) &&
            metaElement.TryGetProperty("format", out var formatElement))
        {
            var metaBranch = new TreeBranch(isSequence: false);
            metaBranch.Add(new TreeItem("format", formatElement.GetString() ?? ""));
            branch.Add(new TreeItem("metadata", metaBranch));
        }

        var metadata = ExtractMetadata(root);

        return new SopsTree(new[] { branch }, metadata, filePath);
    }

    private static SopsMetadata ExtractMetadata(JsonElement root)
    {
        if (root.TryGetProperty("sops", out var sopsElement) && sopsElement.ValueKind == JsonValueKind.Object)
        {
            var sopsBranch = JsonStore.ParseJson(sopsElement) as TreeBranch;
            if (sopsBranch != null)
                return SopsMetadataParser.Parse(TreeConverter.ToDictionary(sopsBranch));
        }

        return SopsMetadataParser.Empty();
    }

    /// <summary>
    /// Encrypt is not supported in MVP (encryption is a CI/CD concern).
    /// </summary>
    public static void Encrypt(SopsTree tree, byte[] plaintext)
    {
        throw new NotSupportedException("Binary encryption is not supported in MVP. Use 'sops -e' in CI/CD.");
    }
}
