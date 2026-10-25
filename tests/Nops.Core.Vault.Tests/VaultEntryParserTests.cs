using Nops.Core.Keys;

namespace Nops.Core.Vault.Tests;

/// <summary>Parsing the opaque <c>hc_vault</c> entries sops writes into the file metadata.</summary>
public class VaultTransitEntryParserTests
{
    private static Dictionary<string, object> Entry() => new()
    {
        ["vault_address"] = "https://vault.example.com:8200",
        ["engine_path"] = "transit",
        ["key_name"] = "my-transit-key",
        ["enc"] = "vault:v1:abcd",
        ["created_at"] = "2026-10-25T00:10:19Z",
    };

    [Fact]
    public void ParseVaultTransit_ValidEntry_ReturnsCorrectValues()
    {
        var entry = VaultEntryParser.ParseVaultTransit(Entry());

        Assert.Equal("https://vault.example.com:8200", entry.Address);
        Assert.Equal("transit", entry.MountPath);
        Assert.Equal("my-transit-key", entry.KeyName);
        Assert.Equal("vault:v1:abcd", entry.Enc);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T00:10:19Z"), entry.CreatedAt);
    }

    [Fact]
    public void ParseVaultTransit_MissingEnginePath_DefaultsToTransit()
    {
        var dict = Entry();
        dict.Remove("engine_path");

        Assert.Equal("transit", VaultEntryParser.ParseVaultTransit(dict).MountPath);
    }

    [Theory]
    [InlineData("vault_address")]
    [InlineData("key_name")]
    [InlineData("enc")]
    public void ParseVaultTransit_MissingRequiredField_Throws(string field)
    {
        var dict = Entry();
        dict.Remove(field);

        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit(dict));
    }

    [Fact]
    public void ParseVaultTransit_NotADictionary_Throws() =>
        Assert.Throws<ArgumentException>(() => VaultEntryParser.ParseVaultTransit("nope"));
}
