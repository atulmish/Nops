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
    /// Key type identifier (e.g., "age", "pgp", "aws_kms", etc.)
    /// </summary>
    string KeyType { get; }

    /// <summary>
    /// Decrypt the encrypted data key share using this master key.
    /// Returns null if this key cannot decrypt the share.
    /// </summary>
    Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default);
}

/// <summary>
/// Resolves the SOPS data key from master keys in the .sops metadata.
/// Handles both single-group (any key works) and multi-group (Shamir secret sharing).
/// </summary>
public static class DataKeyResolver
{
    /// <summary>
    /// Resolve the 32-byte data key from SOPS metadata and available master keys.
    /// </summary>
    public static async Task<byte[]> ResolveAsync(
        SopsMetadata metadata,
        IReadOnlyList<IMasterKeyProvider> providers,
        CancellationToken ct = default)
    {
        // Build key groups from metadata + providers
        var keyGroups = BuildKeyGroups(metadata, providers);

        if (keyGroups.Count == 0)
            throw new InvalidOperationException("No master keys available for decryption");

        // Determine threshold
        int threshold;
        if (metadata.ShamirThreshold > 0)
            threshold = metadata.ShamirThreshold;
        else
            threshold = keyGroups.Count; // Default: all groups required

        // Try to collect shares from each group
        var collectedShares = new List<byte[]>();
        var allErrors = new List<Exception>();

        foreach (var group in keyGroups)
        {
            byte[]? share = null;
            foreach (var key in group)
            {
                try
                {
                    // Try to decrypt the data key with this master key
                    // For single-group, we look for a non-encrypted "data_key" field
                    // For multi-group, each key has an encrypted share
                    var decrypted = await TryDecryptDataKey(key, metadata, ct);
                    if (decrypted != null)
                    {
                        share = decrypted;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    allErrors.Add(ex);
                }
            }

            if (share != null)
                collectedShares.Add(share);
        }

        // Check if we have enough shares
        if (collectedShares.Count < threshold)
        {
            throw new InvalidOperationException(
                $"Cannot decrypt: need {threshold} shares but only got {collectedShares.Count}. " +
                (allErrors.Any() ? $"Errors: {string.Join("; ", allErrors.Take(3))}" : ""));
        }

        // Single group or enough shares: return the first successful decryption
        if (keyGroups.Count == 1 || collectedShares.Count >= threshold && threshold == 1)
        {
            return collectedShares[0];
        }

        // Multi-group Shamir: combine shares (prepend x-coordinates)
        if (collectedShares.Count >= threshold)
        {
            var sharesWithIndex = collectedShares.Select((share, i) =>
            {
                var withIndex = new byte[share.Length + 1];
                withIndex[0] = (byte)(i + 1); // x-coordinate
                Array.Copy(share, 0, withIndex, 1, share.Length);
                return withIndex;
            }).ToArray();

            return Shamir.Combine(sharesWithIndex);
        }

        throw new InvalidOperationException("Insufficient shares to reconstruct data key");
    }

    private static List<List<IMasterKeyProvider>> BuildKeyGroups(
        SopsMetadata metadata, IReadOnlyList<IMasterKeyProvider> providers)
    {
        // For MVP: each provider is its own key group.
        // In real SOPS, a key group has multiple keys that can decrypt the same share.
        // For Shamir secret sharing, you need one share from each group.
        return providers.Select(p => new List<IMasterKeyProvider> { p }).ToList();
    }

    private static async Task<byte[]?> TryDecryptDataKey(
        IMasterKeyProvider provider, SopsMetadata metadata, CancellationToken ct)
    {
        // Try each age entry's enc field
        foreach (var ageEntry in metadata.Age)
        {
            var decrypted = await provider.DecryptShareAsync(ageEntry.Enc, ct);
            if (decrypted != null && decrypted.Length == 32)
                return decrypted;
        }

        // Try each PGP entry's enc field  
        foreach (var pgpEntry in metadata.Pgp)
        {
            var decrypted = await provider.DecryptShareAsync(pgpEntry.Enc, ct);
            if (decrypted != null && decrypted.Length == 32)
                return decrypted;
        }

        // For testing: if provider has a direct data key set, use it
        if (provider is IDataKeyProvider ikp && ikp.DataKey != null && ikp.DataKey!.Length > 0)
            return ikp.DataKey;

        return null;
    }
}

/// <summary>
/// Marker interface for providers that have a direct data key (for testing).
/// </summary>
public interface IDataKeyProvider
{
    byte[]? DataKey { get; }
}
