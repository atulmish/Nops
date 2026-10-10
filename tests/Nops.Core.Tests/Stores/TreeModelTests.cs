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
        var comment = new Nops.Core.Stores.Comment("test comment");
        Assert.Equal("test comment", comment.Value);
        Assert.False(comment.Inline);
    }

    [Fact]
    public void Comment_InlinePropertyWorks()
    {
        var comment = new Nops.Core.Stores.Comment("inline", Inline: true);
        Assert.True(comment.Inline);
    }

    [Fact]
    public void TreeItem_CreatesWithStringKey()
    {
        var item = new Nops.Core.Stores.TreeItem("key", "value");
        Assert.Equal("key", item.Key);
        Assert.Equal("value", item.Value);
    }

    [Fact]
    public void TreeItem_AllowsNullValue()
    {
        var item = new Nops.Core.Stores.TreeItem("key", null);
        Assert.Equal("key", item.Key);
        Assert.Null(item.Value);
    }

    [Fact]
    public void TreeItem_AllowsCommentKey()
    {
        var comment = new Nops.Core.Stores.Comment("# test");
        var item = new Nops.Core.Stores.TreeItem(comment, "value");
        Assert.Same(comment, item.Key);
        Assert.Equal("value", item.Value);
    }

    [Fact]
    public void TreeBranch_PreservesInsertionOrder()
    {
        var branch = new Nops.Core.Stores.TreeBranch();
        branch.Add(new Nops.Core.Stores.TreeItem("z_key", "1"));
        branch.Add(new Nops.Core.Stores.TreeItem("a_key", "2"));
        branch.Add(new Nops.Core.Stores.TreeItem("m_key", "3"));

        // Order must be preserved (not alphabetical)
        Assert.Equal(3, branch.Count);
        Assert.Equal("z_key", ((string)((Nops.Core.Stores.TreeItem)branch[0]).Key!));
        Assert.Equal("a_key", ((string)((Nops.Core.Stores.TreeItem)branch[1]).Key!));
        Assert.Equal("m_key", ((string)((Nops.Core.Stores.TreeItem)branch[2]).Key!));
    }

    [Fact]
    public void TreeBranch_ContainsCommentItems()
    {
        var branch = new Nops.Core.Stores.TreeBranch();
        var comment1 = new Nops.Core.Stores.Comment("# first");
        var comment2 = new Nops.Core.Stores.Comment("# second");

        branch.Add(new Nops.Core.Stores.TreeItem(comment1, null));
        branch.Add(new Nops.Core.Stores.TreeItem("key", "value"));
        branch.Add(new Nops.Core.Stores.TreeItem(comment2, null));

        Assert.Equal(3, branch.Count);
        Assert.IsType<Nops.Core.Stores.Comment>(branch[0].Key);
        Assert.IsType<string>(branch[1].Key);
        Assert.IsType<Nops.Core.Stores.Comment>(branch[2].Key);
    }

    [Fact]
    public void SopsTree_CreatesWithBranchesAndMetadata()
    {
        var branches = new List<Nops.Core.Stores.TreeBranch>
        {
            new Nops.Core.Stores.TreeBranch
            {
                new Nops.Core.Stores.TreeItem("key1", "value1")
            }
        };

        var metadata = new Nops.Core.Stores.SopsMetadata(
            null, Array.Empty<Nops.Core.Stores.AgeEntry>(),
            Array.Empty<Nops.Core.Stores.PgpEntry>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, null, null, null, null, null, null, false, "3.10.2", null);

        var tree = new Nops.Core.Stores.SopsTree(branches, metadata, "/test/file.yaml");

        Assert.Single(tree.Branches);
        Assert.Equal("/test/file.yaml", tree.FilePath);
    }

    [Fact]
    public void TreeBranch_EmptyByDefault()
    {
        var branch = new Nops.Core.Stores.TreeBranch();
        Assert.Empty(branch);
    }

    [Fact]
    public void SopsTree_MultipleBranchesPreserved()
    {
        var branches = new List<Nops.Core.Stores.TreeBranch>
        {
            new Nops.Core.Stores.TreeBranch { new Nops.Core.Stores.TreeItem("a", "1") },
            new Nops.Core.Stores.TreeBranch { new Nops.Core.Stores.TreeItem("b", "2") },
            new Nops.Core.Stores.TreeBranch { new Nops.Core.Stores.TreeItem("c", "3") }
        };

        var metadata = new Nops.Core.Stores.SopsMetadata(
            null, Array.Empty<Nops.Core.Stores.AgeEntry>(),
            Array.Empty<Nops.Core.Stores.PgpEntry>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, null, null, null, null, null, null, false, "3.10.2", null);

        var tree = new Nops.Core.Stores.SopsTree(branches, metadata, "/test.yaml");

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
