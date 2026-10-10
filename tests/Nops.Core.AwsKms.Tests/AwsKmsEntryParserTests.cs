using System;
using System.Collections.Generic;
using Nops.Core.Keys;

namespace Nops.Core.AwsKms.Tests;

/// <summary>
/// Tests for AwsKmsEntryParser - parsing opaque .sops KMS dict entries.
/// </summary>
public class AwsKmsEntryParserTests
{
    [Fact]
    public void Parse_ValidEntry_ReturnsCorrectArnAndEnc()
    {
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:us-east-1:123456789012:key/abcd-1234-5678-efgh",
            ["enc"] = "encrypted-data-key-share-base64"
        };

        var entry = AwsKmsEntryParser.Parse(dict);

        Assert.Equal("arn:aws:kms:us-east-1:123456789012:key/abcd-1234-5678-efgh", entry.Arn);
        Assert.Equal("encrypted-data-key-share-base64", entry.Enc);
        Assert.Empty(entry.Context);
        Assert.Null(entry.CreatedAt);
    }

    [Fact]
    public void Parse_WithEncryptionContext_ReturnsContextPairs()
    {
        var contextDict = new Dictionary<string, object>
        {
            ["Project"] = "Nops",
            ["Environment"] = "test"
        };
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:eu-west-1:999:key/xyz-789",
            ["enc"] = "some-encrypted-share",
            ["context"] = contextDict
        };

        var entry = AwsKmsEntryParser.Parse(dict);

        Assert.Equal(2, entry.Context.Count);
        Assert.Contains(new KeyValuePair<string, string>("Project", "Nops"), entry.Context);
        Assert.Contains(new KeyValuePair<string, string>("Environment", "test"), entry.Context);
    }

    [Fact]
    public void Parse_WithListContext_ReturnsContextPairs()
    {
        var contextList = new List<object>
        {
            new Dictionary<string, object> { ["key"] = "Team", ["value"] = "platform" },
            new Dictionary<string, object> { ["key"] = "App", ["value"] = "secrets" }
        };
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:ap-south-1:111:key/abc-123",
            ["enc"] = "list-context-share",
            ["context"] = contextList
        };

        var entry = AwsKmsEntryParser.Parse(dict);

        Assert.Equal(2, entry.Context.Count);
        Assert.Contains(new KeyValuePair<string, string>("Team", "platform"), entry.Context);
        Assert.Contains(new KeyValuePair<string, string>("App", "secrets"), entry.Context);
    }

    [Fact]
    public void Parse_WithCreatedAt_ReturnsDateTimeOffset()
    {
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:us-west-2:222:key/def-456",
            ["enc"] = "timestamped-share",
            ["created_at"] = DateTimeOffset.Parse("2025-01-15T10:30:00Z")
        };

        var entry = AwsKmsEntryParser.Parse(dict);

        Assert.NotNull(entry.CreatedAt);
        Assert.Equal(new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero), entry.CreatedAt.Value);
    }

    [Fact]
    public void Parse_WithCreatedAtString_ReturnsDateTimeOffset()
    {
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:us-west-2:222:key/def-456",
            ["enc"] = "timestamped-share",
            ["created_at"] = "2025-06-01T00:00:00Z"
        };

        var entry = AwsKmsEntryParser.Parse(dict);

        Assert.NotNull(entry.CreatedAt);
    }

    [Fact]
    public void Parse_MissingArn_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["enc"] = "no-arn-share"
        };

        Assert.Throws<ArgumentException>(() => AwsKmsEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_MissingEnc_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:us-east-1:0:key/test"
        };

        Assert.Throws<ArgumentException>(() => AwsKmsEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_NonDictionaryInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AwsKmsEntryParser.Parse("not-a-dict"));
        Assert.Throws<ArgumentException>(() => AwsKmsEntryParser.Parse(42));
        Assert.Throws<ArgumentException>(() => AwsKmsEntryParser.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyContext_ReturnsEmptyList()
    {
        var dict = new Dictionary<string, object>
        {
            ["arn"] = "arn:aws:kms:us-east-1:0:key/empty",
            ["enc"] = "no-context"
        };

        var entry = AwsKmsEntryParser.Parse(dict);

        Assert.Empty(entry.Context);
    }
}
