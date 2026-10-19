using System;
using System.Collections.Generic;
using System.IO;

namespace Nops.Core.Keys;

/// <summary>
/// Loads age private key identities from either a Bech32 key string
/// ("AGE-SECRET-KEY-...") or a path to a file that contains one or more such keys
/// (for example an <c>age-keygen</c> output file, comments are ignored).
/// </summary>
public static class AgeIdentityLoader
{
    private const string SecretPrefix = "AGE-SECRET-KEY-";

    /// <summary>
    /// Resolve a single identity spec into zero or more Bech32 private keys.
    /// Returns nothing when the spec is neither a key nor an existing file.
    /// </summary>
    public static IEnumerable<string> Load(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            yield break;

        var trimmed = spec.Trim();
        if (trimmed.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            yield return trimmed;
            yield break;
        }

        if (File.Exists(spec))
        {
            foreach (var line in File.ReadAllLines(spec))
            {
                var candidate = line.Trim();
                if (candidate.StartsWith(SecretPrefix, StringComparison.Ordinal))
                    yield return candidate;
            }
        }
    }
}
