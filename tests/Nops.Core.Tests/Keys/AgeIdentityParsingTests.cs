using Nops.Core.Keys;

namespace Nops.Core.Tests.Keys;

public class AgeIdentityParsingTests
{
    // Test-only key from the age test vectors.
    private const string Key = "AGE-SECRET-KEY-1EGTZVFFV20835NWYV6270LXYVK2VKNX2MMDKWYKLMGR48UAWX40Q2P2LM0";

    [Fact]
    public void ParsesValidIdentity()
    {
        Assert.True(AgeDecryptor.TryParseIdentity(Key, out var scalar));
        Assert.Equal(32, scalar.Length);
    }

    [Fact]
    public void ToleratesSurroundingWhitespace() =>
        Assert.True(AgeDecryptor.TryParseIdentity($"  {Key}\n", out _));

    [Theory]
    [InlineData("")]
    [InlineData("AGE-SECRET-KEY-1")]
    [InlineData("age1ysj5akk3pp2sd0pazqw0xd4ysj3tkhh27e0wkwee82scz9hep3nsq0ey4g")]           // a recipient, not an identity
    [InlineData("AGE-SECRET-KEY-PQ-1HZLGZUPT4ETPKDEV8HSGFDCYZ4E522W0A7PU2LHT8EH9W6YLNC3SW78XKG")] // post-quantum identity
    public void RejectsOtherInput(string text) => Assert.False(AgeDecryptor.TryParseIdentity(text, out _));

    [Fact]
    public void RejectsBadChecksum()
    {
        var corrupted = Key[..^1] + (Key[^1] == 'Q' ? 'P' : 'Q');
        Assert.False(AgeDecryptor.TryParseIdentity(corrupted, out _));
    }

    [Fact]
    public void RejectsLowercaseAndMixedCase()
    {
        Assert.False(AgeDecryptor.TryParseIdentity(Key.ToLowerInvariant(), out _));
        Assert.False(AgeDecryptor.TryParseIdentity(Key[..40] + Key[40..].ToLowerInvariant(), out _));
    }
}

public class AgeMasterKeyTests
{
    private const string Key = "AGE-SECRET-KEY-1EGTZVFFV20835NWYV6270LXYVK2VKNX2MMDKWYKLMGR48UAWX40Q2P2LM0";

    private static string Vector(string name)
    {
        var raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "age-cctv", name));
        var split = System.Text.Encoding.Latin1.GetString(raw).IndexOf("\n\n", StringComparison.Ordinal);
        return System.Text.Encoding.Latin1.GetString(raw[(split + 2)..]);
    }

    [Fact]
    public async Task ArmoredFile_DecryptsWithMatchingKey() =>
        Assert.NotNull(await new AgeMasterKey(Key).DecryptShareAsync(Vector("armor_x25519")));

    [Fact]
    public async Task FileForAnotherKey_ReturnsNull() =>
        Assert.Null(await new AgeMasterKey(Key).DecryptShareAsync(Vector("x25519_no_match")));

    [Fact]
    public async Task InvalidPrivateKey_ReturnsNull() =>
        Assert.Null(await new AgeMasterKey("not a key").DecryptShareAsync(Vector("armor_x25519")));

    [Fact]
    public async Task TamperedFile_ThrowsInsteadOfLookingLikeAWrongKey() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => new AgeMasterKey(Key).DecryptShareAsync(Vector("hmac_bad")));
}
