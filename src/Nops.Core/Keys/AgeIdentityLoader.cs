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

    /// <summary>
    /// Age identities from the same places the sops CLI looks: the <c>SOPS_AGE_KEY</c>
    /// variable (key text), the <c>SOPS_AGE_KEY_FILE</c> variable (a path), and
    /// <c>sops/age/keys.txt</c> under the user config directory. Missing sources are skipped.
    /// </summary>
    public static IEnumerable<string> LoadFromSopsEnvironment() =>
        LoadFromSopsEnvironment(Environment.GetEnvironmentVariable, File.Exists);

    internal static IEnumerable<string> LoadFromSopsEnvironment(
        Func<string, string?> getEnvironmentVariable, Func<string, bool> fileExists)
    {
        var keyText = getEnvironmentVariable("SOPS_AGE_KEY");
        if (!string.IsNullOrWhiteSpace(keyText))
        {
            foreach (var token in keyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith(SecretPrefix, StringComparison.Ordinal))
                    yield return token;
            }
        }

        var keyFile = getEnvironmentVariable("SOPS_AGE_KEY_FILE");
        if (!string.IsNullOrWhiteSpace(keyFile))
        {
            foreach (var key in Load(keyFile))
                yield return key;
        }

        var defaultFile = DefaultKeysFile(getEnvironmentVariable);
        if (defaultFile != null && fileExists(defaultFile))
        {
            foreach (var key in Load(defaultFile))
                yield return key;
        }
    }

    /// <summary>
    /// sops' default identities file: <c>$XDG_CONFIG_HOME/sops/age/keys.txt</c>, else the
    /// platform user config directory (<c>~/.config</c>, <c>~/Library/Application Support</c>
    /// or <c>%AppData%</c>).
    /// </summary>
    private static string? DefaultKeysFile(Func<string, string?> getEnvironmentVariable)
    {
        var configDir = getEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configDir))
        {
            if (OperatingSystem.IsWindows())
            {
                configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                    return null;
                configDir = OperatingSystem.IsMacOS()
                    ? Path.Combine(home, "Library", "Application Support")
                    : Path.Combine(home, ".config");
            }
        }

        return string.IsNullOrEmpty(configDir) ? null : Path.Combine(configDir, "sops", "age", "keys.txt");
    }
}
