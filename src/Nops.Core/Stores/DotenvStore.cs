using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for Dotenv files. Handles sops_* flat prefix and newline escaping.
/// </summary>
internal static class DotenvStore
{
    private const string MetadataFlattenFull = "sops_";

    /// <summary>
    /// Load a SOPS-encrypted Dotenv file.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var content = Encoding.UTF8.GetString(fileBytes);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var branch = new TreeBranch(isSequence: false);
        var metadataFlat = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith('#'))
            {
                // sops stores the comment text with the leading '#' removed only.
                branch.Add(new TreeItem(new Comment(line[1..]), null));
                continue;
            }

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = line[..eqIndex].Trim();
            var value = UnescapeNewlines(line[(eqIndex + 1)..]);

            if (key.StartsWith(MetadataFlattenFull, StringComparison.Ordinal))
            {
                metadataFlat[key[MetadataFlattenFull.Length..]] = value;
            }
            else
            {
                branch.Add(new TreeItem(key, value));
            }
        }

        var metadata = SopsMetadataParser.Parse(FlatMetadataDecoder.Decode(metadataFlat));
        return new SopsTree(new[] { branch }, metadata, filePath);
    }

    /// <summary>
    /// Flatten a tree to Dotenv format with sops_* prefix for .sops metadata.
    /// </summary>
    public static string FlattenToDotenv(SopsTree tree)
    {
        var lines = new List<string>();

        // .sops metadata with sops_ prefix (flat path format).
        for (var i = 0; i < tree.Metadata.Age.Count; i++)
        {
            var entry = tree.Metadata.Age[i];
            lines.Add($"{MetadataFlattenFull}age__list_{i}__map_enc={EscapeNewlines(entry.Enc)}");
            lines.Add($"{MetadataFlattenFull}age__list_{i}__map_recipient={EscapeNewlines(entry.Recipient)}");
        }

        for (var i = 0; i < tree.Metadata.Pgp.Count; i++)
        {
            var entry = tree.Metadata.Pgp[i];
            lines.Add($"{MetadataFlattenFull}pgp__list_{i}__map_enc={EscapeNewlines(entry.Enc)}");
            lines.Add($"{MetadataFlattenFull}pgp__list_{i}__map_fp={EscapeNewlines(entry.Fp)}");
        }

        if (tree.Metadata.Mac != null)
            lines.Add($"{MetadataFlattenFull}mac={EscapeNewlines(tree.Metadata.Mac)}");
        if (tree.Metadata.LastModified != null)
            lines.Add($"{MetadataFlattenFull}lastmodified={MacVerifier.FormatRfc3339(tree.Metadata.LastModified.Value)}");
        if (tree.Metadata.ShamirThreshold > 0)
            lines.Add($"{MetadataFlattenFull}shamir_threshold={tree.Metadata.ShamirThreshold}");
        if (tree.Metadata.UnencryptedSuffix != null)
            lines.Add($"{MetadataFlattenFull}unencrypted_suffix={EscapeNewlines(tree.Metadata.UnencryptedSuffix)}");
        if (tree.Metadata.Version != null)
            lines.Add($"{MetadataFlattenFull}version={EscapeNewlines(tree.Metadata.Version)}");

        // Regular key-value pairs.
        foreach (var branch in tree.Branches)
        {
            foreach (var item in branch)
            {
                if (item.Key is string key && item.Value is string value)
                    lines.Add($"{key}={EscapeNewlines(value)}");
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Unescape literal "\n" sequences back to newlines (SOPS dotenv encoding).
    /// </summary>
    public static string UnescapeNewlines(string value)
        => value.Replace("\\n", "\n");

    /// <summary>
    /// Escape newlines to literal "\n" sequences (SOPS dotenv encoding).
    /// </summary>
    public static string EscapeNewlines(string value)
        => value.Replace("\n", "\\n");
}
