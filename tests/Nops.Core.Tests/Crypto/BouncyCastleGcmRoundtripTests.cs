using System;
using System.Text;
using Xunit;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Nops.Core.Tests.Crypto;

public class BouncyCastleGcmRoundtripTests
{
    [Fact]
    public void Gcm_RoundTrip_With32ByteNonce()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        var nonce = new byte[32];
        new Random(123).NextBytes(nonce);

        var plaintext = Encoding.UTF8.GetBytes("hello world");
        var aad = Encoding.UTF8.GetBytes("mykey:");

        // Encrypt
        var gcmEncrypt = new GcmBlockCipher(new AesEngine());
        gcmEncrypt.Init(true, new AeadParameters(
            new KeyParameter(key), 128, nonce, aad));

        var encOutput = new byte[gcmEncrypt.GetOutputSize(plaintext.Length)];
        gcmEncrypt.ProcessBytes(plaintext, 0, plaintext.Length, encOutput, 0);
        gcmEncrypt.DoFinal(encOutput, 0);

        // Extract CT and TAG from encryption output (CT || TAG)
        var ctLen = encOutput.Length - 16;
        var ct = new byte[ctLen];
        Array.Copy(encOutput, 0, ct, 0, ctLen);
        var tag = new byte[16];
        Array.Copy(encOutput, ctLen, tag, 0, 16);

        Console.WriteLine($"CT: {Convert.ToBase64String(ct)}");
        Console.WriteLine($"TAG: {Convert.ToBase64String(tag)}");
        Console.WriteLine($"IV: {Convert.ToBase64String(nonce)}");

        // Decrypt with combined CT||TAG
        var ctWithTag = new byte[ct.Length + tag.Length];
        Array.Copy(ct, 0, ctWithTag, 0, ct.Length);
        Array.Copy(tag, 0, ctWithTag, ct.Length, tag.Length);

        var gcmDecrypt = new GcmBlockCipher(new AesEngine());
        gcmDecrypt.Init(false, new AeadParameters(
            new KeyParameter(key), 128, nonce, aad));

        var decOutput = new byte[gcmDecrypt.GetOutputSize(ctWithTag.Length)];
        gcmDecrypt.ProcessBytes(ctWithTag, 0, ctWithTag.Length, decOutput, 0);
        var plainLen = gcmDecrypt.DoFinal(decOutput, 0);

        var result = Encoding.UTF8.GetString(decOutput, 0, plainLen);
        Console.WriteLine($"Decrypted: {result}");

        Assert.Equal("hello world", result);
    }
}
