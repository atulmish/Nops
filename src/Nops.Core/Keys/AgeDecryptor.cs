using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc7748;
using BcChaCha20Poly1305 = Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305;

namespace Nops.Core.Keys;

/// <summary>
/// Decryption of age (age-encryption.org/v1) files for X25519 identities, which is what sops
/// uses to wrap its data key. Implements the header (stanzas and HMAC) and the
/// ChaCha20-Poly1305 STREAM payload as specified at https://age-encryption.org/v1 and is
/// checked against the C2SP age test vectors. Passphrase (scrypt), post-quantum and
/// plugin recipients are not supported.
/// </summary>
internal static class AgeDecryptor
{
    public const string SecretKeyHrp = "age-secret-key-";

    private const string Intro = "age-encryption.org/v1";
    private const int FileKeySize = 16;
    private const int TagSize = 16;
    private const int ChunkSize = 64 * 1024;
    private const int BytesPerBodyLine = 48;
    private const int MacSize = 32;

    private sealed record Stanza(string Type, string[] Args, byte[] Body);

    /// <summary>The file is well formed but none of its stanzas is addressed to the identity.</summary>
    internal sealed class AgeIdentityMismatchException(string message) : Exception(message);

    /// <summary>Parse an <c>AGE-SECRET-KEY-1...</c> string into the 32-byte X25519 scalar.</summary>
    public static bool TryParseIdentity(string text, out byte[] scalar)
    {
        scalar = [];
        var s = text.Trim();
        if (s != s.ToUpperInvariant())
            return false;
        if (!AgeBech32.TryDecode(s, out var hrp, out var data) || hrp != SecretKeyHrp || data.Length != 32)
            return false;
        scalar = data;
        return true;
    }

    /// <summary>
    /// Decrypt an age file (binary or armored) with the given identity.
    /// Throws <see cref="InvalidDataException"/> when the file is malformed, the identity does not
    /// match, or authentication fails.
    /// </summary>
    public static byte[] Decrypt(byte[] file, ReadOnlySpan<byte> identityScalar)
    {
        var data = AgeArmor.LooksArmored(file) ? AgeArmor.Decode(file) : file;

        var header = ParseHeader(data, out var mac, out var macInputLength, out var payloadStart);

        // An scrypt (passphrase) recipient must be the only one in the file.
        foreach (var stanza in header)
        {
            if (stanza.Type == "scrypt" && header.Count != 1)
                throw new InvalidDataException("age: an scrypt recipient must be the only recipient.");
        }

        // BouncyCastle's X25519 API takes arrays: make one copy and wipe it when done.
        var scalar = identityScalar.ToArray();
        var publicKey = new byte[32];
        X25519.ScalarMultBase(scalar, 0, publicKey, 0);

        byte[]? fileKey = null;
        try
        {
            foreach (var stanza in header)
            {
                fileKey = UnwrapX25519(stanza, scalar, publicKey);
                if (fileKey != null)
                    break;
            }

            if (fileKey == null)
                throw new AgeIdentityMismatchException("age: no recipient matches the identity.");

            var macKey = Hkdf(fileKey, [], "header"u8);
            try
            {
                var expected = HMACSHA256.HashData(macKey, data.AsSpan(0, macInputLength));
                if (!CryptographicOperations.FixedTimeEquals(expected, mac))
                    throw new InvalidDataException("age: header MAC mismatch.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(macKey);
            }

            return DecryptPayload(data, payloadStart, fileKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
            if (fileKey != null)
                CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    private static List<Stanza> ParseHeader(byte[] data, out byte[] mac, out int macInputLength, out int payloadStart)
    {
        var pos = 0;
        if (ReadLine(data, ref pos) != Intro)
            throw new InvalidDataException("age: unsupported or malformed header intro.");

        var stanzas = new List<Stanza>();
        string? type = null;
        string[] args = [];
        var body = new List<byte>();

        while (true)
        {
            var lineStart = pos;
            var line = ReadLine(data, ref pos);

            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                if (type != null)
                    throw new InvalidDataException("age: stanza body is not terminated by a short line.");
                var parts = line.Split(' ');
                if (parts.Length != 2 || parts[0] != "---")
                    throw new InvalidDataException("age: malformed header closing line.");
                mac = DecodeRawBase64(parts[1]);
                if (mac.Length != MacSize)
                    throw new InvalidDataException("age: malformed header MAC.");
                macInputLength = lineStart + 3;
                payloadStart = pos;
                return stanzas;
            }

            if (line.StartsWith("->", StringComparison.Ordinal))
            {
                if (type != null)
                    throw new InvalidDataException("age: stanza body is not terminated by a short line.");
                var parts = line.Split(' ');
                if (parts[0] != "->" || parts.Length < 2)
                    throw new InvalidDataException("age: malformed recipient stanza.");
                foreach (var part in parts.AsSpan(1))
                {
                    if (!IsValidArgument(part))
                        throw new InvalidDataException("age: malformed recipient stanza argument.");
                }

                type = parts[1];
                args = parts[2..];
                body.Clear();
                continue;
            }

            if (type == null)
                throw new InvalidDataException("age: unexpected line in header.");

            if (line.Length > 64)
                throw new InvalidDataException("age: stanza body line is too long.");
            var chunk = DecodeRawBase64(line);
            body.AddRange(chunk);
            if (chunk.Length < BytesPerBodyLine)
            {
                stanzas.Add(new Stanza(type, args, [.. body]));
                type = null;
            }
        }
    }

    /// <summary>Returns the file key, or null when the stanza is not addressed to this identity.</summary>
    private static byte[]? UnwrapX25519(Stanza stanza, byte[] identityScalar, byte[] publicKey)
    {
        if (stanza.Type != "X25519")
            return null;
        if (stanza.Args.Length != 1)
            throw new InvalidDataException("age: malformed X25519 recipient stanza.");

        var share = DecodeRawBase64(stanza.Args[0]);
        if (share.Length != 32)
            throw new InvalidDataException("age: malformed X25519 share.");

        var shared = new byte[32];
        if (!X25519.CalculateAgreement(identityScalar, 0, share, 0, shared, 0))
            throw new InvalidDataException("age: X25519 share produced a degenerate shared secret.");

        var salt = new byte[64];
        share.CopyTo(salt, 0);
        publicKey.CopyTo(salt, 32);
        var wrapKey = Hkdf(shared, salt, "age-encryption.org/v1/X25519"u8);
        CryptographicOperations.ZeroMemory(shared);

        if (stanza.Body.Length != FileKeySize + TagSize)
            throw new InvalidDataException("age: wrapped file key has unexpected length.");

        try
        {
            return ChaChaOpen(wrapKey, new byte[12], stanza.Body, 0, stanza.Body.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    private static byte[] DecryptPayload(byte[] data, int payloadStart, byte[] fileKey)
    {
        if (data.Length - payloadStart < 16)
            throw new InvalidDataException("age: payload is missing its nonce.");

        var salt = data.AsSpan(payloadStart, 16);
        var key = Hkdf(fileKey, salt, "payload"u8);
        byte[]? plaintext = null;
        try
        {
            var pos = payloadStart + 16;
            if (pos == data.Length)
                throw new InvalidDataException("age: payload has no chunks.");

            // The plaintext is never longer than the ciphertext: write into one buffer we can wipe.
            plaintext = new byte[data.Length - pos];
            var written = 0;

            var nonce = new byte[12];
            ulong counter = 0;
            while (true)
            {
                var remaining = data.Length - pos;
                var last = remaining <= ChunkSize + TagSize;
                var take = last ? remaining : ChunkSize + TagSize;
                if (take < TagSize)
                    throw new InvalidDataException("age: truncated payload chunk.");

                for (var i = 0; i < 8; i++)
                    nonce[3 + i] = (byte)(counter >> (8 * (7 - i)));
                nonce[11] = (byte)(last ? 1 : 0);

                var chunk = ChaChaOpen(key, nonce, data, pos, take)
                    ?? throw new InvalidDataException("age: payload authentication failed.");
                if (last && chunk.Length == 0 && counter != 0)
                    throw new InvalidDataException("age: final payload chunk is empty.");
                chunk.CopyTo(plaintext, written);
                written += chunk.Length;
                CryptographicOperations.ZeroMemory(chunk);

                if (last)
                {
                    return plaintext.AsSpan(0, written).ToArray();
                }

                pos += take;
                counter++;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext != null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Hkdf(byte[] ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info)
    {
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, key, salt, info);
        return key;
    }

    private static byte[]? ChaChaOpen(byte[] key, byte[] nonce, byte[] input, int offset, int length)
    {
        var cipher = new BcChaCha20Poly1305();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce));
        var output = new byte[cipher.GetOutputSize(length)];
        try
        {
            var n = cipher.ProcessBytes(input, offset, length, output, 0);
            n += cipher.DoFinal(output, n);
            return n == output.Length ? output : output.AsSpan(0, n).ToArray();
        }
        catch (InvalidCipherTextException)
        {
            CryptographicOperations.ZeroMemory(output);
            return null;
        }
    }

    private static string ReadLine(byte[] data, ref int pos)
    {
        var end = Array.IndexOf(data, (byte)'\n', pos);
        if (end < 0)
            throw new InvalidDataException("age: unexpected end of header.");
        var line = Encoding.Latin1.GetString(data, pos, end - pos);
        pos = end + 1;
        return line;
    }

    private static bool IsValidArgument(string s)
    {
        if (s.Length == 0)
            return false;
        foreach (var c in s)
        {
            if (c < 33 || c > 126)
                return false;
        }

        return true;
    }

    /// <summary>Strict, canonical, unpadded standard base64 (the form used throughout age headers).</summary>
    private static byte[] DecodeRawBase64(string s)
    {
        if (s.Contains('=') || s.Length % 4 == 1)
            throw new InvalidDataException("age: malformed base64.");
        try
        {
            var padded = s.PadRight((s.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(padded);
            if (Convert.ToBase64String(bytes).TrimEnd('=') != s)
                throw new InvalidDataException("age: base64 is not canonical.");
            return bytes;
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("age: malformed base64.", ex);
        }
    }
}
