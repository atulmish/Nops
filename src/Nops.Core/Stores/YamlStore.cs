using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nops.Core.Stores;
using YamlDotNet.RepresentationModel;

namespace Nops.Core.Stores;

/// <summary>
/// Store for YAML files. Parses YAML into ordered TreeBranches and extracts .sops metadata.
/// </summary>
public static class YamlStore
{
    /// <summary>
    /// Load a SOPS-encrypted YAML file and parse it into an ordered tree.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(new MemoryStream(fileBytes));
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
            throw new InvalidOperationException("YAML document is empty");

        var root = yaml.Documents[0].RootNode;

        // Extract .sops metadata from branch 0
        var branches = ParseBranches(root);
        var metadata = ExtractMetadata(branches);

        return new SopsTree(branches, metadata, filePath);
    }

    private static List<TreeBranch> ParseBranches(YamlNode node)
    {
        var branches = new List<TreeBranch>();

        if (node is YamlMappingNode mapping)
        {
            var branch = new TreeBranch();
            foreach (var entry in mapping.Children)
            {
                var key = entry.Key.ToString() ?? "";
                var value = ParseValue(entry.Value);
                branch.Add(new TreeItem(key, value));
            }
            branches.Add(branch);
        }
        else if (node is YamlSequenceNode sequence)
        {
            foreach (var item in sequence.Children)
            {
                branches.AddRange(ParseBranches(item));
            }
        }

        return branches;
    }

    private static object? ParseValue(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlMappingNode mapping => null, // Nested mappings become null (walked recursively later)
            YamlSequenceNode sequence => null, // Arrays become null (walked recursively later)
            _ => null
        };
    }

    private static SopsMetadata ExtractMetadata(List<TreeBranch> branches)
    {
        if (branches.Count == 0 || branches[0].Count == 0)
            throw new InvalidOperationException("No branches found");

        // .sops metadata is in the first branch
        var firstBranch = branches[0]!;
        var sopsEntry = firstBranch.FirstOrDefault(item =>
            item.Key?.ToString() == "sops");

        if (sopsEntry.Value! is YamlMappingNode sopsNode && sopsEntry.Key != null)
        {
            var dict = new Dictionary<string, object>();
            foreach (var entry in sopsNode.Children)
            {
                dict[entry.Key.ToString() ?? ""] = entry.Value;
            }
            return SopsMetadataParser.Parse(dict)!;
        }

        // No .sops key found - return empty metadata
        return new SopsMetadata(
            null, Array.Empty<AgeEntry>(), Array.Empty<PgpEntry>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, null, null, null, null, null, null, false, null, null);
    }
}
