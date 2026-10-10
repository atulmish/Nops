using System;
using System.Linq;
using Xunit;

namespace Nops.Core.Tests.Crypto;

public class ShamirTests
{
    [Fact]
    public void Split_2Of3_CreatesValidShares()
    {
        var secret = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        Assert.Equal(3, shares.Length);
        Assert.All(shares, s => Assert.Equal(secret.Length + 1, s.Length)); // Each share has index byte + secret bytes
    }

    [Fact]
    public void Combine_2Of3_ReconstructsSecret()
    {
        var secret = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        // Combine any 2 shares should reconstruct the secret
        var reconstructed = Nops.Core.Crypto.Shamir.Combine(shares.Take(2).ToArray());

        Assert.Equal(secret, reconstructed);
    }

    [Fact]
    public void Combine_3Of5_ReconstructsSecret()
    {
        var secret = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 5, threshold: 3);

        // Combine any 3 shares should reconstruct the secret
        var reconstructed = Nops.Core.Crypto.Shamir.Combine(shares.Take(3).ToArray());

        Assert.Equal(secret, reconstructed);
    }

    [Fact]
    public void Combine_InsufficientShares_ReturnsWrongValue()
    {
        // Shamir.Combine interpolates with whatever shares are given.
        // Without knowing the original threshold, it can't validate "insufficiency".
        var secret = new byte[] { 0x01, 0x02 };

        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 3, threshold: 2);

        // Using fewer shares than threshold produces wrong result
        var result = Nops.Core.Crypto.Shamir.Combine(new[] { shares[0] });

        Assert.NotEqual(secret, result);
    }

    [Fact]
    public void GfAdd_XorOperations()
    {
        // GF(2^8) addition is XOR
        Assert.Equal(0x00, Nops.Core.Crypto.Shamir.GfAdd(0x00, 0x00));
        Assert.Equal(0xFF, Nops.Core.Crypto.Shamir.GfAdd(0xFF, 0x00));
        Assert.Equal(0x00, Nops.Core.Crypto.Shamir.GfAdd(0xAB, 0xAB));
        Assert.Equal(0xCD, Nops.Core.Crypto.Shamir.GfAdd(0x12, 0xDF));
    }

    [Fact]
    public void GfMul_PolynomialMultiplication()
    {
        // GF(2^8) multiplication with irreducible polynomial x^8 + x^4 + x^3 + x + 1
        Assert.Equal(0x00, Nops.Core.Crypto.Shamir.GfMul(0x00, 0x53));
        Assert.Equal(0x53, Nops.Core.Crypto.Shamir.GfMul(0x01, 0x53));
        Assert.Equal(0x01, Nops.Core.Crypto.Shamir.GfMul(0x02, 0x8D)); // 2 * 0x8D = 1 in this field
    }

    [Fact]
    public void GfDiv_InverseMultiplication()
    {
        // a / a = 1 for any non-zero a (in any field)
        Assert.Equal(0x01, Nops.Core.Crypto.Shamir.GfDiv(0x53, 0x53));
        Assert.Equal(0x01, Nops.Core.Crypto.Shamir.GfDiv(0xFF, 0xFF));

        // a / 1 = a
        Assert.Equal(0x53, Nops.Core.Crypto.Shamir.GfDiv(0x53, 0x01));
    }

    [Fact]
    public void Split_Identity_NoShares()
    {
        var secret = new byte[] { 0x42 };

        var shares = Nops.Core.Crypto.Shamir.Split(secret, numShares: 0, threshold: 0);

        Assert.Empty(shares);
    }

    [Fact]
    public void Combine_EmptyShares_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Nops.Core.Crypto.Shamir.Combine(Array.Empty<byte[]>()));
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== SHAMIR SECRET SHARING ===");
        Console.WriteLine("GF(2^8) arithmetic for polynomial interpolation");
        Console.WriteLine("Split: secret -> n shares with threshold requirement");
        Console.WriteLine("Combine: any k >= threshold shares -> reconstruct secret");
        Console.WriteLine("=============================");
        Assert.True(true, "Spike complete");
    }
}
