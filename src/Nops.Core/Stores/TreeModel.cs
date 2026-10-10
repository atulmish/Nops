using System;
using System.Collections.Generic;

namespace Nops.Core.Stores;

/// <summary>
/// Represents a YAML/Dotenv comment node in the tree.
/// </summary>
public sealed record Comment(string Value, bool Inline = false);

/// <summary>
/// A single key-value item in the SOPS tree. Key is either a string or Comment.
/// </summary>
public sealed record TreeItem(object Key, object? Value);

/// <summary>
/// An ordered branch (list) of TreeItems. Preserves insertion order for MAC/AAD computation.
/// </summary>
public sealed class TreeBranch : List<TreeItem>;

/// <summary>
/// Entry for age master key in .sops metadata.
/// </summary>
public sealed record AgeEntry(string Recipient, string Enc);

/// <summary>
/// Entry for PGP master key in .sops metadata.
/// </summary>
public sealed record PgpEntry(string Fp, string Enc, DateTimeOffset? CreatedAt);

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
    DateTimeOffset? LastModified);

/// <summary>
/// Represents a parsed and/or decrypted SOPS file as an ordered tree.
/// </summary>
public sealed record SopsTree(
    IReadOnlyList<TreeBranch> Branches,
    SopsMetadata Metadata,
    string FilePath);
