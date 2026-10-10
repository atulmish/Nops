using System;
using System.Text;
using Xunit;

namespace Nops.Core.Tests.Crypto;

public class ToBytesTests
{
    [Fact]
    public void ToString_ReturnsUtf8Bytes()
    {
        var input = "hello";
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("hello"), result);
    }

    [Fact]
    public void ToString_EmptyString_ReturnsEmptyBytes()
    {
        var input = "";
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ToInt32_ReturnsUtf8BytesOfNumber()
    {
        var input = 42;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("42"), result);
    }

    [Fact]
    public void ToInt32_Negative_ReturnsUtf8Bytes()
    {
        var input = -17;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("-17"), result);
    }

    [Fact]
    public void ToInt64_ReturnsUtf8BytesOfNumber()
    {
        var input = 9223372036854775807L;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("9223372036854775807"), result);
    }

    [Fact]
    public void ToDouble_NoTrailingZeros()
    {
        var input = 3.14;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("3.14"), result);
    }

    [Fact]
    public void ToDouble_ZeroPointOne()
    {
        // Edge case from PLAN.md
        var input = 0.1;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("0.1"), result);
    }

    [Fact]
    public void ToDouble_LargeExponent()
    {
        // Edge case from PLAN.md: 1e20 should not have trailing zeros
        var input = 1e20;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("1E+20"), result);
    }

    [Fact]
    public void ToDouble_Zero()
    {
        // Edge case from PLAN.md
        var input = 0.0;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("0"), result);
    }

    [Fact]
    public void ToDouble_IntegerValue()
    {
        var input = 42.0;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("42"), result);
    }

    [Fact]
    public void ToBoolean_True_ReturnsCapitalized()
    {
        var input = true;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("True"), result);
    }

    [Fact]
    public void ToBoolean_False_ReturnsCapitalized()
    {
        var input = false;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("False"), result);
    }

    [Fact]
    public void ToByteArray_ReturnsRawBytes()
    {
        var input = new byte[] { 0x01, 0x02, 0x03 };
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, result);
    }

    [Fact]
    public void ToByteArray_Empty_ReturnsEmpty()
    {
        var input = Array.Empty<byte>();
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ToDateTime_Rfc3339()
    {
        var input = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("2024-01-01T00:00:00Z"), result);
    }

    [Fact]
    public void ToDateTime_WithTimezone()
    {
        var input = new DateTime(2024, 6, 15, 12, 30, 45, DateTimeKind.Utc);
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("2024-06-15T12:30:45Z"), result);
    }

    [Fact]
    public void ToObject_UnknownType_Throws()
    {
        var input = new { Name = "test" }; // anonymous type
        Assert.Throws<InvalidOperationException>(() => Nops.Core.Crypto.ToBytes.Convert(input));
    }

    [Fact]
    public void ToObject_Null_ReturnsEmpty()
    {
        object? input = null;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ToFloat_Float64Precision()
    {
        // Test that we use FormatFloat('f', -1, 64) semantics (no trailing zeros)
        var input = 0.3;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("0.3"), result);
    }

    [Fact]
    public void ToInt_Single_ReturnsNumberString()
    {
        var input = 100;
        var result = Nops.Core.Crypto.ToBytes.Convert(input);
        Assert.Equal(Encoding.UTF8.GetBytes("100"), result);
    }
}
