using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Store for INI files. Section-oriented with ; and # comments.
/// Sections map to nested TreeBranches; the [sops] section carries metadata.
/// </summary>
internal static class IniStore
{
    private const string DefaultSection = "DEFAULT";

    /// <summary>
    /// Load a SOPS-encrypted INI file.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var content = Encoding.UTF8.GetString(fileBytes);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var root = new TreeBranch(isSequence: false);
        var metadataFlat = new Dictionary<string, string>();

        // Keys before the first [section] belong to the DEFAULT section, as in sops (go-ini).
        var currentSection = DefaultSection;

        // Comments attach to the key or section header that follows them.
        var pendingComments = new List<Comment>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                pendingComments.Add(new Comment(StripCommentChar(trimmed)));
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1].Trim();
                if (currentSection != "sops")
                    FlushComments(GetOrCreateSectionBranch(root, currentSection), pendingComments);
                pendingComments.Clear();
                continue;
            }

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            if (currentSection == "sops")
            {
                // Only the flattened metadata escapes newlines (e.g. the age "enc" armor);
                // data values are taken verbatim, like sops' ini store.
                metadataFlat[key] = DotenvStore.UnescapeNewlines(value);
                pendingComments.Clear();
            }
            else
            {
                var sectionBranch = GetOrCreateSectionBranch(root, currentSection);
                FlushComments(sectionBranch, pendingComments);
                sectionBranch.Add(new TreeItem(key, value));
            }
        }

        if (pendingComments.Count > 0 && currentSection != "sops")
            FlushComments(GetOrCreateSectionBranch(root, currentSection), pendingComments);

        var metadata = SopsMetadataParser.Parse(FlatMetadataDecoder.Decode(metadataFlat));
        return new SopsTree(new[] { root }, metadata, filePath);
    }

    private static void FlushComments(TreeBranch sectionBranch, List<Comment> comments)
    {
        foreach (var comment in comments)
            sectionBranch.Add(new TreeItem(comment, null));
        comments.Clear();
    }

    private static TreeBranch GetOrCreateSectionBranch(TreeBranch root, string section)
    {
        var item = root.FirstOrDefault(i => i.Key?.ToString() == section);
        if (item?.Value is TreeBranch branch)
            return branch;

        var newBranch = new TreeBranch(isSequence: false);
        root.Add(new TreeItem(section, newBranch));
        return newBranch;
    }

    /// <summary>
    /// Strip the leading comment marker (; or #) and surrounding whitespace, matching
    /// sops' ini store.
    /// </summary>
    private static string StripCommentChar(string comment)
    {
        if (comment.Length > 0 && (comment[0] == ';' || comment[0] == '#'))
            comment = comment[1..];
        return comment.Trim();
    }
}
