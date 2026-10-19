using System;
using System.Globalization;
using System.Text;
using Nops.Core.Crypto;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Verifies the SOPS MAC (Message Authentication Code).
/// Uses SHA-512 (not HMAC) per SOPS specification. The MAC is computed over the
/// decrypted tree in walk order (see SopsFile); this class verifies it against the
/// stored MAC, which is itself encrypted with the data key using the file's
/// lastmodified timestamp (RFC3339) as additional authenticated data.
/// </summary>
public static class MacVerifier
{
    /// <summary>
    /// Fixed seed prefix used when mac_only_encrypted is true.
    /// This is the SHA-256 of the string "sops" (sops.go MACOnlyEncryptedInitialization).
    /// </summary>
    private static readonly byte[] MacOnlyEncryptedPrefix =
        Convert.FromHexString("8a3fd2ad54ce66527b1034f3d147be0b0b975b3bf44f72c6fdadec8176f27d69");

    /// <summary>
    /// The seed prefix, exposed for MAC computation.
    /// </summary>
    public static byte[] GetMacOnlyEncryptedPrefix() => MacOnlyEncryptedPrefix;

    /// <summary>
    /// Verify a computed MAC (uppercase hex) against the stored MAC.
    /// </summary>
    /// <param name="tree">The decrypted tree whose metadata holds the stored MAC.</param>
    /// <param name="dataKey">The 32-byte data key used to decrypt the stored MAC.</param>
    /// <param name="computedMacHex">The MAC computed over the decrypted plaintext (uppercase hex).</param>
    /// <param name="throwOnMismatch">
    /// When false, a mismatch is ignored (useful for diagnostic or best-effort
    /// decryption). Defaults to true.
    /// </param>
    public static void VerifyMac(
        SopsTree tree, byte[] dataKey, string computedMacHex, bool throwOnMismatch = true)
    {
        if (tree.Metadata.Mac == null)
            throw new InvalidOperationException("No MAC found in SOPS metadata");

        var expectedMac = DecryptStoredMac(tree.Metadata.Mac, dataKey, tree.Metadata);

        if (!ConstantTimeEquals(expectedMac, computedMacHex) && throwOnMismatch)
            throw new MacMismatchException($"MAC verification failed: expected {expectedMac}, got {computedMacHex}");
    }

    /// <summary>
    /// Format a timestamp exactly like Go's time.Format(time.RFC3339): UTC times
    /// render with a "Z" suffix, others with a numeric offset; sub-second digits
    /// are dropped.
    /// </summary>
    public static string FormatRfc3339(DateTimeOffset value)
    {
        var s = value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        return value.Offset == TimeSpan.Zero
            ? s + "Z"
            : s + value.ToString("zzz", CultureInfo.InvariantCulture);
    }

    private static string DecryptStoredMac(string encMac, byte[] dataKey, SopsMetadata metadata)
    {
        // The MAC is itself encrypted with the data key; the file's lastmodified
        // timestamp (RFC3339) is the additional authenticated data.
        var aad = metadata.LastModified != null
            ? FormatRfc3339(metadata.LastModified.Value)
            : string.Empty;

        var decrypted = AesGcmDecryptor.DecryptValue(encMac, dataKey, aad);
        if (decrypted is string macStr)
            return macStr.ToUpperInvariant();

        throw new InvalidOperationException("Failed to decrypt stored MAC");
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;

        var diff = 0;
        for (int i = 0; i < ab.Length; i++)
            diff |= ab[i] ^ bb[i];

        return diff == 0;
    }
}

/// <summary>
/// Exception thrown when MAC verification fails.
/// </summary>
public class MacMismatchException : Exception
{
    public MacMismatchException(string message) : base(message) { }
}
