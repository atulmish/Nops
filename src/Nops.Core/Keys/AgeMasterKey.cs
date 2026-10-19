using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Age;
using Age.Recipients;

namespace Nops.Core.Keys;

/// <summary>
/// Master key provider for age (age-encryption.org/v1) keys.
/// Uses AgeSharp for X25519 + scrypt/ML-KEM-768 + ChaCha20-Poly1305 decryption.
/// </summary>
public class AgeMasterKey : IMasterKeyProvider
{
    public string KeyType => "age";

    /// <summary>
    /// The age recipient (bech32 string starting with "age1..."). Optional; used only
    /// as an optimization hint - the private key's own recipient determines which
    /// stanzas it can unwrap.
    /// </summary>
    public string? Recipient { get; }

    /// <summary>
    /// The age private key (Bech32 string starting with "AGE-SECRET-KEY-1...").
    /// </summary>
    public string? PrivateKey { get; }

    public AgeMasterKey(string? recipient, string? privateKey = null)
    {
        Recipient = recipient;
        PrivateKey = privateKey;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using age.
    /// Returns null if this key cannot decrypt (no private key or parse error).
    /// The identity only matches stanzas addressed to its own public key, so no
    /// explicit recipient comparison is needed.
    /// </summary>
    public Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(enc))
            return Task.FromResult<byte[]?>(null);

        if (string.IsNullOrEmpty(PrivateKey))
            return Task.FromResult<byte[]?>(null);

        X25519Identity? identity = null;
        try
        {
            identity = X25519Identity.Parse(PrivateKey);
            if (identity == null)
                return Task.FromResult<byte[]?>(null);

            using var input = new MemoryStream(Encoding.UTF8.GetBytes(enc));
            using var output = new MemoryStream();
            AgeEncrypt.Decrypt(input, output, new[] { identity });
            return Task.FromResult<byte[]?>(output.ToArray());
        }
        catch (Exception)
        {
            // Parse errors, no matching stanza, or other issues → try next key.
            return Task.FromResult<byte[]?>(null);
        }
        finally
        {
            identity?.Dispose();
        }
    }
}
