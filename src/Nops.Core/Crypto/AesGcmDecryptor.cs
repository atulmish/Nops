using System;
using System.Text;
using Nops.Core.Stores;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Nops.Core.Crypto;

/// <summary>
/// Interface for AES-GCM decryption with 32-byte nonce support (SOPS standard).
/// </summary>
internal interface IAesGcmDecryptor
{
    /// <summary>
    /// Decrypt a 32-byte nonce AES-GCM ciphertext.
    /// </summary>
    byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] aad);
}

/// <summary>
/// BouncyCastle-based AES-GCM decryptor supporting 32-byte nonces (SOPS standard).
/// .NET AesGcm only supports 12-byte nonces, so we use BouncyCastle.GcmBlockCipher.
/// </summary>
internal class BouncyCastleAesGcmDecryptor : IAesGcmDecryptor
{
    public byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag, byte[] aad)
    {
        var aesEngine = new AesEngine();
        var gcmCipher = new GcmBlockCipher(aesEngine);

        // Combine ciphertext + tag (SOPS stores them separately, GCM expects combined)
        var ctWithTag = new byte[ciphertext.Length + tag.Length];
        Array.Copy(ciphertext, 0, ctWithTag, 0, ciphertext.Length);
        Array.Copy(tag, 0, ctWithTag, ciphertext.Length, tag.Length);

        var parameters = new AeadParameters(
            new KeyParameter(key),
            tag.Length * 8, // ICV length in bits (typically 128)
            nonce,
            aad);

        gcmCipher.Init(false, parameters); // false = decrypt

        var output = new byte[gcmCipher.GetOutputSize(ctWithTag.Length)];

        // GCM may emit plaintext incrementally: accumulate both chunks.
        var len = gcmCipher.ProcessBytes(ctWithTag, 0, ctWithTag.Length, output, 0);
        len += gcmCipher.DoFinal(output, len);

        return output[..len];
    }
}

/// <summary>
/// High-level decryptor for SOPS encrypted values.
/// Parses ENC[...] strings, decrypts with AES-GCM, and casts by type.
/// </summary>
internal static class AesGcmDecryptor
{
    private static readonly IAesGcmDecryptor Decryptor = new BouncyCastleAesGcmDecryptor();

    /// <summary>
    /// Decrypt an SOPS encrypted value string and cast to the appropriate .NET type.
    /// Returns null if the string is not a valid encrypted value.
    /// </summary>
    public static object? DecryptValue(string encString, byte[] dataKey, string? aad = null)
    {
        if (!EncryptedValueParser.TryParse(encString, out var ev) || ev is null)
            return null;

        var aadBytes = Encoding.UTF8.GetBytes(aad ?? string.Empty);
        byte[] plaintext;
        try
        {
            plaintext = Decryptor.Decrypt(dataKey, ev.Iv!, ev.Data!, ev.Tag!, aadBytes);
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException ex)
        {
            throw new MacMismatchException(
                "An encrypted value failed authentication: the file was modified without sops, or is corrupt.", ex);
        }

        return CastByType(plaintext, ev.DataType!);
    }

    private static object? CastByType(byte[] plaintext, string dataType)
    {
        if (dataType is null)
            throw new InvalidOperationException("Encrypted value type cannot be null");

        return dataType switch
        {
            "str" => Encoding.UTF8.GetString(plaintext),
            "int" => int.Parse(Encoding.UTF8.GetString(plaintext), System.Globalization.CultureInfo.InvariantCulture),
            "float" => double.Parse(Encoding.UTF8.GetString(plaintext), System.Globalization.CultureInfo.InvariantCulture),
            "bool" => Encoding.UTF8.GetString(plaintext) == "True" ? true : false,
            "bytes" => plaintext,
            "time" => DateTimeOffset.Parse(Encoding.UTF8.GetString(plaintext), System.Globalization.CultureInfo.InvariantCulture),
            "comment" => new Comment(Encoding.UTF8.GetString(plaintext)),
            _ => throw new InvalidOperationException($"Unknown SOPS encrypted value type: {dataType}")
        };
    }
}
