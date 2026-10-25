using System;
using System.Collections.Generic;
using Nops.Core.Keys;

namespace Nops.Configuration;

/// <summary>
/// Options for SOPS configuration provider.
/// Fluent builder pattern - all properties are optional with sensible defaults.
/// </summary>
public class SopsConfigurationOptions
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
    /// AgeIdentities and PgpKeys are used in addition to these.
    /// </summary>
    public IReadOnlyList<IMasterKeyProvider>? MasterKeys { get; set; }

    /// <summary>
    /// Also use the age identities the sops CLI would find (<c>SOPS_AGE_KEY</c>,
    /// <c>SOPS_AGE_KEY_FILE</c>, default <c>sops/age/keys.txt</c>). Default <c>true</c>.
    /// </summary>
    public bool UseSopsEnvironment { get; set; } = true;

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
    /// Whether the file may be missing. When <c>true</c> and the file does not exist, the source
    /// contributes no values. Any other failure (wrong key, tampered file, unreadable file) still
    /// throws, so a required secret can never silently disappear. Default: <c>false</c>, which throws
    /// <see cref="System.IO.FileNotFoundException"/> if the file is missing.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Called when a reload triggered by <see cref="WatchForChanges"/> fails (for example the file was
    /// replaced with one the keys cannot decrypt). The previously loaded values stay in place. Use it to log.
    /// </summary>
    public Action<Exception>? OnReloadError { get; set; }

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
    /// Build the master key providers from AgeIdentities and PgpKeys, or return MasterKeys when set.
    /// </summary>
    internal IReadOnlyList<IMasterKeyProvider> ResolveMasterKeys()
    {
        if (MasterKeys != null)
            return MasterKeys;

        var keys = new List<IMasterKeyProvider>();

        if (AgeIdentities != null)
        {
            foreach (var identity in AgeIdentities)
            {
                // Accept a Bech32 "AGE-SECRET-KEY-..." string or a path to a file
                // containing one or more such keys.
                foreach (var key in Nops.Core.Keys.AgeIdentityLoader.Load(identity))
                    keys.Add(new Nops.Core.Keys.AgeMasterKey(key));
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
