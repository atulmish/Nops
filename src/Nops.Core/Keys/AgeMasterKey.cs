using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Age;
using Age.Format;
using Age.Recipients;
using Nops.Core.Keys;

namespace Nops.Core.Keys;

/// <summary>
/// Master key provider for age (age-encryption.org/v1) keys.
/// Uses AgeSharp for X25519 + scrypt/ML-KEM-768 + ChaCha20-Poly1305 decryption.
/// </summary>
public class AgeMasterKey : IMasterKeyProvider
{
    public string KeyType => "age";

    /// <summary>
    /// The age recipient (bech32 string starting with "age1...").
    /// </summary>
    public string? Recipient { get; }

    /// <summary>
    /// The age private key (Bech32 string starting with "AGE-SECRET-KEY-1...") or null if using external decrypt.
    /// </summary>
    public string? PrivateKey { get; }

    public AgeMasterKey(string? recipient, string? privateKey = null)
    {
        Recipient = recipient;
        PrivateKey = privateKey;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using age.
    /// Returns null if this key cannot decrypt (recipient mismatch or parse error).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(enc))
            return null;

        // Must have a private key to decrypt
        if (string.IsNullOrEmpty(PrivateKey))
            return null;

        try
        {
            // Parse the age stanza header
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(enc));
            var header = AgeHeader.Parse(ms);

            // Find matching recipient in the header
            for (int i = 0; i < header.RecipientCount; i++)
            {
                var stanza = header.Recipients[i];

                // Try to unwrap with this stanza
                var unwrapped = await UnwrapWithIdentity(stanza, ct);
                if (unwrapped != null)
                    return unwrapped;
            }

            return null;
        }
        catch (Exception)
        {
            // Parse errors or other issues → try next key
            return null;
        }
    }

    private async Task<byte[]?> UnwrapWithIdentity(Stanza stanza, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(PrivateKey))
            return null;

        X25519Identity? identity = null;

        try
        {
            // Parse the Bech32 private key to create an X25519Identity
            identity = X25519Identity.Parse(PrivateKey);

            if (identity == null)
                return null;

            // Check if this identity matches our recipient
            if (Recipient != null)
            {
                var recipientStr = identity.Recipient.ToString();
                if (!recipientStr.Equals(Recipient, StringComparison.Ordinal))
                    return null;
            }

            // Unwrap the encrypted data key
            var result = identity.Unwrap(stanza);

            // Clean up
            identity.Dispose();

            return result;
        }
        catch (Exception)
        {
            identity?.Dispose();
            return null;
        }
    }
}
