using System;
using System.Text;
using Xunit;

namespace Nops.Core.Tests.Crypto;

public class AesGcmDecryptorTests
{
    private static readonly byte[] TestDataKey = new byte[32];

    static AesGcmDecryptorTests()
    {
        new Random(42).NextBytes(TestDataKey);
    }

    [Fact]
    public void DecryptValue_StrType_ReturnsStringValue()
    {
        var plaintext = "hello world";
        var (ct, iv, tag) = EncryptForTest(Encoding.UTF8.GetBytes(plaintext));
        var encString = MakeEnc(ct, iv, tag, "str");

        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey);

        Assert.NotNull(result);
        Assert.IsType<string>(result);
        Assert.Equal(plaintext, (string)result!);
    }

    [Fact]
    public void DecryptValue_BoolTrueType_ReturnsBooleanTrue()
    {
        var (ct, iv, tag) = EncryptForTest(Encoding.UTF8.GetBytes("True"));
        var encString = MakeEnc(ct, iv, tag, "bool");

        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey);

        Assert.NotNull(result);
        Assert.IsType<bool>(result);
        Assert.True((bool)result!);
    }

    [Fact]
    public void DecryptValue_BoolFalseType_ReturnsBooleanFalse()
    {
        var (ct, iv, tag) = EncryptForTest(Encoding.UTF8.GetBytes("False"));
        var encString = MakeEnc(ct, iv, tag, "bool");

        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey);

        Assert.NotNull(result);
        Assert.IsType<bool>(result);
        Assert.False((bool)result!);
    }

    [Fact]
    public void DecryptValue_IntType_ReturnsInt32()
    {
        var (ct, iv, tag) = EncryptForTest(Encoding.UTF8.GetBytes("42"));
        var encString = MakeEnc(ct, iv, tag, "int");

        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey);

        Assert.NotNull(result);
        Assert.IsType<int>(result);
        Assert.Equal(42, (int)result!);
    }

    [Fact]
    public void DecryptValue_FloatType_ReturnsDouble()
    {
        var (ct, iv, tag) = EncryptForTest(Encoding.UTF8.GetBytes("3.14"));
        var encString = MakeEnc(ct, iv, tag, "float");

        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey);

        Assert.NotNull(result);
        Assert.IsType<double>(result);
        Assert.Equal(3.14, (double)result!, 10);
    }

    [Fact]
    public void DecryptValue_BytesType_ReturnsByteArray()
    {
        var plaintext = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var (ct, iv, tag) = EncryptForTest(plaintext);
        var encString = MakeEnc(ct, iv, tag, "bytes");

        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey);

        Assert.NotNull(result);
        Assert.IsType<byte[]>(result);
        Assert.Equal(plaintext, (byte[])result!);
    }

    [Fact]
    public void DecryptValue_WrongAad_Throws()
    {
        var plaintextBytes = Encoding.UTF8.GetBytes("hello");
        var (ct, iv, tag) = EncryptForTest(plaintextBytes);
        var encString = MakeEnc(ct, iv, tag, "str");

        Assert.ThrowsAny<Exception>(() =>
            Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, TestDataKey, "wrong:aad:"));
    }

    [Fact]
    public void DecryptValue_WrongKey_Throws()
    {
        var plaintextBytes = Encoding.UTF8.GetBytes("hello");
        var (ct, iv, tag) = EncryptForTest(plaintextBytes);
        var encString = MakeEnc(ct, iv, tag, "str");

        var wrongKey = new byte[32];
        new Random(99).NextBytes(wrongKey);

        Assert.ThrowsAny<Exception>(() =>
            Nops.Core.Crypto.AesGcmDecryptor.DecryptValue(encString, wrongKey));
    }

    [Fact]
    public void DecryptValue_InvalidEncryptedString_ReturnsNull()
    {
        var result = Nops.Core.Crypto.AesGcmDecryptor.DecryptValue("not an encrypted value", TestDataKey);
        Assert.Null(result);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== AES-GCM DECRYPTOR ===");
        Console.WriteLine("Uses BouncyCastle GcmBlockCipher (32-byte nonce support)");
        Console.WriteLine("Decrypts ENC[...] strings and casts by type");
        Console.WriteLine("=========================");
        Assert.True(true, "Spike complete");
    }

    private static (byte[] ct, byte[] iv, byte[] tag) EncryptForTest(byte[] plaintext)
    {
        var iv = new byte[32];
        new Random(123).NextBytes(iv);

        var gcmOutputLen = plaintext.Length + 16;
        var gcmOutput = new byte[gcmOutputLen];

        var aesEngine = new Org.BouncyCastle.Crypto.Engines.AesEngine();
        var gcmCipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(aesEngine);

        var parameters = new Org.BouncyCastle.Crypto.Parameters.AeadParameters(
            new Org.BouncyCastle.Crypto.Parameters.KeyParameter(TestDataKey),
            128, iv, Array.Empty<byte>());

        gcmCipher.Init(true, parameters);
        gcmCipher.ProcessBytes(plaintext, 0, plaintext.Length, gcmOutput, 0);
        gcmCipher.DoFinal(gcmOutput, 0);

        var ctLen = gcmOutput.Length - 16;
        var ct = new byte[ctLen];
        Array.Copy(gcmOutput, 0, ct, 0, ctLen);
        var t = new byte[16];
        Array.Copy(gcmOutput, ctLen, t, 0, 16);

        return (ct, iv, t);
    }

    private static string MakeEnc(byte[] ct, byte[] iv, byte[] tag, string type)
    {
        return $"ENC[AES256_GCM,data:{Convert.ToBase64String(ct)},iv:{Convert.ToBase64String(iv)},tag:{Convert.ToBase64String(tag)},type:{type}]";
    }
}
