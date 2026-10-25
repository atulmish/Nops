using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nops.Core.Keys;

/// <summary>
/// Master key provider for age (age-encryption.org/v1) keys.
/// Decrypts X25519 recipients (what sops uses to wrap its data key); passphrase, post-quantum
/// and plugin recipients are not supported.
/// </summary>
public class AgeMasterKey : IMasterKeyProvider
{
    /// <summary>
    /// The key type identifier ("age").
    /// </summary>
    public string KeyType => "age";

    /// <summary>
    /// The age private key (Bech32 string starting with "AGE-SECRET-KEY-1...").
    /// </summary>
    public string PrivateKey { get; }

    /// <summary>
    /// Creates an age master key from a private key.
    /// </summary>
    /// <param name="privateKey">The age private key (Bech32 string starting with "AGE-SECRET-KEY-1...").</param>
    public AgeMasterKey(string privateKey)
    {
        PrivateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
    }

    /// <summary>
    /// Decrypt the encrypted data key share using age.
    /// Returns null if this key cannot decrypt it: the private key is not valid or the file is
    /// not addressed to it. A malformed or tampered age blob throws <see cref="InvalidDataException"/>.
    /// </summary>
    public Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(enc))
            return Task.FromResult<byte[]?>(null);

        byte[]? scalar = null;
        try
        {
            if (!AgeDecryptor.TryParseIdentity(PrivateKey, out scalar))
                return Task.FromResult<byte[]?>(null);

            return Task.FromResult<byte[]?>(AgeDecryptor.Decrypt(Encoding.UTF8.GetBytes(enc), scalar));
        }
        catch (AgeDecryptor.AgeIdentityMismatchException)
        {
            // The file is not addressed to this identity: let the next key try.
            return Task.FromResult<byte[]?>(null);
        }
        finally
        {
            if (scalar != null)
                CryptographicOperations.ZeroMemory(scalar);
        }
    }
}
