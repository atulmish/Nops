using System;
using Nops.Core.Keys;

namespace Nops.Core.GcpKms.Tests;

/// <summary>
/// Tests for GcpKmsEntryParser — parsing opaque .sops GCP KMS dict entries.
/// </summary>
public class GcpKmsEntryParserTests
{
    [Fact]
    public void Parse_ValidEntry_ReturnsCorrectResourceNameAndEnc()
    {
        var dict = new Dictionary<string, object>
        {
            ["resource_name"] = "projects/nops-secrets/locations/global/keyRings/test-keyring/cryptoKeys/nops-key",
            ["enc"] = "encrypted-data-key-share-base64"
        };

        var entry = GcpKmsEntryParser.Parse(dict);

        Assert.Equal("projects/nops-secrets/locations/global/keyRings/test-keyring/cryptoKeys/nops-key", entry.ResourceName);
        Assert.Equal("encrypted-data-key-share-base64", entry.Enc);
        Assert.Empty(entry.Context);
        Assert.Null(entry.CreatedAt);
    }

    [Fact]
    public void Parse_WithCreatedAt_ReturnsDateTimeOffset()
    {
        var dict = new Dictionary<string, object>
        {
            ["resource_name"] = "projects/test/locations/global/keyRings/kr/cryptoKeys/ck",
            ["enc"] = "share-data",
            ["created_at"] = DateTimeOffset.Parse("2025-03-15T08:00:00Z")
        };

        var entry = GcpKmsEntryParser.Parse(dict);

        Assert.NotNull(entry.CreatedAt);
    }

    [Fact]
    public void Parse_WithCreatedAtString_ReturnsDateTimeOffset()
    {
        var dict = new Dictionary<string, object>
        {
            ["resource_name"] = "projects/test/locations/global/keyRings/kr/cryptoKeys/ck",
            ["enc"] = "share-data",
            ["created_at"] = "2025-06-01T00:00:00Z"
        };

        var entry = GcpKmsEntryParser.Parse(dict);

        Assert.NotNull(entry.CreatedAt);
    }

    [Fact]
    public void Parse_MissingResourceName_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["enc"] = "no-resource-name"
        };

        Assert.Throws<ArgumentException>(() => GcpKmsEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_MissingEnc_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["resource_name"] = "projects/test/locations/global/keyRings/kr/cryptoKeys/ck"
        };

        Assert.Throws<ArgumentException>(() => GcpKmsEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_NonDictionaryInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GcpKmsEntryParser.Parse("not-a-dict"));
        Assert.Throws<ArgumentException>(() => GcpKmsEntryParser.Parse(42));
        Assert.Throws<ArgumentException>(() => GcpKmsEntryParser.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyResourceName_ReturnsEmptyString()
    {
        var dict = new Dictionary<string, object>
        {
            ["resource_name"] = "",
            ["enc"] = "empty-resource"
        };

        var entry = GcpKmsEntryParser.Parse(dict);

        Assert.Equal("", entry.ResourceName);
    }
}
