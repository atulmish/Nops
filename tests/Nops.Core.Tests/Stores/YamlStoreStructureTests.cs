using System.Text;
using Nops.Core.Stores;

namespace Nops.Core.Tests.Stores;

/// <summary>Real-world YAML shapes: comments in every position, sequences, aliases, flow style, multi-line scalars.</summary>
public class YamlStoreStructureTests
{
    private static TreeBranch Load(string yaml) =>
        YamlStore.LoadEncrypted(Encoding.UTF8.GetBytes(yaml), "t.yaml").Branches[0];

    private static object? Value(TreeBranch branch, string key) =>
        branch.First(i => i.Key is string s && s == key).Value;

    private static IEnumerable<string> CommentTexts(TreeBranch branch) =>
        branch.SelectMany(i => new[] { i.Key, i.Value }).OfType<Comment>().Select(c => c.Value.Trim());

    [Fact]
    public void CommentBetweenKeyAndValue_IsKept()
    {
        var branch = Load("key: # about key\n  value\nother: 1\n");

        Assert.Equal("value", Value(branch, "key"));
        Assert.Contains("about key", CommentTexts(branch));
    }

    [Fact]
    public void InlineCommentAfterValue_IsKeptAsInline()
    {
        var branch = Load("a: 1 # trailing\nb: 2\n");

        var comment = branch.Select(i => i.Key).OfType<Comment>().Concat(branch.Select(i => i.Value).OfType<Comment>()).Single();
        Assert.True(comment.Inline);
        Assert.Equal("trailing", comment.Value.Trim());
    }

    [Fact]
    public void FootComments_OfNestedBlocks_BelongToTheRightLevel()
    {
        var yaml =
            "outer:\n" +
            "  inner:\n" +
            "    a: 1\n" +
            "    # belongs to inner\n" +
            "  # belongs to outer\n" +
            "# belongs to root\n" +
            "last: 2\n";

        var branch = Load(yaml);

        Assert.Equal(2L, Value(branch, "last"));
        var all = CommentTexts(branch).Concat(branch.Select(i => i.Value).OfType<TreeBranch>().SelectMany(b => CommentTexts(b)))
            .Concat(branch.Select(i => i.Value).OfType<TreeBranch>().SelectMany(b => b.Select(x => x.Value).OfType<TreeBranch>().SelectMany(CommentTexts)))
            .ToList();
        Assert.Contains("belongs to inner", all);
        Assert.Contains("belongs to outer", all);
        Assert.Contains("belongs to root", all);
    }

    [Fact]
    public void CommentAtTheEndOfTheDocument_IsKept()
    {
        var branch = Load("a: 1\n# the end\n");

        Assert.Contains("the end", CommentTexts(branch));
    }

    [Fact]
    public void SequencesOfMappingsAndScalars_KeepTheirOrder()
    {
        var branch = Load(
            "items:\n" +
            "  - name: first\n" +
            "    port: 1\n" +
            "  # between items\n" +
            "  - name: second\n" +
            "  - plain\n" +
            "  - - nested\n" +
            "    - list\n");

        var items = Assert.IsType<TreeBranch>(Value(branch, "items"));
        Assert.True(items.IsSequence);
        Assert.Equal(4, items.Count(i => i.Value is not Comment && i.Key is not Comment));
        Assert.Contains("between items", CommentTexts(items));
    }

    [Fact]
    public void AliasesInSequences_AndMappingMerge_AreExpanded()
    {
        var branch = Load(
            "defaults: &d\n" +
            "  host: localhost\n" +
            "  port: 5432\n" +
            "list:\n" +
            "  - *d\n" +
            "  - &s scalar\n" +
            "  - *s\n");

        var list = Assert.IsType<TreeBranch>(Value(branch, "list"));
        Assert.Equal(3, list.Count);
        var expanded = Assert.IsType<TreeBranch>(list[0].Value);
        Assert.Equal("localhost", expanded.First(i => (string)i.Key == "host").Value);
        Assert.Equal("scalar", list[2].Value);
    }

    [Fact]
    public void FlowCollections_AndEmptyOnes_AreParsed()
    {
        var branch = Load("map: {a: 1, b: [x, y]}\nempty_map: {}\nempty_list: []\n");

        var map = Assert.IsType<TreeBranch>(Value(branch, "map"));
        Assert.Equal(2, map.Count);
        Assert.Empty(Assert.IsType<TreeBranch>(Value(branch, "empty_map")));
        Assert.Empty(Assert.IsType<TreeBranch>(Value(branch, "empty_list")));
    }

    [Fact]
    public void MultiLineScalars_KeepTheirText()
    {
        var branch = Load("literal: |\n  line one\n  line two\nfolded: >\n  folded\n  text\nquoted: \"a\\nb\"\n");

        Assert.Equal("line one\nline two\n", Value(branch, "literal"));
        Assert.Equal("folded text\n", Value(branch, "folded"));
        Assert.Equal("a\nb", Value(branch, "quoted"));
    }

    [Fact]
    public void QuotedAndNumericKeys_AreStrings()
    {
        var branch = Load("\"quoted key\": 1\n42: answer\ntrue: yes\n");

        Assert.Equal(1L, Value(branch, "quoted key"));
        Assert.Equal("answer", Value(branch, "42"));
        Assert.Equal("yes", Value(branch, "true"));
    }

    [Fact]
    public void NullValues_AreKept()
    {
        var branch = Load("a:\nb: null\nc: ~\nitems:\n  -\n  - x\n");

        Assert.Null(Value(branch, "a"));
        Assert.Null(Value(branch, "b"));
        Assert.Null(Value(branch, "c"));
    }

    [Fact]
    public void WindowsLineEndings_AreAccepted()
    {
        var branch = Load("a: 1\r\n# note\r\nb: 2\r\n");

        Assert.Equal(2L, Value(branch, "b"));
        Assert.Contains("note", CommentTexts(branch));
    }

    [Fact]
    public void LeadingWhitespaceInComments_IsPreserved()
    {
        var comments = Load("a: 1\n#   indented text\n").Select(i => i.Key).OfType<Comment>().ToList();

        Assert.Equal("   indented text", Assert.Single(comments).Value);
    }
}
