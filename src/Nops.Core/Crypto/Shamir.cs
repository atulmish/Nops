using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Nops.Core.Crypto;

/// <summary>
/// Shamir's Secret Sharing over GF(2^8).
/// Port of sops/shamir/shamir.go with exact GF(2^8) arithmetic.
/// </summary>
public static class Shamir
{
    // Irreducible polynomial: x^8 + x^4 + x^3 + x + 1 = 0x11B
    private const int IrreduciblePoly = 0x11B;

    // GF(2^8) addition is XOR
    public static byte GfAdd(byte a, byte b) => (byte)(a ^ b);

    // GF(2^8) multiplication using Russian peasant algorithm
    public static byte GfMul(byte a, byte b)
    {
        var p = 0;
        for (int i = 0; i < 8; i++)
        {
            if ((b & 1) != 0)
                p ^= a;
            var hiBit = (a & 0x80);
            a = (byte)((a << 1) & 0xFF);
            if (hiBit != 0)
                a ^= (IrreduciblePoly & 0xFF);
            b >>= 1;
        }
        return (byte)p;
    }

    // GF(2^8) division = multiply by inverse
    public static byte GfDiv(byte a, byte b)
    {
        if (b == 0) throw new DivideByZeroException();
        if (a == 0) return 0;

        return GfMul(a, GfInverse(b));
    }

    /// <summary>
    /// Invert a non-zero element in GF(2^8): x^-1 = x^254 (multiplicative group order 255).
    /// </summary>
    private static byte GfInverse(byte b)
    {
        if (b == 0) throw new DivideByZeroException();
        return GfPow(b, 254);
    }

    private static byte GfPow(byte baseVal, int exp)
    {
        var result = (byte)1;
        var b = baseVal;

        while (exp > 0)
        {
            if ((exp & 1) == 1)
                result = GfMul(result, b);
            b = GfMul(b, b);
            exp >>= 1;
        }

        return result;
    }

    /// <summary>
    /// Split a secret into n shares with threshold k.
    /// Any k shares can reconstruct the secret; fewer cannot.
    /// Uses a cryptographically secure random number generator for the polynomial
    /// coefficients.
    /// </summary>
    public static byte[][] Split(byte[] secret, int numShares, int threshold)
    {
        if (numShares == 0 || threshold == 0)
            return Array.Empty<byte[]>();

        if (threshold > numShares)
            throw new ArgumentException("Threshold cannot exceed number of shares");

        var shares = new byte[numShares][];

        // Split each byte of the secret independently
        for (int i = 0; i < secret.Length; i++)
        {
            var shareBytes = SplitByte(secret[i], numShares, threshold);
            for (int j = 0; j < numShares; j++)
            {
                if (shares[j] == null)
                    shares[j] = new byte[secret.Length + 1];
                shares[j][i] = shareBytes[j];
            }
        }

        // Store the x-coordinate (index) as the last byte of each share.
        // The representation of each share is {y1, y2, .., yN, x}.
        for (int i = 0; i < numShares; i++)
        {
            shares[i][secret.Length] = (byte)(i + 1);
        }

        return shares;
    }

    /// <summary>
    /// Combine shares to reconstruct the secret.
    /// Requires at least two shares; all shares must have the same length.
    /// </summary>
    public static byte[] Combine(byte[][] shares)
    {
        if (shares == null || shares.Length < 2)
            throw new ArgumentException("Less than two shares cannot be used to reconstruct the secret");

        var firstLen = shares[0].Length;
        if (firstLen < 2)
            throw new ArgumentException("Shares must be at least two bytes");

        foreach (var share in shares)
        {
            if (share.Length != firstLen)
                throw new ArgumentException("All shares must be the same length");
        }

        var secretLength = firstLen - 1;
        var result = new byte[secretLength];

        // Combine each byte position using Lagrange interpolation
        for (int i = 0; i < secretLength; i++)
        {
            result[i] = CombineByte(shares, i);
        }

        return result;
    }

    private static byte[] SplitByte(byte secret, int numShares, int threshold)
    {
        // Generate random coefficients for polynomial (except constant term which is the secret)
        var coeffs = new byte[threshold];
        coeffs[0] = secret;

        // Cryptographically secure randomness for polynomial coefficients.
        var randBytes = new byte[threshold - 1];
        RandomNumberGenerator.Fill(randBytes);
        for (int i = 1; i < threshold; i++)
            coeffs[i] = randBytes[i - 1];

        // Evaluate polynomial at points x=1, x=2, ..., x=numShares
        var result = new byte[numShares];
        for (int x = 1; x <= numShares; x++)
        {
            result[x - 1] = EvalPoly(coeffs, (byte)x);
        }

        return result;
    }

    private static byte EvalPoly(byte[] coeffs, byte x)
    {
        // Horner's method: a0 + x*(a1 + x*(a2 + ...))
        var result = coeffs[coeffs.Length - 1];
        for (int i = coeffs.Length - 2; i >= 0; i--)
        {
            result = GfAdd(coeffs[i], GfMul(x, result));
        }
        return result;
    }

    private static byte CombineByte(byte[][] shares, int valueIndex)
    {
        var threshold = shares.Length; // Need all provided shares

        // Get x-coordinates from shares
        var xs = new byte[threshold];
        var ys = new byte[threshold];

        for (int i = 0; i < threshold; i++)
        {
            xs[i] = shares[i][shares[i].Length - 1]; // Last byte is the index
            ys[i] = shares[i][valueIndex]; // y-values precede the index byte
        }

        // Lagrange interpolation at x=0
        var result = (byte)0;
        for (int i = 0; i < threshold; i++)
        {
            var li = LagrangeBase(xs, i);
            result = GfAdd(result, GfMul(ys[i], li));
        }

        return result;
    }

    private static byte LagrangeBase(byte[] xs, int skip)
    {
        var num = (byte)1;
        var den = (byte)1;

        for (int j = 0; j < xs.Length; j++)
        {
            if (j == skip) continue;

            // numerator *= (0 - xj) = xj (in GF(2^8), subtraction = addition = XOR, and 0 XOR x = x)
            num = GfMul(num, xs[j]);

            // denominator *= (xi - xj) = xi XOR xj
            var diff = (byte)(xs[skip] ^ xs[j]);
            if (diff == 0) throw new ArgumentException("Duplicate share indices");
            den = GfMul(den, diff);
        }

        return GfDiv(num, den);
    }
}
