using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Nops.Core.Tests.Stores;

public class TreeWalkerTests
{
    private static Nops.Core.Stores.SopsMetadata CreateMetadata(
        string? unencryptedSuffix = null,
        string? encryptedSuffix = null,
        string? unencryptedRegex = null,
        string? encryptedRegex = null,
        string? unencryptedCommentRegex = null,
        string? encryptedCommentRegex = null)
    {
        return new Nops.Core.Stores.SopsMetadata(
            null, Array.Empty<Nops.Core.Stores.AgeEntry>(),
            Array.Empty<Nops.Core.Stores.PgpEntry>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(),
            0, unencryptedSuffix, encryptedSuffix,
            unencryptedRegex, encryptedRegex,
            unencryptedCommentRegex, encryptedCommentRegex, false, "3.10.2", null);
    }

    [Fact]
    public void AdditionalData_SimpleKey()
    {
        var path = new List<string> { "myapp1" };
        var result = Nops.Core.Stores.TreeWalker.AdditionalData(path);
        Assert.Equal("myapp1:", result);
    }

    [Fact]
    public void AdditionalData_NestedKeys()
    {
        var path = new List<string> { "app2", "db", "user" };
        var result = Nops.Core.Stores.TreeWalker.AdditionalData(path);
        Assert.Equal("app2:db:user:", result);
    }

    [Fact]
    public void AdditionalData_DeeplyNested()
    {
        var path = new List<string> { "this", "is", "a", "nested", "value" };
        var result = Nops.Core.Stores.TreeWalker.AdditionalData(path);
        Assert.Equal("this:is:a:nested:value:", result);
    }

    [Fact]
    public void AdditionalData_EmptyPath()
    {
        var path = new List<string>();
        var result = Nops.Core.Stores.TreeWalker.AdditionalData(path);
        Assert.Equal(":", result);
    }

    [Fact]
    public void ShouldBeEncrypted_Default_AllKeysEncrypted()
    {
        var metadata = CreateMetadata();
        var path = new List<string> { "secret" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldBeEncrypted_UnencryptedSuffix_Matches()
    {
        var metadata = CreateMetadata(unencryptedSuffix: "_unencrypted");
        var path = new List<string> { "key_unencrypted" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldBeEncrypted_UnencryptedSuffix_DoesNotMatch()
    {
        var metadata = CreateMetadata(unencryptedSuffix: "_unencrypted");
        var path = new List<string> { "secret_key" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldBeEncrypted_UnencryptedSuffix_NestedKey()
    {
        var metadata = CreateMetadata(unencryptedSuffix: "_unencrypted");
        var path = new List<string> { "app", "config_unencrypted" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.False(result); // Any element with suffix → skip
    }

    [Fact]
    public void ShouldBeEncrypted_EncryptedSuffix_Matches()
    {
        var metadata = CreateMetadata(encryptedSuffix: "_encrypted");
        var path = new List<string> { "key_encrypted" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.True(result); // Has encrypted_suffix → encrypt
    }

    [Fact]
    public void ShouldBeEncrypted_EncryptedSuffix_DoesNotMatch()
    {
        var metadata = CreateMetadata(encryptedSuffix: "_encrypted");
        var path = new List<string> { "public_key" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.False(result); // No encrypted_suffix → skip (default when encrypted_suffix is set)
    }

    [Fact]
    public void ShouldBeEncrypted_UnencryptedRegex_Matches()
    {
        var metadata = CreateMetadata(unencryptedRegex: "^public_");
        var path = new List<string> { "public_key" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldBeEncrypted_EncryptedRegex_Matches()
    {
        var metadata = CreateMetadata(encryptedRegex: "^secret_");
        var path = new List<string> { "secret_key" };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldBeEncrypted_CommentNode_ShouldBeProcessed()
    {
        var metadata = CreateMetadata();
        var path = new List<string> { "key" };

        // Comment nodes should be walked (they can contain ENC[...,type:comment])
        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, Array.Empty<IReadOnlyList<string>>(), true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldBeEncrypted_EncryptedCommentRegex_SkipsComment()
    {
        var metadata = CreateMetadata(encryptedCommentRegex: "^#sops");
        var path = new List<string> { "key" };
        var commentsStack = new[] { new[] { "#sops comment" } };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, commentsStack, true);

        Assert.False(result); // encrypted_comment_regex matches → skip the comment line itself
    }

    [Fact]
    public void ShouldBeEncrypted_UnencryptedCommentRegex_SkipsComment()
    {
        var metadata = CreateMetadata(unencryptedCommentRegex: "^#public");
        var path = new List<string> { "key" };
        var commentsStack = new[] { new[] { "#public data" } };

        var result = Nops.Core.Stores.TreeWalker.ShouldBeEncrypted(metadata, path, commentsStack, true);

        Assert.False(result);
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== TREE WALKER ===");
        Console.WriteLine("AdditionalData: Join(path, ':') + ':'");
        Console.WriteLine("Arrays share parent AAD (no index in path)");
        Console.WriteLine("shouldBeEncrypted: checks suffix/regex on each path element");
        Console.WriteLine("====================");

        Assert.True(true, "Spike complete - see console output");
    }
}
