using System;
using System.Security.Cryptography;

namespace Nops.Core.Keys;

/// <summary>
/// Minimal Bech32 (BIP-173) decoder, used to read age identities
/// (<c>AGE-SECRET-KEY-1...</c>). age does not apply the 90-character limit.
/// </summary>
internal static class AgeBech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly uint[] Generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];

    /// <summary>Decode a Bech32 string. The human-readable part is returned lower-cased.</summary>
    public static bool TryDecode(string s, out string hrp, out byte[] data)
    {
        hrp = string.Empty;
        data = [];

        var hasLower = false;
        var hasUpper = false;
        foreach (var c in s)
        {
            if (c < 33 || c > 126)
                return false;
            hasLower |= char.IsAsciiLetterLower(c);
            hasUpper |= char.IsAsciiLetterUpper(c);
        }

        if (hasLower && hasUpper)
            return false;

        s = s.ToLowerInvariant();
        var sep = s.LastIndexOf('1');
        if (sep < 1 || s.Length - sep - 1 < 6)
            return false;

        // For secret keys these arrays hold key material: wipe them before returning.
        var values = new byte[s.Length - sep - 1];
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var v = Charset.IndexOf(s[sep + 1 + i]);
                if (v < 0)
                    return false;
                values[i] = (byte)v;
            }

            var name = s[..sep];
            if (Polymod(name, values) != 1)
                return false;

            var bytes = ConvertBits(values.AsSpan(0, values.Length - 6));
            if (bytes == null)
                return false;

            hrp = name;
            data = bytes;
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(values);
        }
    }

    private static uint Polymod(string hrp, ReadOnlySpan<byte> values)
    {
        uint chk = 1;
        void Step(int v)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ (uint)v;
            for (var i = 0; i < 5; i++)
            {
                if (((top >> i) & 1) == 1)
                    chk ^= Generator[i];
            }
        }

        foreach (var c in hrp)
            Step(c >> 5);
        Step(0);
        foreach (var c in hrp)
            Step(c & 31);
        foreach (var v in values)
            Step(v);
        return chk;
    }

    /// <summary>Regroup 5-bit values into bytes. Padding must be fewer than 5 bits and all zero.</summary>
    private static byte[]? ConvertBits(ReadOnlySpan<byte> values)
    {
        var result = new byte[values.Length * 5 / 8];
        var count = 0;
        var acc = 0;
        var bits = 0;
        foreach (var v in values)
        {
            acc = (acc << 5) | v;
            bits += 5;
            while (bits >= 8)
            {
                bits -= 8;
                result[count++] = (byte)((acc >> bits) & 0xff);
            }

            acc &= (1 << bits) - 1;
        }

        if (bits >= 5 || acc != 0)
        {
            CryptographicOperations.ZeroMemory(result);
            return null;
        }

        return result;
    }
}
