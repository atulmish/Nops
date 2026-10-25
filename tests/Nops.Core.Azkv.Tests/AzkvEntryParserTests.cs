using System;
using Nops.Core.Keys;

namespace Nops.Core.Azkv.Tests;

/// <summary>
/// Tests for AzkvEntryParser - parsing opaque .sops Azure KV dict entries.
/// </summary>
public class AzkvEntryParserTests
{
    [Fact]
    public void Parse_ValidEntry_ReturnsCorrectVaultNameAndEnc()
    {
        var dict = new Dictionary<string, object>
        {
            ["vaultUrl"] = "https://my-vault.vault.azure.net",
            ["name"] = "my-secret-key",
            ["enc"] = "encrypted-data-key-share-base64"
        };

        var entry = AzkvEntryParser.Parse(dict);

        Assert.Equal("https://my-vault.vault.azure.net", entry.VaultUrl);
        Assert.Equal("my-secret-key", entry.KeyName);
        Assert.Null(entry.KeyVersion);
        Assert.Equal("encrypted-data-key-share-base64", entry.Enc);
    }

    [Fact]
    public void Parse_WithVersion_ReturnsKeyVersion()
    {
        var dict = new Dictionary<string, object>
        {
            ["vaultUrl"] = "https://my-vault.vault.azure.net",
            ["name"] = "my-key",
            ["enc"] = "share-data",
            ["version"] = "a1b2c3d4e5f6"
        };

        var entry = AzkvEntryParser.Parse(dict);

        Assert.Equal("a1b2c3d4e5f6", entry.KeyVersion);
    }

    [Fact]
    public void Parse_WithCreatedAt_ReturnsDateTimeOffset()
    {
        var dict = new Dictionary<string, object>
        {
            ["vaultUrl"] = "https://my-vault.vault.azure.net",
            ["name"] = "my-key",
            ["enc"] = "share-data",
            ["created_at"] = DateTimeOffset.Parse("2025-01-20T12:00:00Z")
        };

        var entry = AzkvEntryParser.Parse(dict);

        Assert.NotNull(entry.CreatedAt);
    }

    [Fact]
    public void Parse_MissingVault_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["name"] = "my-key",
            ["enc"] = "no-vault"
        };

        Assert.Throws<ArgumentException>(() => AzkvEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_MissingName_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["vaultUrl"] = "https://my-vault.vault.azure.net",
            ["enc"] = "no-name"
        };

        Assert.Throws<ArgumentException>(() => AzkvEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_MissingEnc_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["vaultUrl"] = "https://my-vault.vault.azure.net",
            ["name"] = "my-key"
        };

        Assert.Throws<ArgumentException>(() => AzkvEntryParser.Parse(dict));
    }

    [Fact]
    public void Parse_NonDictionaryInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AzkvEntryParser.Parse("not-a-dict"));
        Assert.Throws<ArgumentException>(() => AzkvEntryParser.Parse(42));
        Assert.Throws<ArgumentException>(() => AzkvEntryParser.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyVault_ReturnsEmptyString()
    {
        var dict = new Dictionary<string, object>
        {
            ["vaultUrl"] = "",
            ["name"] = "my-key",
            ["enc"] = "empty-vault"
        };

        var entry = AzkvEntryParser.Parse(dict);

        Assert.Equal("", entry.VaultUrl);
    }
}
