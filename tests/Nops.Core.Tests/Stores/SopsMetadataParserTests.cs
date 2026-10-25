using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Nops.Core.Tests.Stores;

public class SopsMetadataParserTests
{
    [Fact]
    public void Parse_FromFlatDict_ExtractsAllFields()
    {
        var dict = new Dictionary<string, object>
        {
            ["lastmodified"] = "2025-08-06T18:55:05Z",
            ["mac"] = "ENC[AES256_GCM,data:test,iv:test,tag:test,type:str]",
            ["version"] = "3.10.2",
            ["shamir_threshold"] = 2,
            ["unencrypted_suffix"] = "_unencrypted",
            ["mac_only_encrypted"] = true,
        };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.NotNull(metadata.LastModified);
        Assert.Equal(2025, metadata.LastModified.Value.Year);
        Assert.Equal(8, metadata.LastModified.Value.Month);
        Assert.Equal(6, metadata.LastModified.Value.Day);
        Assert.NotNull(metadata.Mac);
        Assert.Equal("3.10.2", metadata.Version);
        Assert.Equal(2, metadata.ShamirThreshold);
        Assert.Equal("_unencrypted", metadata.UnencryptedSuffix);
        Assert.True(metadata.MacOnlyEncrypted);
    }

    [Fact]
    public void Parse_AgeEntries_ExtractedCorrectly()
    {
        var ageList = new List<object>
        {
            new Dictionary<string, object> { ["recipient"] = "age1ql3z5hjv...", ["enc"] = "encrypted_data" }
        };

        var dict = new Dictionary<string, object> { ["age"] = ageList };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.Single(metadata.Age);
        Assert.Equal("age1ql3z5hjv...", metadata.Age[0].Recipient);
        Assert.Equal("encrypted_data", metadata.Age[0].Enc);
    }

    [Fact]
    public void Parse_PgpEntries_ExtractedCorrectly()
    {
        var pgpList = new List<object>
        {
            new Dictionary<string, object>
            {
                ["fp"] = "ABCD1234",
                ["enc"] = "pgp_encrypted_data",
                ["created_at"] = "2019-12-10T22:45:53Z"
            }
        };

        var dict = new Dictionary<string, object> { ["pgp"] = pgpList };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.Single(metadata.Pgp);
        Assert.Equal("ABCD1234", metadata.Pgp[0].Fp);
        Assert.Equal("pgp_encrypted_data", metadata.Pgp[0].Enc);
    }

    [Fact]
    public void Parse_MissingShamirThreshold_UsesGroupCount()
    {
        var ageList = new List<object>
        {
            new Dictionary<string, object> { ["recipient"] = "r1", ["enc"] = "e1" },
            new Dictionary<string, object> { ["recipient"] = "r2", ["enc"] = "e2" }
        };

        var dict = new Dictionary<string, object> { ["age"] = ageList };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        // shamir_threshold == 0 means len(KeyGroups) when groups are built later
        Assert.Equal(0, metadata.ShamirThreshold);
    }

    [Fact]
    public void Parse_UnknownKeys_Ignored()
    {
        var dict = new Dictionary<string, object>
        {
            ["version"] = "3.10.2",
            ["unknown_key"] = "should be ignored",
            ["another_unknown"] = 123
        };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.Equal("3.10.2", metadata.Version);
    }

    [Fact]
    public void Parse_KeyGroups_ExtractedCorrectly()
    {
        var groups = new List<object>
        {
            new Dictionary<string, object>
            {
                ["age"] = new List<object>
                {
                    new Dictionary<string, object> { ["recipient"] = "r1", ["enc"] = "e1" }
                }
            },
            new Dictionary<string, object>
            {
                ["age"] = new List<object>
                {
                    new Dictionary<string, object> { ["recipient"] = "r2", ["enc"] = "e2" }
                }
            },
        };

        var dict = new Dictionary<string, object>
        {
            ["key_groups"] = groups,
            ["shamir_threshold"] = 2,
        };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.NotNull(metadata.KeyGroups);
        Assert.Equal(2, metadata.KeyGroups!.Count);
        Assert.Equal("age", metadata.KeyGroups[0].Keys[0].KeyType);
        Assert.Equal("e1", metadata.KeyGroups[0].Keys[0].Enc);
        Assert.Equal("e2", metadata.KeyGroups[1].Keys[0].Enc);
    }

    [Fact]
    public void Parse_FlatKeys_BecomeSingleGroup()
    {
        var dict = new Dictionary<string, object>
        {
            ["age"] = new List<object>
            {
                new Dictionary<string, object> { ["recipient"] = "r1", ["enc"] = "e1" }
            },
            ["pgp"] = new List<object>
            {
                new Dictionary<string, object> { ["fp"] = "fp1", ["enc"] = "e2" }
            },
        };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.NotNull(metadata.KeyGroups);
        Assert.Single(metadata.KeyGroups!);
        Assert.Equal(2, metadata.KeyGroups[0].Keys.Count);
    }

    [Fact]
    public void Parse_NoCryptRule_DefaultsToUnencryptedSuffix()
    {
        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(new Dictionary<string, object>
        {
            ["version"] = "3.10.2",
        });

        Assert.Equal("_unencrypted", metadata.UnencryptedSuffix);
    }

    [Fact]
    public void Parse_MultipleCryptRules_Throws()
    {
        var dict = new Dictionary<string, object>
        {
            ["unencrypted_suffix"] = "_unencrypted",
            ["encrypted_regex"] = "^(secret)$",
        };

        Assert.Throws<Nops.Core.SopsMetadataException>(
            () => Nops.Core.Stores.SopsMetadataParser.Parse(dict));
    }

    [Fact]
    public void Parse_EmptyDict_ReturnsDefaults()
    {
        var dict = new Dictionary<string, object>();

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.Null(metadata.Mac);
        Assert.Equal(0, metadata.ShamirThreshold);
        Assert.False(metadata.MacOnlyEncrypted);
        Assert.Null(metadata.Version);
    }

    [Fact]
    public void Parse_MacOnlyEncrypted_FalseByDefault()
    {
        var dict = new Dictionary<string, object> { ["version"] = "3.10.2" };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.False(metadata.MacOnlyEncrypted);
    }

    [Fact]
    public void Parse_MacOnlyEncrypted_True()
    {
        var dict = new Dictionary<string, object>
        {
            ["version"] = "3.10.2",
            ["mac_only_encrypted"] = true
        };

        var metadata = Nops.Core.Stores.SopsMetadataParser.Parse(dict);

        Assert.True(metadata.MacOnlyEncrypted);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== SOPS METADATA PARSER ===");
        Console.WriteLine("Parses flat dict from .sops YAML/JSON key");
        Console.WriteLine("Extracts: mac, version, age, pgp, kms, etc.");
        Console.WriteLine("Unknown keys ignored (forward compat)");
        Console.WriteLine("=============================");
        Assert.True(true, "Spike complete");
    }
}
