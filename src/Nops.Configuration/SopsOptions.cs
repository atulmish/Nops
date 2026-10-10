using System;
using System.Collections.Generic;
using Nops.Core.Keys;

namespace Nops.Configuration;

/// <summary>
/// Options for SOPS configuration provider.
/// Fluent builder pattern — all properties are optional with sensible defaults.
/// </summary>
public class SopsOptions
{
    /// <summary>
    /// Path to the SOPS-encrypted file to load. Required.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// File kind/format detection: "yaml", "json", "dotenv", "ini", "binary".
    /// Auto-detected from file extension if null.
    /// </summary>
    public string? FileKind { get; set; }

    /// <summary>
    /// Age private key identities (Bech32 strings or file paths).
    /// </summary>
    public IReadOnlyList<string>? AgeIdentities { get; set; }

    /// <summary>
    /// PGP private keys for decryption.
    /// </summary>
    public IReadOnlyList<(byte[] Key, string? Passphrase)>? PgpKeys { get; set; }

    /// <summary>
    /// Master key providers for cloud KMS (AWS, GCP, Azure, Vault).
    /// Populated automatically from AgeIdentities/PgpKeys if not set.
    /// </summary>
    public IReadOnlyList<IMasterKeyProvider>? MasterKeys { get; set; }

    /// <summary>
    /// Whether to watch the file for changes and reload automatically.
    /// Default: false.
    /// </summary>
    public bool WatchForChanges { get; set; }

    /// <summary>
    /// Reload interval when WatchForChanges is true. Debounces rapid file changes.
    /// Default: 250ms.
    /// </summary>
    public TimeSpan? ReloadInterval { get; set; }

    /// <summary>
    /// Whether the file is optional (no exception if missing).
    /// Default: false — throws SopsException if file not found.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Error handling mode for missing or unreadable files.
    /// </summary>
    public FileNotFoundMode FileNotFoundMode { get; set; } = FileNotFoundMode.Throw;

    /// <summary>
    /// Maximum file size to read (default: 10MB). Larger files will be rejected.
    /// </summary>
    public long? MaxFileSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Custom decryptor callback for advanced use cases.
    /// If set, overrides all key provider options.
    /// </summary>
    public Func<byte[], string, CancellationToken, System.Threading.Tasks.Task<IReadOnlyDictionary<string, string>>>? DecryptCallback { get; set; }

    /// <summary>
    /// Build the master key providers from AgeIdentities and PgpKeys.
    /// Called automatically if MasterKeys is null when needed.
    /// </summary>
    public IReadOnlyList<IMasterKeyProvider> ResolveMasterKeys()
    {
        if (MasterKeys != null)
            return MasterKeys;

        var keys = new List<IMasterKeyProvider>();

        if (AgeIdentities != null)
        {
            foreach (var identity in AgeIdentities)
            {
                // If it looks like a Bech32 age key, create AgeMasterKey directly
                if (identity.StartsWith("AGE-SECRET-KEY-"))
                {
                    keys.Add(new Nops.Core.Keys.AgeMasterKey(identity, identity));
                }
                // Otherwise treat as file path to read identities from
            }
        }

        if (PgpKeys != null)
        {
            foreach (var (key, passphrase) in PgpKeys)
            {
                keys.Add(new Nops.Core.Keys.PgpMasterKey("", key, passphrase));
            }
        }

        return keys;
    }
}

/// <summary>
/// How to handle missing or unreadable SOPS files.
/// </summary>
public enum FileNotFoundMode
{
    /// <summary>
    /// Throw a SopsException if the file is not found or cannot be read.
    /// </summary>
    Throw,

    /// <summary>
    /// Log a warning and return empty configuration (no values loaded).
    /// </summary>
    WarnAndContinue,

    /// <summary>
    /// Silently ignore missing files — no values loaded, no warnings.
    /// </summary>
    Ignore
}
