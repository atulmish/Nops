using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto.Parameters;
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
    /// The PGP fingerprint (hex string). May be empty to match any key in the ring.
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

        if (PrivateKeyBytes == null || PrivateKeyBytes.Length == 0)
            return null;

        try
        {
            var secretKeyRing = LoadSecretKeyRing();
            if (secretKeyRing == null || !MatchesFingerprint(secretKeyRing))
                return null;

            var encBytes = Encoding.UTF8.GetBytes(enc);

            using var encMs = new MemoryStream(encBytes);
            using var encArmored = new ArmoredInputStream(encMs);

            var pgpFactory = new PgpObjectFactory(encArmored);
            var pgpObj = pgpFactory.NextPgpObject();

            PgpEncryptedDataList? encDataList = null;
            if (pgpObj is PgpEncryptedDataList encryptedList)
                encDataList = encryptedList;
            else if (pgpObj is PgpCompressedData compressedData)
                encDataList = new PgpObjectFactory(compressedData.GetDataStream()).NextPgpObject() as PgpEncryptedDataList;

            if (encDataList == null)
                return null;

            foreach (var encryptedKey in encDataList.GetEncryptedDataObjects())
            {
                if (encryptedKey is not PgpPublicKeyEncryptedData pkesKey)
                    continue;

                // gpg encrypts to the encryption subkey, not the primary key: look the
                // secret key up by the recipient key ID of each encrypted-data packet.
                var secretKey = secretKeyRing.GetSecretKey(pkesKey.KeyId);
                if (secretKey == null)
                    continue;

                var privateKey = ExtractPrivateKey(secretKey);
                if (privateKey == null)
                    continue;

                try
                {
                    using var clearStream = pkesKey.GetDataStream(privateKey);
                    var literal = ReadLiteralData(clearStream);
                    if (literal == null)
                        continue;

                    using var literalStream = literal.GetInputStream();
                    using var buffer = new MemoryStream();
                    await literalStream.CopyToAsync(buffer, ct);
                    return buffer.ToArray();
                }
                catch
                {
                    // Try next key
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

    /// <summary>
    /// The decrypted stream is itself an OpenPGP message (gpg normally compresses it):
    /// unwrap compressed packets down to the literal data that holds the data key.
    /// </summary>
    private static PgpLiteralData? ReadLiteralData(Stream clearStream)
    {
        var factory = new PgpObjectFactory(clearStream);
        PgpObject? obj;
        while ((obj = factory.NextPgpObject()) != null)
        {
            switch (obj)
            {
                case PgpLiteralData literal:
                    return literal;
                case PgpCompressedData compressed:
                    factory = new PgpObjectFactory(compressed.GetDataStream());
                    break;
                // Skip one-pass signature / signature / marker packets.
            }
        }

        return null;
    }

    private PgpSecretKeyRing? LoadSecretKeyRing()
    {
        using var ms = new MemoryStream(PrivateKeyBytes!);

        try
        {
            using var armoredReader = new ArmoredInputStream(ms);
            return new PgpSecretKeyRing(armoredReader);
        }
        catch
        {
            ms.Position = 0;
            return new PgpSecretKeyRing(ms);
        }
    }

    /// <summary>
    /// True when <see cref="Fingerprint"/> is empty or identifies the primary key or any
    /// subkey in the ring (by full fingerprint or trailing 64-bit key ID).
    /// </summary>
    private bool MatchesFingerprint(PgpSecretKeyRing secretKeyRing)
    {
        if (string.IsNullOrEmpty(Fingerprint))
            return true;

        var normalized = Fingerprint.Replace(":", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

        byte[]? targetFingerprint = null;
        if (normalized.Length % 2 == 0 && normalized.Length >= 2)
        {
            try
            {
                targetFingerprint = Hex.Decode(normalized);
            }
            catch (Exception)
            {
                // Malformed fingerprint: fall back to key-id matching only.
                targetFingerprint = null;
            }
        }

        long? targetKeyId = null;
        if (normalized.Length >= 16)
        {
            var idHex = normalized[^16..];
            if (ulong.TryParse(idHex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var keyIdValue))
            {
                targetKeyId = unchecked((long)keyIdValue);
            }
        }

        foreach (PgpSecretKey sk in secretKeyRing.GetSecretKeys())
        {
            if (targetFingerprint != null)
            {
                var fp = sk.PublicKey.GetFingerprint();
                if (fp != null && fp.SequenceEqual(targetFingerprint))
                    return true;
            }

            if (targetKeyId.HasValue && sk.KeyId == targetKeyId.Value)
                return true;
        }

        return false;
    }

    private PgpPrivateKey? ExtractPrivateKey(PgpSecretKey secretKey)
    {
        try
        {
            if (!string.IsNullOrEmpty(Passphrase))
                return secretKey.ExtractPrivateKey(Passphrase.ToCharArray());

            // Unencrypted key: extract with an empty passphrase. If the key is actually
            // encrypted, this throws - surface a clear error rather than silently ignoring.
            return secretKey.ExtractPrivateKey(Array.Empty<char>());
        }
        catch (PgpException) when (string.IsNullOrEmpty(Passphrase))
        {
            throw new NotSupportedException(
                "PGP private key is encrypted but no passphrase was supplied. " +
                "Provide a passphrase or use an unencrypted key.");
        }
        catch (PgpException)
        {
            // Wrong passphrase → try the next key.
            return null;
        }
        catch
        {
            return null;
        }
    }
}
