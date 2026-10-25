using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nops.Core.Crypto;
using Nops.Core.Stores;

namespace Nops.Core.Keys;

/// <summary>
/// Interface for master key providers (age, PGP, KMS, etc.).
/// Each provider knows how to decrypt the data key share encrypted under its key.
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>
    /// Key type identifier (e.g., "age", "pgp", "aws_kms", "gcp_kms", "azure_kv", "hc_vault").
    /// </summary>
    string KeyType { get; }

    /// <summary>
    /// Decrypt the encrypted data key share using this master key.
    /// Returns null if this key cannot decrypt the share.
    /// </summary>
    Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default);
}

/// <summary>
/// Resolves the SOPS data key from the key groups in the .sops metadata.
/// Handles both single-group (any key works) and multi-group (Shamir secret sharing).
/// </summary>
internal static class DataKeyResolver
{
    /// <summary>
    /// Resolve the 32-byte data key from SOPS metadata and available master keys.
    /// </summary>
    public static async Task<byte[]> ResolveAsync(
        SopsMetadata metadata,
        IReadOnlyList<IMasterKeyProvider> providers,
        CancellationToken ct = default)
    {
        var groups = ResolveKeyGroups(metadata);

        if (groups.Count == 0)
            throw new InvalidOperationException("No master keys available for decryption");

        // Single key group: the whole data key is encrypted under each master key;
        // any one success recovers it (Shamir is not used).
        // An explicit "unsupported key / missing passphrase" error from one provider must not
        // stop other keys being tried; it is only surfaced if nothing else succeeds.
        NotSupportedException? unsupported = null;
        Exception? lastError = null;

        if (groups.Count == 1)
        {
            var dataKey = await TryDecryptGroupAsync(groups[0], providers, ct, singleGroup: true,
                ex => unsupported ??= ex, ex => lastError = ex);
            if (dataKey != null)
                return dataKey;

            if (unsupported != null)
                throw unsupported;

            throw new InvalidOperationException(
                $"Cannot decrypt: none of the {providers.Count} configured master key(s) could decrypt the data key. " +
                DescribeRecipients(groups) + DescribeLastError(lastError));
        }

        // Multiple key groups: Shamir secret sharing. Collect >= threshold shares.
        var threshold = metadata.ShamirThreshold > 0 ? metadata.ShamirThreshold : groups.Count;
        var collected = new List<byte[]>();

        foreach (var group in groups)
        {
            var share = await TryDecryptGroupAsync(group, providers, ct, singleGroup: false,
                ex => unsupported ??= ex, ex => lastError = ex);
            if (share != null)
            {
                collected.Add(share);
                if (collected.Count >= threshold)
                    break;
            }
        }

        if (collected.Count < threshold && unsupported != null)
            throw unsupported;

        if (collected.Count < threshold)
            throw new InvalidOperationException(
                $"Cannot decrypt: need {threshold} shares but only got {collected.Count} from {groups.Count} key group(s). " +
                DescribeRecipients(groups) + DescribeLastError(lastError));

        return Shamir.Combine(collected.ToArray());
    }

    /// <summary>
    /// Use the on-disk key groups when present; otherwise treat the flat key lists
    /// as one implicit key group (mirrors sops.stores internalKeygroups).
    /// </summary>
    private static IReadOnlyList<KeyGroup> ResolveKeyGroups(SopsMetadata metadata)
    {
        if (metadata.KeyGroups is { Count: > 0 })
            return metadata.KeyGroups;

        var flat = new List<KeyGroupEntry>();
        foreach (var entry in metadata.Age)
            flat.Add(new KeyGroupEntry("age", entry.Enc));
        foreach (var entry in metadata.Pgp)
            flat.Add(new KeyGroupEntry("pgp", entry.Enc));
        foreach (var (type, list) in new[]
                 {
                     ("aws_kms", metadata.Kms),
                     ("gcp_kms", metadata.GcpKms),
                     ("azure_kv", metadata.AzureKv),
                     ("hckms", metadata.Hckms),
                     ("hc_vault", metadata.HcVault),
                 })
        {
            foreach (var opaque in list)
            {
                if (opaque is Dictionary<string, object> d && d.TryGetValue("enc", out var enc) &&
                    enc?.ToString() is { Length: > 0 } s)
                {
                    flat.Add(new KeyGroupEntry(type, s));
                }
            }
        }

        return flat.Count > 0 ? new List<KeyGroup> { new(flat) } : Array.Empty<KeyGroup>();
    }

    /// <summary>
    /// A provider that failed with an error (a tampered age blob, an unreachable KMS) rather than
    /// just "not my key": say so, because "none of the keys could decrypt" would hide the cause.
    /// </summary>
    private static string DescribeLastError(Exception? lastError) =>
        lastError == null ? "" : $" Last error: {lastError.GetType().Name}: {lastError.Message}";

    private static string DescribeRecipients(IReadOnlyList<KeyGroup> groups)
    {
        var types = groups.SelectMany(g => g.Keys)
            .GroupBy(k => k.KeyType, StringComparer.Ordinal)
            .Select(g => $"{g.Key} x{g.Count()}");
        return $"The file is encrypted for: {string.Join(", ", types)}. " +
            "Supply a matching key via AgeIdentities, PgpKeys or MasterKeys, or the sops " +
            "environment (SOPS_AGE_KEY, SOPS_AGE_KEY_FILE, ~/.config/sops/age/keys.txt).";
    }

    private static async Task<byte[]?> TryDecryptGroupAsync(
        KeyGroup group, IReadOnlyList<IMasterKeyProvider> providers, CancellationToken ct, bool singleGroup,
        Action<NotSupportedException> onUnsupported, Action<Exception> onFailure)
    {
        foreach (var entry in group.Keys)
        {
            foreach (var provider in providers)
            {
                if (!string.Equals(provider.KeyType, entry.KeyType, StringComparison.Ordinal))
                    continue;

                try
                {
                    var decrypted = await provider.DecryptShareAsync(entry.Enc, ct);
                    if (decrypted == null)
                        continue;

                    if (singleGroup)
                    {
                        if (decrypted.Length == 32)
                            return decrypted;
                    }
                    else if (decrypted.Length >= 32)
                    {
                        // Shamir share: 32-byte secret plus a trailing index byte.
                        return decrypted;
                    }
                }
                catch (NotSupportedException ex)
                {
                    // Explicit "unsupported key / missing passphrase": record it and keep trying.
                    onUnsupported(ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Remember why, then try the next master key.
                    onFailure(ex);
                }
            }
        }

        return null;
    }
}
