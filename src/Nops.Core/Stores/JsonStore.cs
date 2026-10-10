using System;
using System.Collections.Generic;
using System.Text.Json;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for JSON files. Parses JSON into ordered TreeBranches and extracts .sops metadata.
/// </summary>
public static class JsonStore
{
    /// <summary>
    /// Load a SOPS-encrypted JSON file and parse it into an ordered tree.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var jsonDoc = JsonDocument.Parse(fileBytes);
        var root = jsonDoc.RootElement;

        var branches = ParseElement(root);
        var metadata = ExtractMetadata(branches);

        return new SopsTree(branches, metadata, filePath);
    }

    private static List<TreeBranch> ParseElement(JsonElement element)
    {
        var branches = new List<TreeBranch>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            var branch = new TreeBranch();
            foreach (var property in element.EnumerateObject())
            {
                var value = ParseJsonValue(property.Value);
                branch.Add(new TreeItem(property.Name, value));
            }
            branches.Add(branch);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                branches.AddRange(ParseElement(item));
            }
        }

        return branches;
    }

    private static object? ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => null, // Nested objects walked recursively
            JsonValueKind.Array => null, // Arrays walked recursively
            _ => null
        };
    }

    private static SopsMetadata ExtractMetadata(List<TreeBranch> branches)
    {
        if (branches.Count == 0 || branches[0].Count == 0)
            throw new InvalidOperationException("No branches found");

        var firstBranch = branches[0]!;
        var sopsEntry = firstBranch.FirstOrDefault(item =>
            item.Key?.ToString() == "sops");

        if (sopsEntry.Value! is string sopsStr && !string.IsNullOrEmpty(sopsStr) && sopsEntry.Key != null)
        {
            // .sops might be a nested object in JSON - parse it
            try
            {
                using var doc = JsonDocument.Parse(sopsStr);
                var dict = new Dictionary<string, object>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ToString();
                }
                return SopsMetadataParser.Parse(dict)!;
            }
            catch
            {
                // If parsing fails, return empty metadata
            }
        }

        return new SopsMetadata(
            null, Array.Empty<AgeEntry>(), Array.Empty<PgpEntry>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, null, null, null, null, null, null, false, null, null);
    }
}
