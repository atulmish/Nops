using System.Text;
using Nops.Core.Stores;

namespace Nops.Core.Tests.Stores;

/// <summary>Edge cases of the YAML reader: malformed documents, aliases, tags and scalar typing.</summary>
public class YamlStoreEdgeCaseTests
{
    private static TreeBranch Load(string yaml) =>
        YamlStore.LoadEncrypted(Encoding.UTF8.GetBytes(yaml), "t.yaml").Branches[0];

    private static object? Value(TreeBranch branch, string key) =>
        branch.First(i => i.Key is string s && s == key).Value;

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    [InlineData("---\n")]
    [InlineData("---\n# comment\n")]
    public void EmptyDocuments_AreRejected(string yaml) =>
        Assert.Throws<InvalidOperationException>(() => Load(yaml));

    [Theory]
    [InlineData("- a\n- b\n")]
    [InlineData("just a scalar\n")]
    [InlineData("---\n- a\n")]
    public void RootThatIsNotAMapping_IsRejected(string yaml)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Load(yaml));
        Assert.Contains("not a mapping", ex.Message);
    }

    [Fact]
    public void CommentsAroundTheDocumentMarker_AreKept()
    {
        var branch = Load("# before\n---\n# after marker\na: 1\n");

        Assert.Contains(branch, i => i.Key is Comment c && c.Value.Contains("before"));
        Assert.Contains(branch, i => i.Key is Comment c && c.Value.Contains("after marker"));
        Assert.Equal(1L, Value(branch, "a"));
    }

    [Fact]
    public void AliasAsMappingKey_ResolvesToTheAnchoredScalar()
    {
        var branch = Load("name: &k hello\n*k : world\n");

        Assert.Equal("world", Value(branch, "hello"));
    }

    [Fact]
    public void AliasAsMappingKey_ToANumber_UsesItsInvariantText()
    {
        var branch = Load("n: &n 5\n*n : v\n");

        Assert.Equal("v", Value(branch, "5"));
    }

    [Fact]
    public void AliasAsMappingKey_ToAMapping_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Load("m: &m {a: 1}\n*m : 2\n"));
        Assert.Contains("does not refer to a scalar", ex.Message);
    }

    [Fact]
    public void NonScalarMappingKey_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Load("? [a, b]\n: value\n"));
        Assert.Contains("mapping key is not a scalar", ex.Message);
    }

    [Fact]
    public void UnknownAlias_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Load("a: *nope\n"));
        Assert.Contains("unknown anchor", ex.Message);
    }

    [Fact]
    public void AliasedMapping_IsDeepCopied()
    {
        var branch = Load("base: &b {x: 1}\ncopy: *b\n");

        var copy = Assert.IsType<TreeBranch>(Value(branch, "copy"));
        Assert.Equal(1L, copy.First().Value);
        Assert.NotSame(Value(branch, "base"), copy);
    }

    [Theory]
    [InlineData("a: !!str 123\n", "123")]
    [InlineData("a: !!str true\n", "true")]
    public void ExplicitStrTag_KeepsAString(string yaml, string expected) =>
        Assert.Equal(expected, Value(Load(yaml), "a"));

    [Fact]
    public void LeadingDotValues_AreFloatsOrStrings()
    {
        var branch = Load("half: .5\nnot_a_number: .foo\n");

        Assert.Equal(0.5d, Value(branch, "half"));
        Assert.Equal(".foo", Value(branch, "not_a_number"));
    }

    [Theory]
    [InlineData("0o17", 15L)]
    [InlineData("0b101", 5L)]
    [InlineData("0xFF", 255L)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("+12", 12L)]
    public void IntegerForms_AreParsed(string text, long expected) =>
        Assert.Equal(expected, Value(Load($"a: {text}\n"), "a"));

    [Theory]
    [InlineData("+")]
    [InlineData("0x")]
    [InlineData("0o")]
    [InlineData("0b")]
    [InlineData("0b102")]
    public void MalformedIntegers_StayStrings(string text) =>
        Assert.Equal(text, Value(Load($"a: {text}\n"), "a"));

    [Theory]
    [InlineData("123456789012345678901234567890", 1.2345678901234568E+29)]
    [InlineData("9223372036854775808", 9.2233720368547758E+18)]
    [InlineData("-9223372036854775809", -9.2233720368547758E+18)]
    public void IntegersBeyondInt64_BecomeFloatsLikeYamlV3(string text, double expected) =>
        Assert.Equal(expected, Value(Load($"a: {text}\n"), "a"));

    [Fact]
    public void Timestamps_CoverSeparatorsFractionsAndZones()
    {
        var branch = Load(
            "space: 2024-01-01 10:20:30\n" +
            "space_zone: 2024-01-01 10:20:30 +01:00\n" +
            "fraction: 2024-01-01T10:20:30.123456789Z\n" +
            "offset: 2024-01-01T10:20:30+05:30\n" +
            "negative: 2024-01-01T10:20:30-08:00\n" +
            "lower_z: 2024-01-01t10:20:30z\n");

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 20, 30, TimeSpan.Zero), Value(branch, "space"));
        // yaml.v3 has no "space then zone" timestamp form, so this stays a string.
        Assert.Equal("2024-01-01 10:20:30 +01:00", Value(branch, "space_zone"));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 20, 30, TimeSpan.Zero).AddTicks(1234567), Value(branch, "fraction"));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 20, 30, new TimeSpan(5, 30, 0)), Value(branch, "offset"));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 20, 30, TimeSpan.FromHours(-8)), Value(branch, "negative"));
        Assert.IsType<DateTimeOffset>(Value(branch, "lower_z"));
    }

    [Theory]
    [InlineData("2024-13-45")]
    [InlineData("2024-02-30")]
    [InlineData("2024-01-01T25:61:61Z")]
    [InlineData("2024-01-01 99:00:00")]
    public void ImpossibleDatesAndTimes_StayStrings(string text) =>
        Assert.Equal(text, Value(Load($"a: {text}\n"), "a"));

    [Fact]
    public void MultiDocumentStream_UsesTheFirstDocument()
    {
        var branch = Load("a: 1\n---\nb: 2\n");

        Assert.Equal(1L, Value(branch, "a"));
    }

    [Fact]
    public void MoreThanOneSopsKey_IsRejected() =>
        Assert.Throws<SopsMetadataException>(() => Load("a: 1\nsops: {}\nsops: {}\n"));
}
