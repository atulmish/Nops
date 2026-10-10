using System;
using System.Collections.Generic;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for INI files. Section-oriented with ; and # comments.
/// </summary>
public static class IniStore
{
    /// <summary>
    /// Load a SOPS-encrypted INI file.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var content = System.Text.Encoding.UTF8.GetString(fileBytes);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var branches = new List<TreeBranch>();
        var currentSection = "DEFAULT";
        var sectionBranch = new TreeBranch();
        var metadataDict = new Dictionary<string, object>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed)) continue;

            // Comments
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                sectionBranch.Add(new TreeItem(new Comment(trimmed), null));
                continue;
            }

            // Section header
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                // Save previous section
                if (sectionBranch.Count > 0)
                    branches.Add(sectionBranch);

                currentSection = trimmed[1..^1];
                sectionBranch = new TreeBranch();
                continue;
            }

            // Key=value
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = $"{currentSection}.{trimmed[..eqIndex].Trim()}";
            var value = trimmed[(eqIndex + 1)..].Trim();

            sectionBranch.Add(new TreeItem(key, value));
        }

        // Add last section
        if (sectionBranch.Count > 0)
            branches.Add(sectionBranch);

        var metadata = SopsMetadataParser.Parse(metadataDict);
        return new SopsTree(branches, metadata, filePath);
    }
}
