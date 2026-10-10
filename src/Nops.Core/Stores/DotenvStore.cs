using System;
using System.Collections.Generic;
using System.Linq;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for Dotenv files. Handles sops_* flat prefix and newline escaping.
/// </summary>
public static class DotenvStore
{
    private const string MetadataFlattenFull = "sops_";

    /// <summary>
    /// Load a SOPS-encrypted Dotenv file.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var content = System.Text.Encoding.UTF8.GetString(fileBytes);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var branches = new List<TreeBranch>();
        var branch = new TreeBranch();
        var metadataDict = new Dictionary<string, object>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                // Comments become Comment keys
                if (!string.IsNullOrWhiteSpace(line))
                    branch.Add(new TreeItem(new Comment(line.Trim()), null));
                continue;
            }

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = line[..eqIndex];
            var value = line[(eqIndex + 1)..];

            // Unescape \n in values
            value = value.Replace("\\n", "\n");

            // Strip sops_ prefix for regular keys
            if (key.StartsWith(MetadataFlattenFull))
            {
                metadataDict[key[MetadataFlattenFull.Length..]] = value;
            }
            else
            {
                branch.Add(new TreeItem(key, value));
            }
        }

        branches.Add(branch);

        var metadata = SopsMetadataParser.Parse(metadataDict);
        return new SopsTree(branches, metadata, filePath);
    }

    /// <summary>
    /// Flatten a tree to Dotenv format with sops_* prefix for .sops metadata.
    /// </summary>
    public static string FlattenToDotenv(SopsTree tree)
    {
        var lines = new List<string>();

        // Add .sops metadata with sops_ prefix
        foreach (var entry in tree.Metadata.Age)
            lines.Add($"{MetadataFlattenFull}age={entry.Recipient}:{entry.Enc}");

        foreach (var entry in tree.Metadata.Pgp)
            lines.Add($"{MetadataFlattenFull}pgp={entry.Fp}:{entry.Enc}");

        if (tree.Metadata.Version != null)
            lines.Add($"{MetadataFlattenFull}version={tree.Metadata.Version}");

        // Add regular key-value pairs
        foreach (var branch in tree.Branches)
        {
            foreach (var item in branch)
            {
                if (item.Key is string key && item.Value is string value)
                {
                    var escapedValue = value.Replace("\n", "\\n");
                    lines.Add($"{key}={escapedValue}");
                }
            }
        }

        return string.Join('\n', lines);
    }
}
