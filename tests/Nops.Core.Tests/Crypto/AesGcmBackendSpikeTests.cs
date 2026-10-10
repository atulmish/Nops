using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Nops.Core.Tests.Crypto;

public class AesGcmBackendSpikeTests
{
    [Fact]
    public void AesGcm_Rejects32ByteNonce()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var nonce32 = new byte[32];
        Random.Shared.NextBytes(nonce32);
        var plaintext = Encoding.UTF8.GetBytes("SOPS uses 32-byte nonces");
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        // .NET AesGcm rejects 32-byte nonces - this is the expected behavior
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Encrypt(nonce32, plaintext, ciphertext, tag, Array.Empty<byte>());
        });

        Console.WriteLine($"RESULT: .NET AesGcm REJECTS 32-byte nonce as expected");
        Console.WriteLine($"Error: {ex.Message}");
    }

    [Fact]
    public void AesGcm_Accepts12ByteNonce()
    {
        // Confirm .NET AesGcm works with standard 12-byte nonce
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var nonce12 = new byte[12];
        Random.Shared.NextBytes(nonce12);
        var plaintext = Encoding.UTF8.GetBytes("test value");
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Encrypt(nonce12, plaintext, ciphertext, tag, Array.Empty<byte>());

        var decrypted = new byte[plaintext.Length];
        aesGcm.Decrypt(nonce12, ciphertext, tag, decrypted, Array.Empty<byte>());

        Console.WriteLine("RESULT: .NET AesGcm works with 12-byte nonce (standard)");
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        // SPIKE CONCLUSION - Decision 2:
        // .NET AesGcm does NOT accept 32-byte nonces (SOPS standard)
        // Must use BouncyCastle.GcmBlockCipher for value decryption

        Console.WriteLine("=== AES-GCM BACKEND DECISION ===");
        Console.WriteLine(".NET AesGcm accepts 32-byte nonces: NO");
        Console.WriteLine("Decision: Use BouncyCastle.GcmBlockCipher as primary backend");
        Console.WriteLine("Reason: SOPS uses 32-byte nonces, .NET only supports 12-byte");
        Console.WriteLine("================================");

        Assert.True(true, "Spike complete - see console output");
    }
}
