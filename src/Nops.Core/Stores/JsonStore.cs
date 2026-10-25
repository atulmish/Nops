using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for JSON files. Parses JSON into ordered TreeBranches (preserving nested
/// objects and arrays) and extracts .sops metadata.
/// </summary>
internal static class JsonStore
{
    /// <summary>
    /// Load a SOPS-encrypted JSON file and parse it into an ordered tree.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var jsonDoc = JsonDocument.Parse(fileBytes);
        var root = jsonDoc.RootElement;

        var branch = ParseJson(root) as TreeBranch
            ?? throw new InvalidOperationException("JSON root is not an object");

        var metadata = ExtractMetadata(branch);

        var sopsKeys = branch.Count(item => item.Key is string key && key == "sops");
        if (sopsKeys > 1)
            throw new SopsMetadataException("Found more than one 'sops' key at the top level of the file.");

        // The .sops key is metadata and must not appear in the decrypted data tree.
        branch.RemoveAll(item => item.Key is string key && key == "sops");

        return new SopsTree(new[] { branch }, metadata, filePath);
    }

    /// <summary>
    /// Parse a JSON element into an ordered tree object (TreeBranch / scalar).
    /// </summary>
    public static object? ParseJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var branch = new TreeBranch(isSequence: false);
                    foreach (var property in element.EnumerateObject())
                        branch.Add(new TreeItem(property.Name, ParseJson(property.Value)));
                    return branch;
                }
            case JsonValueKind.Array:
                {
                    var branch = new TreeBranch(isSequence: true);
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                        branch.Add(new TreeItem(index++, ParseJson(item)));
                    return branch;
                }
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                {
                    // Like sops: int64 when the literal fits, otherwise float64. Casting a
                    // whole double to long would lose precision or saturate past 2^63.
                    if (element.TryGetInt64(out var l))
                        return l;
                    return element.GetDouble();
                }
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    private static SopsMetadata ExtractMetadata(TreeBranch root)
    {
        var sopsItem = root.FirstOrDefault(item => item.Key is string key && key == "sops");

        if (sopsItem?.Value is TreeBranch sopsBranch)
            return SopsMetadataParser.Parse(TreeConverter.ToDictionary(sopsBranch));

        return SopsMetadataParser.Empty();
    }
}
