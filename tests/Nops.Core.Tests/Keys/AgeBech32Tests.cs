using Nops.Core.Keys;

namespace Nops.Core.Tests.Keys;

public class AgeBech32Tests
{
    private const string Valid = "age1ysj5akk3pp2sd0pazqw0xd4ysj3tkhh27e0wkwee82scz9hep3nsq0ey4g";

    [Fact]
    public void ValidString_Decodes()
    {
        Assert.True(AgeBech32.TryDecode(Valid, out var hrp, out var data));
        Assert.Equal("age", hrp);
        Assert.Equal(32, data.Length);
    }

    [Fact]
    public void UpperCase_DecodesToTheSameData()
    {
        Assert.True(AgeBech32.TryDecode(Valid, out _, out var lower));
        Assert.True(AgeBech32.TryDecode(Valid.ToUpperInvariant(), out _, out var upper));
        Assert.Equal(lower, upper);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]                                  // no human-readable part
    [InlineData("age1")]                               // no data or checksum
    [InlineData("age1qqqqq")]                          // too short for a checksum
    [InlineData("nosep")]
    [InlineData("age1ysj5akk3pp2sd0pazqw0xd4ysj3tkhh27e0wkwee82scz9hep3nsq0ey4b")] // bad checksum
    [InlineData("age1ysj5akk3pp2sd0pazqw0xd4ysj3tkhh27e0wkwee82scz9hep3nsq0eyib")] // 'i' is not in the alphabet
    [InlineData("age1ysj5akk3pp2sd0pazqw0xd4ysj3tkhh27e0wkwee82scz9hep3nsq0ey4\u0001")] // control character
    public void Malformed_IsRejected(string text) => Assert.False(AgeBech32.TryDecode(text, out _, out _));

    [Fact]
    public void MixedCase_IsRejected() =>
        Assert.False(AgeBech32.TryDecode("Age" + Valid[3..], out _, out _));
}
