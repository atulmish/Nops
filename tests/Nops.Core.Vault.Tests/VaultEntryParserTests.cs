using System;
using Nops.Core.Keys;

namespace Nops.Core.Vault.Tests;

/// <summary>
/// Tests for VaultEntryParser - parsing opaque .sops HashiCorp entries.
/// </summary>
public class VaultTransitEntryParserTests
{
    [Fact]
    public void ParseVaultTransit_ValidEntry_ReturnsCorrectValues()
    {
        var dict = new Dictionary<string, object>
        {
            ["address"] = "https://vault.example.com:8200",
            ["mount_path"] = "transit",
            ["key"] = "my-transit-key",
            ["enc"] = "encrypted-data-key-share-base64"
        };

        var entry = VaultEntryParser.ParseVaultTransit(dict);

        Assert.Equal("https://vault.example.com:8200", entry.Address);
        Assert.Equal("transit", entry.MountPath);
        Assert.Equal("my-transit-key", entry.KeyName);
        Assert.Equal("encrypted-data-key-share-base64", entry.Enc);
        Assert.Null(entry.Version);
    }

    [Fact]
    public void ParseVaultTransit_WithVersion_ReturnsKeyVersion()
    {
        var dict = new Dictionary<string, object>
        {
            ["address"] = "https://vault.example.com:8200",
            ["mount_path"] = "transit",
            ["key"] = "my-key",
            ["enc"] = "share-data",
            ["version"] = "5"
        };

        var entry = VaultEntryParser.ParseVaultTransit(dict);

        Assert.Equal("5", entry.Version);
    }

    [Fact]
    public void ParseVaultTransit_MissingAddress_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["key"] = "my-key",
            ["enc"] = "no-address"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit(dict));
    }

    [Fact]
    public void ParseVaultTransit_MissingKey_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["address"] = "https://vault.example.com:8200",
            ["enc"] = "no-key"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit(dict));
    }

    [Fact]
    public void ParseVaultTransit_MissingEnc_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["address"] = "https://vault.example.com:8200",
            ["key"] = "my-key"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit(dict));
    }

    [Fact]
    public void ParseHckms_ValidEntry_ReturnsCorrectValues()
    {
        var dict = new Dictionary<string, object>
        {
            ["project_id"] = "my-gcp-project",
            ["location"] = "global",
            ["key_ring"] = "my-keyring",
            ["crypto_key"] = "my-hckms-key",
            ["enc"] = "encrypted-share"
        };

        var entry = VaultEntryParser.ParseHckms(dict);

        Assert.Equal("my-gcp-project", entry.ProjectId);
        Assert.Equal("global", entry.Location);
        Assert.Equal("my-keyring", entry.KeyRing);
        Assert.Equal("my-hckms-key", entry.CryptoKey);
        Assert.Equal("encrypted-share", entry.Enc);
    }

    [Fact]
    public void ParseHckms_WithAltKeyNames_ReturnsCorrectValues()
    {
        var dict = new Dictionary<string, object>
        {
            ["project_id"] = "my-project",
            ["location"] = "us-central1",
            ["keyring"] = "kr",  // alternate spelling
            ["cryptokey"] = "ck", // alternate spelling
            ["enc"] = "share"
        };

        var entry = VaultEntryParser.ParseHckms(dict);

        Assert.Equal("kr", entry.KeyRing);
        Assert.Equal("ck", entry.CryptoKey);
    }

    [Fact]
    public void ParseHckms_MissingProjectId_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["location"] = "global",
            ["key_ring"] = "kr",
            ["crypto_key"] = "ck",
            ["enc"] = "share"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms(dict));
    }

    [Fact]
    public void ParseHckms_MissingKeyRing_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["project_id"] = "my-project",
            ["location"] = "global",
            ["crypto_key"] = "ck",
            ["enc"] = "share"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms(dict));
    }

    [Fact]
    public void ParseHckms_MissingCryptoKey_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["project_id"] = "my-project",
            ["location"] = "global",
            ["key_ring"] = "kr",
            ["enc"] = "share"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms(dict));
    }

    [Fact]
    public void ParseHckms_MissingEnc_ThrowsArgumentException()
    {
        var dict = new Dictionary<string, object>
        {
            ["project_id"] = "my-project",
            ["location"] = "global",
            ["key_ring"] = "kr",
            ["crypto_key"] = "ck"
        };

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms(dict));
    }

    [Fact]
    public void ParseVaultTransit_NonDictionaryInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit("not-a-dict"));
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit(42));
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit(null!));
    }

    [Fact]
    public void ParseHckms_NonDictionaryInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms("not-a-dict"));
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms(42));
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseHckms(null!));
    }

    [Fact]
    public void ParseVaultTransit_DefaultMountPath_ReturnsTransit()
    {
        var dict = new Dictionary<string, object>
        {
            ["address"] = "https://vault.example.com:8200",
            ["key"] = "my-key",
            ["enc"] = "share"
            // No mount_path provided
        };

        var entry = VaultEntryParser.ParseVaultTransit(dict);

        Assert.Equal("transit", entry.MountPath);
    }
}
