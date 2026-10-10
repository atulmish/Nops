using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using Nops.Core.Keys;

namespace Nops.Core.Keys;

/// <summary>
/// Master key provider for PGP (OpenPGP) keys.
/// Uses BouncyCastle OpenPGP to decrypt session keys.
/// </summary>
public class PgpMasterKey : IMasterKeyProvider
{
    public string KeyType => "pgp";

    /// <summary>
    /// The PGP fingerprint (hex string).
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// The PGP private key bytes (ASCII-armored or binary) or null if using external decrypt.
    /// </summary>
    public byte[]? PrivateKeyBytes { get; }

    /// <summary>
    /// Optional passphrase for the PGP private key.
    /// SOPS requires explicit passphrase handling - never silently ignore.
    /// </summary>
    public string? Passphrase { get; }

    public PgpMasterKey(string fingerprint, byte[]? privateKeyBytes = null, string? passphrase = null)
    {
        Fingerprint = fingerprint;
        PrivateKeyBytes = privateKeyBytes;
        Passphrase = passphrase;
    }

    /// <summary>
    /// Decrypt the encrypted data key share using PGP.
    /// Returns null if this key cannot decrypt (key ID mismatch or parse error).
    /// </summary>
    public async Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(enc))
            return null;

        // Must have a private key to decrypt
        if (PrivateKeyBytes == null || PrivateKeyBytes.Length == 0)
            return null;

        try
        {
            // Parse the ASCII-armored PGP message
            using var ms = new MemoryStream(PrivateKeyBytes);

            // Try to parse as armored first, then binary
            PgpSecretKeyRing? secretKeyRing = null;

            try
            {
                // Attempt armored parsing
                using var armoredReader = new ArmoredInputStream(ms);
                secretKeyRing = new PgpSecretKeyRing(armoredReader);
            }
            catch
            {
                // Try binary parsing
                ms.Position = 0;
                secretKeyRing = new PgpSecretKeyRing(ms);
            }

            if (secretKeyRing == null)
                return null;

            // Find matching secret key by fingerprint
            var targetFingerprint = Hex.Decode(Fingerprint.Replace(":", "").Replace("-", "").ToLower());

            PgpSecretKey? secretKey = null;
            foreach (var sk in secretKeyRing.GetSecretKeys())
            {
                var fp = sk.GetFingerprint();
                if (fp != null && fp.SequenceEqual(targetFingerprint))
                {
                    secretKey = sk;
                    break;
                }

                // Also try by key ID as fallback
                if (!string.IsNullOrEmpty(Fingerprint) && Fingerprint.Length == 16)
                {
                    var keyId = unchecked((long)Convert.ToUInt64(Fingerprint.Substring(Fingerprint.Length - 16), 16));
                    if (sk.KeyId == keyId)
                    {
                        secretKey = sk;
                        break;
                    }
                }
            }

            if (secretKey == null)
                return null;

            // Extract private key using passphrase
            PgpPrivateKey? privateKey = null;
            try
            {
                if (!string.IsNullOrEmpty(Passphrase))
                {
                    privateKey = secretKey.ExtractPrivateKey(Passphrase.ToCharArray());
                }
                else
                {
                    // No passphrase - try without encryption (unlikely for SOPS files)
                    throw new NotSupportedException(
                        "PGP private key requires a passphrase. SOPS does not support unencrypted PGP keys.");
                }
            }
            catch (NotSupportedException)
            {
                throw; // Re-throw known exceptions
            }
            catch
            {
                return null; // Wrong passphrase or other error → try next key
            }

            if (privateKey == null)
                return null;

            // Parse the encrypted data from SOPS metadata
            var encBytes = Encoding.UTF8.GetBytes(enc);

            using var encMs = new MemoryStream(encBytes);
            using var encArmored = new ArmoredInputStream(encMs);

            var pgpFactory = new PgpObjectFactory(encArmored);
            var pgpObj = pgpFactory.NextPgpObject();

            // Get PgpEncryptedDataList
            PgpEncryptedDataList? encDataList = null;
            if (pgpObj is PgpEncryptedDataList encryptedList)
            {
                encDataList = encryptedList;
            }
            else if (pgpObj is PgpCompressedData compressedData)
            {
                encDataList = new PgpObjectFactory(compressedData.GetDataStream()).NextPgpObject() as PgpEncryptedDataList;
            }

            if (encDataList == null)
                return null;

            // Find matching encrypted session key
            foreach (var encryptedKey in encDataList.GetEncryptedDataObjects())
            {
                if (encryptedKey is PgpPublicKeyEncryptedData pkesKey)
                {
                    // Check if this key matches our secret key
                    if (pkesKey.KeyId == privateKey.KeyId)
                    {
                        try
                        {
                            // Decrypt the message; its plaintext is the data key share
                            using var sessionStream = pkesKey.GetDataStream(privateKey);
                            using var buffer = new MemoryStream();
                            await sessionStream.CopyToAsync(buffer, ct);
                            return buffer.ToArray();
                        }
                        catch
                        {
                            continue; // Try next key
                        }
                    }
                }
            }

            return null;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
