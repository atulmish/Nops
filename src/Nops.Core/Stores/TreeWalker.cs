using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nops.Core.Stores;

/// <summary>
/// Tree walker for SOPS encrypted files. Handles path computation, AAD generation,
/// and shouldBeEncrypted logic per the SOPS specification.
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
    /// Checks unencrypted_suffix, encrypted_suffix, unencrypted_regex, encrypted_regex,
    /// and comment regexes against path elements and comments stack.
    /// </summary>
    public static bool ShouldBeEncrypted(
        SopsMetadata metadata,
        IReadOnlyList<string> path,
        IReadOnlyList<IReadOnlyList<string>> commentsStack,
        bool isComment)
    {
        // Check unencrypted_suffix: any path element with this suffix → skip encryption
        if (!string.IsNullOrEmpty(metadata.UnencryptedSuffix))
        {
            foreach (var element in path)
            {
                if (element.EndsWith(metadata.UnencryptedSuffix!, StringComparison.Ordinal))
                    return false;
            }
        }

        // Check encrypted_suffix: default is to skip unless any element matches
        if (!string.IsNullOrEmpty(metadata.EncryptedSuffix))
        {
            bool hasMatch = false;
            foreach (var element in path)
            {
                if (element.EndsWith(metadata.EncryptedSuffix!, StringComparison.Ordinal))
                {
                    hasMatch = true;
                    break;
                }
            }
            if (!hasMatch)
                return false;
        }

        // Check unencrypted_regex: per-element regex match → skip encryption
        if (!string.IsNullOrEmpty(metadata.UnencryptedRegex))
        {
            var regex = new Regex(metadata.UnencryptedRegex);
            foreach (var element in path)
            {
                if (regex.IsMatch(element))
                    return false;
            }
        }

        // Check encrypted_regex: per-element regex match → encrypt only matching elements
        if (!string.IsNullOrEmpty(metadata.EncryptedRegex))
        {
            var regex = new Regex(metadata.EncryptedRegex);
            bool hasMatch = false;
            foreach (var element in path)
            {
                if (regex.IsMatch(element))
                {
                    hasMatch = true;
                    break;
                }
            }
            if (!hasMatch)
                return false;
        }

        // Check comment regexes (only for comment nodes)
        if (isComment && commentsStack.Count > 0)
        {
            // encrypted_comment_regex: skip the comment line that triggered the match
            if (!string.IsNullOrEmpty(metadata.EncryptedCommentRegex))
            {
                var regex = new Regex(metadata.EncryptedCommentRegex);
                foreach (var commentGroup in commentsStack)
                {
                    foreach (var comment in commentGroup)
                    {
                        if (regex.IsMatch(comment))
                            return false;
                    }
                }
            }

            // unencrypted_comment_regex: skip comments matching this pattern
            if (!string.IsNullOrEmpty(metadata.UnencryptedCommentRegex))
            {
                var regex = new Regex(metadata.UnencryptedCommentRegex);
                foreach (var commentGroup in commentsStack)
                {
                    foreach (var comment in commentGroup)
                    {
                        if (regex.IsMatch(comment))
                            return false;
                    }
                }
            }
        }

        // Default: encrypt
        return true;
    }
}
