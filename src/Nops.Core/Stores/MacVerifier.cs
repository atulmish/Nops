using System;
using System.Security.Cryptography;
using System.Text;
using Nops.Core.Crypto;
using Nops.Core.Stores;

namespace Nops.Core.Stores;

/// <summary>
/// Verifies the SOPS MAC (Message Authentication Code).
/// Uses SHA-512 (not HMAC) per SOPS specification.
/// </summary>
public static class MacVerifier
{
    /// <summary>
    /// Compute the MAC over a decrypted tree in walk order.
    /// </summary>
    public static string ComputeMac(SopsTree tree, byte[] dataKey)
    {
        var hash = SHA512.Create();

        // Seed with fixed prefix if mac_only_encrypted is true
        if (tree.Metadata.MacOnlyEncrypted)
        {
            var seed = Encoding.UTF8.GetBytes("sops");
            hash.TransformBlock(seed, 0, seed.Length, null, 0);
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }

        // Walk tree in order and hash ToBytes values
        foreach (var branch in tree.Branches)
        {
            WalkAndHash(branch, new[] { Array.Empty<string>() }, tree.Metadata, hash);
        }

        var macBytes = hash.ComputeHash(Array.Empty<byte>());
        return BitConverter.ToString(macBytes).Replace("-", "").ToUpper();
    }

    private static void WalkAndHash(TreeBranch branch, IReadOnlyList<IReadOnlyList<string>> commentsStack,
        SopsMetadata metadata, SHA512 hash)
    {
        foreach (var item in branch)
        {
            var path = new System.Collections.Generic.List<string>();

            if (item.Key is string keyStr)
                path.Add(keyStr);
            else if (item.Key is Comment comment)
            {
                // Comment keys/values must be walked
                var newComments = new List<IReadOnlyList<string>>(commentsStack);
                newComments.Add(new[] { comment.Value });
                WalkAndHash(item.Value as TreeBranch ?? new TreeBranch(), newComments, metadata, hash);
                continue;
            }

            if (item.Value is string encValue && EncryptedValueParser.TryParse(encValue, out var ev))
            {
                // This is an encrypted value - it will be decrypted later
                // For MAC computation, we hash the ToBytes of the plaintext after decryption
                // For now, skip (MAC verified after full decryption)
            }
            else if (item.Value != null)
            {
                var toBytes = ToBytes.Convert(item.Value);
                hash.TransformBlock(toBytes, 0, toBytes.Length, null, 0);
            }

            // Recurse into nested structures
            if (item.Value is TreeBranch subBranch)
            {
                WalkAndHash(subBranch, commentsStack, metadata, hash);
            }
        }
    }

    /// <summary>
    /// Verify the computed MAC against the stored MAC.
    /// </summary>
    public static void VerifyMac(SopsTree tree, byte[] dataKey)
    {
        if (tree.Metadata.Mac == null)
            throw new InvalidOperationException("No MAC found in SOPS metadata");

        var expectedMac = DecryptStoredMac(tree.Metadata.Mac, dataKey);
        var computedMac = ComputeMac(tree, dataKey);

        if (!ConstantTimeEquals(expectedMac, computedMac))
            throw new MacMismatchException($"MAC verification failed: expected {expectedMac}, got {computedMac}");
    }

    private static string DecryptStoredMac(string encMac, byte[] dataKey)
    {
        // The MAC is itself encrypted with the data key
        var decrypted = AesGcmDecryptor.DecryptValue(encMac, dataKey);
        if (decrypted is string macStr)
            return macStr.ToUpper();

        throw new InvalidOperationException("Failed to decrypt stored MAC");
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;

        var result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}

/// <summary>
/// Exception thrown when MAC verification fails.
/// </summary>
public class MacMismatchException : Exception
{
    public MacMismatchException(string message) : base(message) { }
}
