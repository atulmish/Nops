using System.Text;
using System.Text.Json;
using Nops.Core.Crypto;
using Nops.Core.Stores;

namespace Nops.Core.Tests.Stores;

/// <summary>Edge cases of the dotenv, JSON, INI and binary stores and the metadata decoding behind them.</summary>
public class StoreEdgeCaseTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private const string DotenvWithMetadata =
        "# a comment\n" +
        "KEY=value\n" +
        "sops_age__list_0__map_enc=age-blob\n" +
        "sops_age__list_0__map_recipient=age1xyz\n" +
        "sops_pgp__list_0__map_enc=pgp-blob\n" +
        "sops_pgp__list_0__map_fp=ABCDEF\n" +
        "sops_mac=mac-value\n" +
        "sops_shamir_threshold=2\n" +
        "sops_unencrypted_suffix=_unencrypted\n" +
        "sops_version=3.9.0\n" +
        "sops_lastmodified=2024-01-01T00:00:00Z\n";

    [Fact]
    public void Dotenv_KeepsCommentsAndSplitsOffMetadata()
    {
        var tree = DotenvStore.LoadEncrypted(Bytes(DotenvWithMetadata), "x.env");

        var items = tree.Branches[0];
        Assert.Contains(items, i => i.Key is Comment c && c.Value.Contains("a comment"));
        Assert.Contains(items, i => i.Key is string k && k == "KEY");
        Assert.DoesNotContain(items, i => i.Key is string k && k.StartsWith("sops_"));
        Assert.Equal("age1xyz", Assert.Single(tree.Metadata.Age).Recipient);
        Assert.Equal("ABCDEF", Assert.Single(tree.Metadata.Pgp).Fp);
        Assert.Equal(2, tree.Metadata.ShamirThreshold);
    }

    [Fact]
    public void Dotenv_FlattensItsMetadataBack()
    {
        var tree = DotenvStore.LoadEncrypted(Bytes(DotenvWithMetadata), "x.env");

        var flat = DotenvStore.FlattenToDotenv(tree);

        Assert.Contains("sops_age__list_0__map_recipient=age1xyz", flat);
        Assert.Contains("sops_pgp__list_0__map_enc=pgp-blob", flat);
        Assert.Contains("sops_pgp__list_0__map_fp=ABCDEF", flat);
        Assert.Contains("sops_mac=mac-value", flat);
        Assert.Contains("sops_shamir_threshold=2", flat);
        Assert.Contains("sops_unencrypted_suffix=_unencrypted", flat);
        Assert.Contains("sops_version=3.9.0", flat);
        Assert.Contains("sops_lastmodified=2024-01-01T00:00:00Z", flat);
    }

    [Fact]
    public void Dotenv_EscapesAndUnescapesNewlines()
    {
        var escaped = DotenvStore.EscapeNewlines("a\nb");

        Assert.DoesNotContain('\n', escaped);
        Assert.Equal("a\nb", DotenvStore.UnescapeNewlines(escaped));
    }

    [Fact]
    public void FlatMetadata_BuildsNestedListsAndMaps()
    {
        var decoded = FlatMetadataDecoder.Decode(new Dictionary<string, string>
        {
            ["kms__list_0__map_arn"] = "arn-a",
            ["kms__list_0__map_context__map_app"] = "nops",
            ["kms__list_1__map_arn"] = "arn-b",
            ["plain"] = "value",
        });

        var kms = Assert.IsType<List<object>>(decoded["kms"]);
        Assert.Equal(2, kms.Count);
        var first = Assert.IsType<Dictionary<string, object>>(kms[0]);
        Assert.Equal("arn-a", first["arn"]);
        Assert.Equal("nops", Assert.IsType<Dictionary<string, object>>(first["context"])["app"]);
        Assert.Equal("value", decoded["plain"]);
    }

    [Fact]
    public void FlatMetadata_ListAtTheRoot_IsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            FlatMetadataDecoder.Decode(new Dictionary<string, string> { ["list_0"] = "x" }));

    [Fact]
    public void FlatMetadata_LeafInAList_ReplacesTheSlot()
    {
        var decoded = FlatMetadataDecoder.Decode(new Dictionary<string, string> { ["names__list_1"] = "second" });

        var names = Assert.IsType<List<object>>(decoded["names"]);
        Assert.Equal("second", names[1]);
    }

    [Fact]
    public void FlatMetadata_RebuildsAMapThatWasAList()
    {
        var decoded = FlatMetadataDecoder.Decode(new Dictionary<string, string>
        {
            ["a__map_x"] = "1",
            ["a__map_y__map_z"] = "2",
        });

        var a = Assert.IsType<Dictionary<string, object>>(decoded["a"]);
        Assert.Equal("1", a["x"]);
        Assert.Equal("2", Assert.IsType<Dictionary<string, object>>(a["y"])["z"]);
    }

    [Fact]
    public void Json_MoreThanOneSopsKey_IsRejected() =>
        Assert.Throws<SopsMetadataException>(() =>
            JsonStore.LoadEncrypted(Bytes("{\"a\":1,\"sops\":{},\"sops\":{}}"), "t.json"));

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("1.5", 1.5)]
    [InlineData("9223372036854775808", 9.2233720368547758E+18)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Json_ScalarsKeepTheirTypes(string json, object expected) =>
        Assert.Equal(expected, JsonStore.ParseJson(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void Json_NullAndNestedValues()
    {
        Assert.Null(JsonStore.ParseJson(JsonDocument.Parse("null").RootElement));
        var list = Assert.IsType<TreeBranch>(JsonStore.ParseJson(JsonDocument.Parse("[1,[2]]").RootElement));
        Assert.True(list.IsSequence);
    }

    [Fact]
    public void Metadata_ToleratesMalformedEntriesAndStringNumbers()
    {
        var metadata = SopsMetadataParser.Parse(new Dictionary<string, object>
        {
            ["age"] = new List<object> { "junk" },
            ["pgp"] = new List<object> { 5 },
            ["shamir_threshold"] = "3",
            ["mac_only_encrypted"] = "true",
        });

        Assert.Equal("", Assert.Single(metadata.Age).Enc);
        Assert.Equal("", Assert.Single(metadata.Pgp).Fp);
        Assert.Equal(3, metadata.ShamirThreshold);
        Assert.True(metadata.MacOnlyEncrypted);
    }

    [Fact]
    public void Binary_RequiresADataProperty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BinaryStore.LoadEncrypted(Bytes("{\"sops\":{}}"), "t.bin"));

        Assert.Contains("'data'", ex.Message);
    }

    [Fact]
    public void Binary_EncryptIsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => BinaryStore.Encrypt(null!, []));

    [Fact]
    public void Ini_TrailingCommentsAreKept()
    {
        var tree = IniStore.LoadEncrypted(Bytes("[a]\nk=v\n; trailing note\n"), "t.ini");

        Assert.Contains(tree.Branches[0], i => i.Value is TreeBranch b && b.Any(x => x.Key is Comment c && c.Value.Contains("trailing note")));
    }

    [Fact]
    public void TreeConverter_UnwrapsComments_AndLeavesOtherValues()
    {
        Assert.Equal("note", TreeConverter.ToObject(new Comment("note")));
        Assert.Equal(5L, TreeConverter.ToObject(5L));
    }

    [Fact]
    public void Mac_VerificationNeedsAStoredMac()
    {
        var tree = new SopsTree([new TreeBranch()], SopsMetadataParser.Empty(), "t");

        var ex = Assert.Throws<InvalidOperationException>(() => MacVerifier.VerifyMac(tree, new byte[32], "ABC"));

        Assert.Contains("No MAC found", ex.Message);
    }

    [Fact]
    public void Shamir_RejectsBadArguments()
    {
        Assert.Empty(Shamir.Split([1], 0, 0));
        Assert.Throws<ArgumentException>(() => Shamir.Split([1], 2, 3));
        Assert.Throws<ArgumentException>(() => Shamir.Combine([[1, 2]]));
        Assert.Throws<ArgumentException>(() => Shamir.Combine([[1], [2]]));
        Assert.Throws<ArgumentException>(() => Shamir.Combine([[1, 2], [3, 4, 5]]));
    }
}
