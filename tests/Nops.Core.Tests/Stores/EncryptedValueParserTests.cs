using System;
using System.Text;
using Xunit;

namespace Nops.Core.Tests.Stores;

public class EncryptedValueParserTests
{
    [Fact]
    public void TryParse_ValidStr_ReturnsEncryptedValue()
    {
        var input = "ENC[AES256_GCM,data:zlGNmhTYX5xol4ZZFsiaoGkD73nn,iv:ql9mkhoU1I64E/FJi3iA0HaAe2U3kQVFee2ZLwPnBik=,tag:SqVSfu/JkRrwqidAT/i0pg==,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.NotNull(parsed);
        Assert.Equal("str", parsed!.DataType);
    }

    [Fact]
    public void TryParse_ValidInt_ReturnsIntType()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:int]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("int", parsed!.DataType);
    }

    [Fact]
    public void TryParse_ValidFloat_ReturnsFloatType()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:float]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("float", parsed!.DataType);
    }

    [Fact]
    public void TryParse_ValidBool_ReturnsBoolType()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:bool]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("bool", parsed!.DataType);
    }

    [Fact]
    public void TryParse_ValidBytes_ReturnsBytesType()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:bytes]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("bytes", parsed!.DataType);
    }

    [Fact]
    public void TryParse_ValidTime_ReturnsTimeType()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:time]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("time", parsed!.DataType);
    }

    [Fact]
    public void TryParse_ValidComment_ReturnsCommentType()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:comment]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("comment", parsed!.DataType);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        var input = "";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_MissingDataField_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,iv:test,tag:test,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_MissingIvField_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,data:test,tag:test,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_MissingTagField_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_MissingTypeField_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_InvalidBase64Data_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,data:!!!invalid,iv:test,tag:test,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_InvalidBase64Iv_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,data:test,iv:!!!invalid,tag:test,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_InvalidBase64Tag_ReturnsFalse()
    {
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:!!!invalid,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_TrailingContentAfterBracket_ParsesSuccessfully()
    {
        // No $ anchor - trailing content after ] should be ignored per upstream behavior
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:str]extra";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void TryParse_UnknownType_PreservesType()
    {
        // Unknown types should be preserved for later error (not swallowed)
        var input = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:unknown_type]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal("unknown_type", parsed!.DataType);
    }

    [Fact]
    public void TryParse_Base64StdEncoding_DecodesCorrectly()
    {
        // Use standard base64 (not URL-safe)
        var input = "ENC[AES256_GCM,data:dGVzdA==,iv:dGVzdA==,tag:dGVzdA==,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.Equal(Encoding.UTF8.GetBytes("test"), parsed!.Data);
        Assert.Equal(Encoding.UTF8.GetBytes("test"), parsed.Iv);
        Assert.Equal(Encoding.UTF8.GetBytes("test"), parsed.Tag);
    }

    [Fact]
    public void TryParse_RealVector_FromExampleYaml()
    {
        // Real vector from sops/example.yaml:1
        var input = "ENC[AES256_GCM,data:zlGNmhTYX5xol4ZZFsiaoGkD73nn,iv:ql9mkhoU1I64E/FJi3iA0HaAe2U3kQVFee2ZLwPnBik=,tag:SqVSfu/JkRrwqidAT/i0pg==,type:str]";

        var result = Nops.Core.Stores.EncryptedValueParser.TryParse(input, out var parsed);

        Assert.True(result);
        Assert.NotNull(parsed);
        Assert.Equal("str", parsed.DataType);
        // Verify base64 decoding works with standard encoding
        Assert.NotEmpty(parsed.Data);
        Assert.NotEmpty(parsed.Iv);
        Assert.NotEmpty(parsed.Tag);
    }

    [Fact]
    public void EncryptedValue_RecordHasCorrectProperties()
    {
        var data = Encoding.UTF8.GetBytes("ciphertext");
        var iv = new byte[32]; // 32-byte nonce
        var tag = new byte[16]; // 16-byte GCM tag
        var type = "str";

        var value = new Nops.Core.Stores.EncryptedValue(data, iv, tag, type);

        Assert.Equal(data, value.Data);
        Assert.Equal(iv, value.Iv);
        Assert.Equal(tag, value.Tag);
        Assert.Equal(type, value.DataType);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== ENCRYPTED VALUE PARSER ===");
        Console.WriteLine("Regex: ^ENC\\[AES256_GCM,data:(.+),iv:(.+),tag:(.+),type:(.+)\\]");
        Console.WriteLine("No trailing $ anchor (matches upstream)");
        Console.WriteLine("Uses Convert.FromBase64String (StdEncoding, not UrlSafe)");
        Console.WriteLine("Empty string → false (not encrypted)");
        Console.WriteLine("===============================");

        Assert.True(true, "Spike complete - see console output");
    }
}
