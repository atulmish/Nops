using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nops.Core.Stores;

/// <summary>
/// Tree walker for SOPS encrypted files. Handles path computation, AAD generation,
/// and shouldBeEncrypted logic per the SOPS specification (exact port of
/// walkBranch/walkSlice/walkValue/shouldBeEncrypted from sops.go).
/// </summary>
public static class TreeWalker
{
    /// <summary>
    /// Compute Additional Authenticated Data (AAD) for GCM encryption.
    /// Per SOPS: additionalData = strings.Join(path, ":") + ":"
    /// Arrays do NOT add an index to path (all items share parent's AAD).
    /// </summary>
    public static string AdditionalData(IReadOnlyList<string> path)
    {
        return string.Join(":", path) + ":";
    }

    /// <summary>
    /// Determine whether a value at the given path should be encrypted.
    /// Exact port of tree.shouldBeEncrypted: rules apply sequentially and later
    /// rules override earlier ones. Comment regexes apply to values as well as
    /// comments (via the comments stack), not just to comment nodes.
    /// </summary>
    public static bool ShouldBeEncrypted(
        SopsMetadata metadata,
        IReadOnlyList<string> path,
        IReadOnlyList<IReadOnlyList<string>> commentsStack,
        bool isComment)
    {
        // Static Regex.IsMatch reuses the framework's regex cache, so each pattern is
        // parsed once rather than once per leaf.
        var encrypted = true;

        if (!string.IsNullOrEmpty(metadata.UnencryptedSuffix))
        {
            foreach (var element in path)
            {
                if (element.EndsWith(metadata.UnencryptedSuffix!, StringComparison.Ordinal))
                {
                    encrypted = false;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(metadata.EncryptedSuffix))
        {
            encrypted = false;
            foreach (var element in path)
            {
                if (element.EndsWith(metadata.EncryptedSuffix!, StringComparison.Ordinal))
                {
                    encrypted = true;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(metadata.UnencryptedRegex))
        {
            var pattern = metadata.UnencryptedRegex!;
            foreach (var element in path)
            {
                if (Regex.IsMatch(element, pattern))
                {
                    encrypted = false;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(metadata.EncryptedRegex))
        {
            encrypted = false;
            var pattern = metadata.EncryptedRegex!;
            foreach (var element in path)
            {
                if (Regex.IsMatch(element, pattern))
                {
                    encrypted = true;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(metadata.UnencryptedCommentRegex))
        {
            var pattern = metadata.UnencryptedCommentRegex!;
            foreach (var commentGroup in commentsStack)
            {
                var matched = false;
                foreach (var comment in commentGroup)
                {
                    if (Regex.IsMatch(comment, pattern))
                    {
                        encrypted = false;
                        matched = true;
                        break;
                    }
                }
                if (matched)
                    break;
            }
        }

        if (!string.IsNullOrEmpty(metadata.EncryptedCommentRegex))
        {
            var stackCount = commentsStack.Count;
            var lastCount = stackCount > 0 ? commentsStack[stackCount - 1].Count : 0;
            encrypted = false;
            var pattern = metadata.EncryptedCommentRegex!;
            for (int i = 0; i < stackCount && !encrypted; i++)
            {
                for (int j = 0; j < commentsStack[i].Count; j++)
                {
                    // The comment line itself which matches the regex is not
                    // encrypted; only values/comments after it are.
                    if (isComment && i == stackCount - 1 && j == lastCount - 1)
                        continue;
                    if (Regex.IsMatch(commentsStack[i][j], pattern))
                    {
                        encrypted = true;
                        break;
                    }
                }
            }
        }

        return encrypted;
    }
}
