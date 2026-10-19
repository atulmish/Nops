using System;
using System.Linq;
using System.Text;
using Nops.Core.Stores;
using Xunit;

namespace Nops.Core.Tests.Stores;

public class YamlStoreTests
{
    private static TreeBranch Load(string yaml)
    {
        var tree = YamlStore.LoadEncrypted(Encoding.UTF8.GetBytes(yaml), "test.yaml");
        return tree.Branches[0];
    }

    private static object? Value(TreeBranch branch, string key) =>
        branch.First(i => i.Key is string s && s == key).Value;

    [Fact]
    public void PlainScalars_AreTypedLikeYamlV3()
    {
        var branch = Load(
            "a_bool: true\n" +
            "a_false: false\n" +
            "an_int: 42\n" +
            "a_neg: -7\n" +
            "a_hex: 0x10\n" +
            "an_octal: 010\n" +
            "a_float: 3.14\n" +
            "a_whole: 1.0\n" +
            "an_exp: 1e3\n" +
            "a_null: null\n" +
            "a_tilde: ~\n" +
            "a_date: 2024-01-02\n" +
            "a_dt: 2024-01-01T10:20:30Z\n" +
            "a_str: hello\n" +
            "yes_is_str: yes\n");

        Assert.Equal(true, Value(branch, "a_bool"));
        Assert.Equal(false, Value(branch, "a_false"));
        Assert.Equal(42L, Value(branch, "an_int"));
        Assert.Equal(-7L, Value(branch, "a_neg"));
        Assert.Equal(16L, Value(branch, "a_hex"));
        Assert.Equal(8L, Value(branch, "an_octal"));
        Assert.Equal(3.14d, Value(branch, "a_float"));
        Assert.Equal(1.0d, Value(branch, "a_whole"));
        Assert.Equal(1000d, Value(branch, "an_exp"));
        Assert.Null(Value(branch, "a_null"));
        Assert.Null(Value(branch, "a_tilde"));
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), Value(branch, "a_date"));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 20, 30, TimeSpan.Zero), Value(branch, "a_dt"));
        Assert.Equal("hello", Value(branch, "a_str"));
        // yaml.v3 does NOT resolve yes/no/on/off as booleans.
        Assert.Equal("yes", Value(branch, "yes_is_str"));
    }

    [Fact]
    public void QuotedScalars_StayStrings()
    {
        var branch = Load(
            "q_bool: \"true\"\n" +
            "q_int: '42'\n" +
            "q_null: \"null\"\n");

        Assert.Equal("true", Value(branch, "q_bool"));
        Assert.Equal("42", Value(branch, "q_int"));
        Assert.Equal("null", Value(branch, "q_null"));
    }

    [Fact]
    public void Comments_AreCapturedWithLeadingSpace()
    {
        var branch = Load(
            "# head comment\n" +
            "key: value\n" +
            "# another\n" +
            "key2: v2\n");

        var comments = branch.Where(i => i.Key is Comment).Select(i => (Comment)i.Key).ToList();

        Assert.Contains(comments, c => c.Value == " head comment" && !c.Inline);
        Assert.Contains(comments, c => c.Value == " another" && !c.Inline);
    }

    [Fact]
    public void InlineComment_IsInlineAndPrecedesValue()
    {
        var branch = Load("key: value # inline comment\n");

        var commentIndex = branch.FindIndex(i => i.Key is Comment c && c.Value == " inline comment");
        var valueIndex = branch.FindIndex(i => i.Key is string s && s == "key");

        Assert.True(commentIndex >= 0, "inline comment should be captured");
        Assert.True(((Comment)branch[commentIndex].Key!).Inline);
        Assert.True(commentIndex < valueIndex, "inline comment should precede its value");
    }

    [Fact]
    public void SequenceComments_AreValueComments()
    {
        var branch = Load(
            "list:\n" +
            "  - one\n" +
            "  # before two\n" +
            "  - two\n" +
            "  - three # trailing\n");

        var list = (TreeBranch)Value(branch, "list")!;

        // A comment in a sequence is represented as a Comment value, in order.
        Assert.Contains(list, i => i.Value is Comment c && c.Value == " before two");
        Assert.Contains(list, i => i.Value is Comment c && c.Value == " trailing" && c.Inline);
    }

    [Fact]
    public void DuplicateSopsKey_Throws()
    {
        var yaml = "a: 1\nsops:\n  version: \"3.13.3\"\nsops:\n  version: \"3.13.3\"\n";

        Assert.Throws<SopsMetadataException>(
            () => YamlStore.LoadEncrypted(Encoding.UTF8.GetBytes(yaml), "test.yaml"));
    }

    [Fact]
    public void RootNotMapping_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => YamlStore.LoadEncrypted(Encoding.UTF8.GetBytes("- a\n- b\n"), "test.yaml"));
    }
}
