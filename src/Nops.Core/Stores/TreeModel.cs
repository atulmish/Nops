using System;
using System.Collections.Generic;

namespace Nops.Core.Stores;

/// <summary>
/// Represents a YAML/Dotenv comment node in the tree.
/// </summary>
public sealed record Comment(string Value, bool Inline = false);

/// <summary>
/// A single key-value item in the SOPS tree. Key is either a string, an int (array
/// index) or a Comment.
/// </summary>
public sealed record TreeItem(object Key, object? Value);

/// <summary>
/// An ordered branch of TreeItems. Preserves insertion order for MAC/AAD computation.
/// A branch may represent a mapping (keys are strings) or a sequence/array (keys are
/// int indices, indicated by <see cref="IsSequence"/>). Sequences do NOT contribute
/// their index to the SOPS AAD/MAC path.
/// </summary>
public sealed class TreeBranch : List<TreeItem>
{
    /// <summary>
    /// True when this branch represents an array/sequence (keys are int indices).
    /// </summary>
    public bool IsSequence { get; set; }

    public TreeBranch() { }

    public TreeBranch(bool isSequence) => IsSequence = isSequence;
}

/// <summary>
/// Entry for age master key in .sops metadata.
/// </summary>
public sealed record AgeEntry(string Recipient, string Enc);

/// <summary>
/// Entry for PGP master key in .sops metadata.
/// </summary>
public sealed record PgpEntry(string Fp, string Enc, DateTimeOffset? CreatedAt);

/// <summary>
/// A single master-key reference inside a key group: the provider key type
/// (e.g. "age", "pgp", "aws_kms") and the encrypted data-key share.
/// </summary>
public sealed record KeyGroupEntry(string KeyType, string Enc);

/// <summary>
/// A SOPS key group. With Shamir secret sharing there is one encrypted share per
/// group (see <see cref="SopsMetadata.ShamirThreshold"/>); otherwise a single group
/// has the whole data key encrypted under every key it contains.
/// </summary>
public sealed record KeyGroup(IReadOnlyList<KeyGroupEntry> Keys);

/// <summary>
/// SOPS metadata extracted from the .sops top-level key in YAML/JSON files.
/// Fields map directly to the .sops dict keys (case-exact).
/// </summary>
public sealed record SopsMetadata(
    string? Mac,
    IReadOnlyList<AgeEntry> Age,
    IReadOnlyList<PgpEntry> Pgp,
    IReadOnlyList<object> Kms,
    IReadOnlyList<object> GcpKms,
    IReadOnlyList<object> AzureKv,
    IReadOnlyList<object> Hckms,
    IReadOnlyList<object> HcVault,
    int ShamirThreshold,
    string? UnencryptedSuffix,
    string? EncryptedSuffix,
    string? UnencryptedRegex,
    string? EncryptedRegex,
    string? UnencryptedCommentRegex,
    string? EncryptedCommentRegex,
    bool MacOnlyEncrypted,
    string? Version,
    DateTimeOffset? LastModified,
    IReadOnlyList<KeyGroup>? KeyGroups = null);

/// <summary>
/// Represents a parsed and/or decrypted SOPS file as an ordered tree.
/// </summary>
public sealed record SopsTree(
    IReadOnlyList<TreeBranch> Branches,
    SopsMetadata Metadata,
    string FilePath);
