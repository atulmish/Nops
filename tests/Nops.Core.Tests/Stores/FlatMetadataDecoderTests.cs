using Nops.Core.Stores;

namespace Nops.Core.Tests.Stores;

public class FlatMetadataDecoderTests
{
    [Fact]
    public void DecodesNestedMapsAndLists()
    {
        var decoded = FlatMetadataDecoder.Decode(new()
        {
            ["age__list_0__map_recipient"] = "age1abc",
            ["age__list_0__map_enc"] = "ciphertext",
            ["mac"] = "MAC",
        });

        Assert.Equal("MAC", decoded["mac"]);
        var list = Assert.IsType<List<object>>(decoded["age"]);
        var entry = Assert.IsType<Dictionary<string, object>>(Assert.Single(list));
        Assert.Equal("age1abc", entry["recipient"]);
        Assert.Equal("ciphertext", entry["enc"]);
    }

    [Theory]
    [InlineData("a__list_0", "a__list_0__b")]           // a list slot already holds a string
    [InlineData("a__list_0__list_0", "a__list_0__b")]   // a list element addressed as a map
    public void ConflictingPathsFailWithADocumentedError(string first, string second)
    {
        var flat = new Dictionary<string, string> { [first] = "x", [second] = "y" };
        Assert.Throws<InvalidOperationException>(() => FlatMetadataDecoder.Decode(flat));
    }

    [Theory]
    [InlineData("a__list_x")]
    [InlineData("a__list_-1")]
    [InlineData("a__list_99999999999999999999")]
    [InlineData("a__list_2000000000")]
    public void InvalidOrHugeListIndexesAreRejectedWithoutAllocating(string key)
    {
        var flat = new Dictionary<string, string> { [key] = "x" };
        Assert.Throws<InvalidOperationException>(() => FlatMetadataDecoder.Decode(flat));
    }

    [Fact]
    public void AMapSegmentAtTheRootOfAListIsRejected()
    {
        var flat = new Dictionary<string, string> { ["list_0__b"] = "x" };
        Assert.Throws<InvalidOperationException>(() => FlatMetadataDecoder.Decode(flat));
    }
}
