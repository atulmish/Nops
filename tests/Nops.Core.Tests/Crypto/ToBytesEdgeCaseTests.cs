using System.Text;
using Nops.Core.Crypto;

namespace Nops.Core.Tests.Crypto;

/// <summary>The canonical byte form of every value type sops hashes into the MAC.</summary>
public class ToBytesEdgeCaseTests
{
    private static string Text(object? value) => Encoding.UTF8.GetString(ToBytes.Convert(value));

    [Fact]
    public void Null_IsEmpty() => Assert.Empty(ToBytes.Convert(null));

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "+Inf")]
    [InlineData(double.NegativeInfinity, "-Inf")]
    [InlineData(0.0, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(100.0, "100")]
    [InlineData(-2.25, "-2.25")]
    [InlineData(1e21, "1000000000000000000000")]
    [InlineData(1.5e-7, "0.00000015")]
    [InlineData(-1.5e-10, "-0.00000000015")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(123456789.125, "123456789.125")]
    public void Doubles_UseGoFixedNotation(double value, string expected) =>
        Assert.Equal(expected, Text(value));

    [Fact]
    public void NegativeZeroDouble_IsMinusZero() => Assert.Equal("-0", Text(-0.0));

    [Theory]
    [InlineData(float.NaN, "NaN")]
    [InlineData(float.PositiveInfinity, "+Inf")]
    [InlineData(float.NegativeInfinity, "-Inf")]
    [InlineData(0.0f, "0")]
    [InlineData(1.5f, "1.5")]
    [InlineData(3.14f, "3.14")]
    [InlineData(1e10f, "10000000000")]
    public void Floats_UseGoFixedNotation(float value, string expected) =>
        Assert.Equal(expected, Text(value));

    [Fact]
    public void NegativeZeroFloat_IsMinusZero() => Assert.Equal("-0", Text(-0.0f));

    [Fact]
    public void Integers_AreInvariantDecimal()
    {
        Assert.Equal("-42", Text(-42));
        Assert.Equal("9223372036854775807", Text(long.MaxValue));
    }

    [Fact]
    public void Booleans_AreCapitalised()
    {
        Assert.Equal("True", Text(true));
        Assert.Equal("False", Text(false));
    }

    [Fact]
    public void Bytes_AreReturnedAsIs() => Assert.Equal(new byte[] { 1, 2, 3 }, ToBytes.Convert(new byte[] { 1, 2, 3 }));

    [Fact]
    public void Comments_UseTheirText() => Assert.Equal("note", Text(new Comment("note")));

    [Fact]
    public void UnsupportedTypes_Throw()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ToBytes.Convert(Guid.NewGuid()));
        Assert.Contains("Guid", ex.Message);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DateTimes_AreRfc3339Utc(DateTimeKind kind) =>
        Assert.Equal("2024-01-02T03:04:05Z", Text(new DateTime(2024, 1, 2, 3, 4, 5, kind)));

    [Fact]
    public void LocalDateTimes_AreConvertedToUtc()
    {
        var local = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Text(local));
    }

    [Theory]
    [InlineData("2024-01-02T03:04:05Z", "2024-01-02T03:04:05Z")]
    [InlineData("2024-01-02T03:04:05.5Z", "2024-01-02T03:04:05.5Z")]
    [InlineData("2024-01-02T03:04:05.1234567Z", "2024-01-02T03:04:05.1234567Z")]
    [InlineData("2024-01-02T03:04:05+05:30", "2024-01-02T03:04:05+05:30")]
    [InlineData("2024-01-02T03:04:05.25-08:00", "2024-01-02T03:04:05.25-08:00")]
    public void DateTimeOffsets_AreRfc3339(string input, string expected) =>
        Assert.Equal(expected, Text(DateTimeOffset.Parse(input)));
}
