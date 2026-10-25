using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Nops.Core.Tests.Stores;

public class TreeModelTests
{
    [Fact]
    public void Comment_RecordsCorrectly()
    {
        var comment = new Nops.Core.Comment("test comment");
        Assert.Equal("test comment", comment.Value);
        Assert.False(comment.Inline);
    }

    [Fact]
    public void Comment_InlinePropertyWorks()
    {
        var comment = new Nops.Core.Comment("inline", Inline: true);
        Assert.True(comment.Inline);
    }

    [Fact]
    public void TreeItem_CreatesWithStringKey()
    {
        var item = new Nops.Core.TreeItem("key", "value");
        Assert.Equal("key", item.Key);
        Assert.Equal("value", item.Value);
    }

    [Fact]
    public void TreeItem_AllowsNullValue()
    {
        var item = new Nops.Core.TreeItem("key", null);
        Assert.Equal("key", item.Key);
        Assert.Null(item.Value);
    }

    [Fact]
    public void TreeItem_AllowsCommentKey()
    {
        var comment = new Nops.Core.Comment("# test");
        var item = new Nops.Core.TreeItem(comment, "value");
        Assert.Same(comment, item.Key);
        Assert.Equal("value", item.Value);
    }

    [Fact]
    public void TreeBranch_PreservesInsertionOrder()
    {
        var branch = new Nops.Core.TreeBranch();
        branch.Add(new Nops.Core.TreeItem("z_key", "1"));
        branch.Add(new Nops.Core.TreeItem("a_key", "2"));
        branch.Add(new Nops.Core.TreeItem("m_key", "3"));

        // Order must be preserved (not alphabetical)
        Assert.Equal(3, branch.Count);
        Assert.Equal("z_key", ((string)((Nops.Core.TreeItem)branch[0]).Key!));
        Assert.Equal("a_key", ((string)((Nops.Core.TreeItem)branch[1]).Key!));
        Assert.Equal("m_key", ((string)((Nops.Core.TreeItem)branch[2]).Key!));
    }

    [Fact]
    public void TreeBranch_ContainsCommentItems()
    {
        var branch = new Nops.Core.TreeBranch();
        var comment1 = new Nops.Core.Comment("# first");
        var comment2 = new Nops.Core.Comment("# second");

        branch.Add(new Nops.Core.TreeItem(comment1, null));
        branch.Add(new Nops.Core.TreeItem("key", "value"));
        branch.Add(new Nops.Core.TreeItem(comment2, null));

        Assert.Equal(3, branch.Count);
        Assert.IsType<Nops.Core.Comment>(branch[0].Key);
        Assert.IsType<string>(branch[1].Key);
        Assert.IsType<Nops.Core.Comment>(branch[2].Key);
    }

    [Fact]
    public void SopsTree_CreatesWithBranchesAndMetadata()
    {
        var branches = new List<Nops.Core.TreeBranch>
        {
            new Nops.Core.TreeBranch
            {
                new Nops.Core.TreeItem("key1", "value1")
            }
        };

        var metadata = new Nops.Core.SopsMetadata(
            null, Array.Empty<Nops.Core.AgeEntry>(),
            Array.Empty<Nops.Core.PgpEntry>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, null, null, null, null, null, null, false, "3.10.2", null);

        var tree = new Nops.Core.SopsTree(branches, metadata, "/test/file.yaml");

        Assert.Single(tree.Branches);
        Assert.Equal("/test/file.yaml", tree.FilePath);
    }

    [Fact]
    public void TreeBranch_EmptyByDefault()
    {
        var branch = new Nops.Core.TreeBranch();
        Assert.Empty(branch);
    }

    [Fact]
    public void SopsTree_MultipleBranchesPreserved()
    {
        var branches = new List<Nops.Core.TreeBranch>
        {
            new Nops.Core.TreeBranch { new Nops.Core.TreeItem("a", "1") },
            new Nops.Core.TreeBranch { new Nops.Core.TreeItem("b", "2") },
            new Nops.Core.TreeBranch { new Nops.Core.TreeItem("c", "3") }
        };

        var metadata = new Nops.Core.SopsMetadata(
            null, Array.Empty<Nops.Core.AgeEntry>(),
            Array.Empty<Nops.Core.PgpEntry>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, null, null, null, null, null, null, false, "3.10.2", null);

        var tree = new Nops.Core.SopsTree(branches, metadata, "/test.yaml");

        Assert.Equal(3, tree.Branches.Count);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== TREE MODEL ===");
        Console.WriteLine("Comment: sealed record with Value + Inline");
        Console.WriteLine("TreeItem: sealed record with Key (string|Comment) + Value?");
        Console.WriteLine("TreeBranch: sealed class extending List<TreeItem>");
        Console.WriteLine("SopsTree: sealed record with Branches, Metadata, FilePath");
        Console.WriteLine("No Dictionary<string,object> in Core until after MAC verify");
        Console.WriteLine("==================");

        Assert.True(true, "Spike complete - see console output");
    }
}
