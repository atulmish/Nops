using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nops.Core.Crypto;
using Nops.Core.Keys;
using Nops.Core.Stores;

namespace Nops.Core;

/// <summary>
/// Options for SOPS file decryption. An options bag (not positional arguments).
/// </summary>
public sealed class SopsOptions
{
    /// <summary>
    /// Path of the file being decrypted (used for diagnostics).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Age private key identities (Bech32 "AGE-SECRET-KEY-..." strings or file paths).
    /// </summary>
    public IReadOnlyList<string>? AgeIdentities { get; set; }

    /// <summary>
    /// PGP private keys as (key bytes, optional passphrase) tuples.
    /// </summary>
    public IReadOnlyList<(byte[] Key, string? Passphrase)>? PgpKeys { get; set; }

    /// <summary>
    /// Throw a <see cref="MacMismatchException"/> when the MAC does not match.
    /// </summary>
    public bool ThrowOnMacMismatch { get; set; } = true;

    /// <summary>
    /// Explicit master key providers (age, PGP, AWS KMS, GCP KMS, Azure KV, Vault).
    /// </summary>
    public IReadOnlyList<IMasterKeyProvider>? MasterKeys { get; set; }

    /// <summary>
    /// Also use the age identities the sops CLI would find: <c>SOPS_AGE_KEY</c>,
    /// <c>SOPS_AGE_KEY_FILE</c> and the default <c>sops/age/keys.txt</c> in the user
    /// config directory. Default <c>true</c>; set <c>false</c> to use only explicit keys.
    /// </summary>
    public bool UseSopsEnvironment { get; set; } = true;
}

/// <summary>
/// Main entry point for SOPS file decryption.
/// Loads, decrypts, and returns ordered trees, flattened key-values, or typed objects.
/// </summary>
public static class SopsFile
{
    /// <summary>
    /// Decrypt a SOPS file and return the ordered tree.
    /// MAC is verified before returning.
    /// </summary>
    public static async Task<SopsTree> DecryptTreeAsync(
        byte[] fileBytes,
        string fileKind, // "yaml", "json", "dotenv", "ini", "binary"
        SopsOptions options,
        CancellationToken ct = default)
    {
        // 1. Detect store and load into an ordered tree.
        var tree = DetectStore(fileKind).LoadEncrypted(fileBytes, options.FilePath ?? "");

        // 2. Resolve the 32-byte data key from the master keys.
        var providers = BuildKeyProviders(options);
        var dataKey = await DataKeyResolver.ResolveAsync(tree.Metadata, providers, ct);

        // 3. Walk the tree in order, decrypting values and computing the MAC
        //    over the decrypted plaintext (exact port of Tree.Decrypt).
        var computedMac = DecryptAndComputeMac(tree, dataKey);

        // 4. Verify the computed MAC against the stored MAC.
        MacVerifier.VerifyMac(tree, dataKey, computedMac, options.ThrowOnMacMismatch);

        return tree;
    }

    /// <summary>
    /// Decrypt a SOPS file and return flattened key-value pairs.
    /// Keys are ":"-joined paths (e.g., "app.db.user"); arrays use ":index" (e.g., "list.0.x").
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> DecryptToDictionaryAsync(
        byte[] fileBytes,
        string fileKind,
        SopsOptions options,
        CancellationToken ct = default)
    {
        var tree = await DecryptTreeAsync(fileBytes, fileKind, options, ct);

        var dict = new Dictionary<string, string>();
        FlattenTree(tree.Branches[0], new List<string>(), dict);
        return dict;
    }

    /// <summary>
    /// Decrypt a SOPS file and deserialize to a typed object.
    /// Types are preserved (int, float, bool, bytes as base64, time as ISO-8601).
    /// </summary>
    public static async Task<T> DecryptAsync<T>(
        byte[] fileBytes,
        string fileKind,
        SopsOptions options,
        CancellationToken ct = default)
    {
        var tree = await DecryptTreeAsync(fileBytes, fileKind, options, ct);

        // Convert the ordered tree into a plain CLR object graph (comments dropped)
        // and let System.Text.Json perform the typed bind.
        var plain = TreeConverter.ToPlainObject(tree.Branches[0]);
        var json = JsonSerializer.Serialize(plain, TypedJsonOptions);
        return JsonSerializer.Deserialize<T>(json, TypedJsonOptions)!;
    }

    // YAML can hold .nan/.inf, which System.Text.Json rejects unless named literals are allowed.
    private static readonly JsonSerializerOptions TypedJsonOptions = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static ISopsStore DetectStore(string fileKind)
    {
        return fileKind.ToLowerInvariant() switch
        {
            "yaml" => new YamlStoreWrapper(),
            "json" => new JsonStoreWrapper(),
            "dotenv" => new DotenvStoreWrapper(),
            "ini" => new IniStoreWrapper(),
            "binary" => new BinaryStoreWrapper(),
            _ => throw new InvalidOperationException($"Unknown file kind: {fileKind}")
        };
    }

    private static IReadOnlyList<IMasterKeyProvider> BuildKeyProviders(SopsOptions options)
    {
        var providers = new List<IMasterKeyProvider>();

        if (options.MasterKeys != null)
            providers.AddRange(options.MasterKeys);

        // Age identities: Bech32 private keys or file paths containing secret keys,
        // then the sops CLI's environment sources. Each distinct key is tried once.
        var ageKeys = new HashSet<string>(StringComparer.Ordinal);
        if (options.AgeIdentities != null)
        {
            foreach (var identity in options.AgeIdentities)
                ageKeys.UnionWith(AgeIdentityLoader.Load(identity));
        }

        if (options.UseSopsEnvironment)
            ageKeys.UnionWith(AgeIdentityLoader.LoadFromSopsEnvironment());

        foreach (var key in ageKeys)
            providers.Add(new AgeMasterKey(recipient: null, privateKey: key));

        // PGP private keys.
        if (options.PgpKeys != null)
        {
            foreach (var (key, passphrase) in options.PgpKeys)
                providers.Add(new PgpMasterKey(fingerprint: "", key, passphrase));
        }

        return providers;
    }

    /// <summary>
    /// Walk every branch in document order, decrypting values in place and
    /// feeding the SHA-512 MAC over the decrypted plaintext. Exact port of
    /// Tree.Decrypt (walkBranch/walkSlice/walkValue) from sops.go.
    /// Returns the computed MAC as uppercase hex.
    /// </summary>
    private static string DecryptAndComputeMac(SopsTree tree, byte[] dataKey)
    {
        using var hash = SHA512.Create();

        if (tree.Metadata.MacOnlyEncrypted)
        {
            var prefix = MacVerifier.GetMacOnlyEncryptedPrefix();
            hash.TransformBlock(prefix, 0, prefix.Length, null, 0);
        }

        var stack = new List<List<string>>();
        foreach (var branch in tree.Branches)
            WalkBranch(branch, new List<string>(), stack, tree.Metadata, dataKey, hash);

        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(hash.Hash!).ToUpperInvariant();
    }

    private static void WalkBranch(
        TreeBranch branch,
        List<string> path,
        List<List<string>> stack,
        SopsMetadata metadata,
        byte[] dataKey,
        SHA512 hash)
    {
        // Each branch level gets its own comments frame.
        stack.Add(new List<string>());

        for (int i = 0; i < branch.Count; i++)
        {
            var item = branch[i];

            // Comment keys are visited as leaves (path is not extended) and
            // their values are left untouched.
            if (item.Key is Comment keyComment)
            {
                stack[^1].Add(keyComment.Value);
                var newKey = VisitLeaf(item.Key, path, stack, metadata, dataKey, hash);
                if (newKey is Comment nc)
                    branch[i] = new TreeItem(nc, item.Value);
                else if (newKey is string ns)
                    branch[i] = new TreeItem(new Comment(ns, keyComment.Inline), item.Value);
                else
                    throw new InvalidOperationException(
                        $"walkValue of Comment should be either Comment or string, was {newKey?.GetType().Name ?? "null"}.");
                continue;
            }

            if (item.Key is not string key)
            {
                throw new InvalidOperationException(
                    $"Tree contains a non-string key (type {item.Key?.GetType().Name ?? "null"}). Only string keys are supported.");
            }

            var valueIsComment = item.Value is Comment;
            if (valueIsComment)
                stack[^1].Add(((Comment)item.Value!).Value);

            var newPath = new List<string>(path) { key };

            object? newValue;
            if (item.Value is TreeBranch sub)
            {
                if (sub.IsSequence)
                    WalkSlice(sub, newPath, stack, metadata, dataKey, hash);
                else
                    WalkBranch(sub, newPath, stack, metadata, dataKey, hash);
                newValue = sub;
            }
            else if (item.Value is null)
            {
                // nil values are never visited, encrypted, or hashed.
                newValue = null;
            }
            else
            {
                newValue = VisitLeaf(item.Value, newPath, stack, metadata, dataKey, hash);
            }

            branch[i] = new TreeItem(item.Key, newValue);

            if (!valueIsComment)
                stack[^1].Clear();
        }

        stack.RemoveAt(stack.Count - 1);
    }

    private static void WalkSlice(
        TreeBranch sequence,
        List<string> path,
        List<List<string>> stack,
        SopsMetadata metadata,
        byte[] dataKey,
        SHA512 hash)
    {
        // Sequence elements share their parent's path (no index in AAD).
        stack.Add(new List<string>());

        for (int i = 0; i < sequence.Count; i++)
        {
            var item = sequence[i];

            var valueIsComment = item.Value is Comment;
            if (valueIsComment)
                stack[^1].Add(((Comment)item.Value!).Value);

            object? newValue;
            if (item.Value is TreeBranch sub)
            {
                if (sub.IsSequence)
                    WalkSlice(sub, path, stack, metadata, dataKey, hash);
                else
                    WalkBranch(sub, path, stack, metadata, dataKey, hash);
                newValue = sub;
            }
            else if (item.Value is null)
            {
                newValue = null;
            }
            else
            {
                newValue = VisitLeaf(item.Value, path, stack, metadata, dataKey, hash);
            }

            sequence[i] = new TreeItem(item.Key, newValue);

            // The active comments are cleared unless the element was a comment
            // (matches sops walkSlice: only the input type matters).
            if (!valueIsComment)
                stack[^1].Clear();
        }

        stack.RemoveAt(stack.Count - 1);
    }

    private static object? VisitLeaf(
        object? value,
        List<string> path,
        List<List<string>> stack,
        SopsMetadata metadata,
        byte[] dataKey,
        SHA512 hash)
    {
        var isComment = value is Comment;
        // List<List<string>> is already an IReadOnlyList<IReadOnlyList<string>> (covariance).
        var encrypted = TreeWalker.ShouldBeEncrypted(metadata, path, stack, isComment);

        object? decrypted;
        if (encrypted)
        {
            if (isComment)
            {
                var text = ((Comment)value!).Value;
                decrypted = value;
                if (EncryptedValueParser.TryParse(text, out _))
                {
                    try
                    {
                        var attempt = AesGcmDecryptor.DecryptValue(text, dataKey, TreeWalker.AdditionalData(path));
                        if (attempt != null)
                            decrypted = attempt;
                    }
                    catch
                    {
                        // Assume the comment was not encrypted in the first place; keep it.
                    }
                }
            }
            else if (value is string s)
            {
                if (s.Length == 0)
                {
                    // Empty values are never encrypted (isEmpty); keep as-is.
                    decrypted = s;
                }
                else
                {
                    if (!EncryptedValueParser.TryParse(s, out _))
                    {
                        throw new InvalidOperationException(
                            $"Expected encrypted value as string, but got plaintext value at '{string.Join(":", path)}'.");
                    }
                    decrypted = AesGcmDecryptor.DecryptValue(s, dataKey, TreeWalker.AdditionalData(path));
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Expected encrypted value as string, but got {value?.GetType().Name ?? "null"} at '{string.Join(":", path)}'.");
            }
        }
        else
        {
            decrypted = value;
        }

        // In normal mode every value contributes to the MAC; in mac-only-encrypted
        // mode only values that ended up encrypted do. Comments never contribute.
        if (!metadata.MacOnlyEncrypted || encrypted)
        {
            if (decrypted is not null && decrypted is not Comment)
            {
                var hashBytes = ToBytes.Convert(decrypted);
                if (hashBytes.Length > 0)
                    hash.TransformBlock(hashBytes, 0, hashBytes.Length, null, 0);
            }
        }

        return decrypted;
    }

    private static void FlattenTree(TreeBranch branch, List<string> path, Dictionary<string, string> dict)
    {
        foreach (var item in branch)
        {
            var newPath = new List<string>(path);

            if (item.Key is string key)
                newPath.Add(key);
            else if (item.Key is int index)
                newPath.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (item.Value is TreeBranch subBranch)
            {
                FlattenTree(subBranch, newPath, dict);
            }
            else if (item.Value is string s)
            {
                dict[string.Join(":", newPath)] = s;
            }
            else if (item.Value is int i)
            {
                dict[string.Join(":", newPath)] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (item.Value is long l)
            {
                dict[string.Join(":", newPath)] = l.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (item.Value is double d)
            {
                dict[string.Join(":", newPath)] = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (item.Value is bool b)
            {
                dict[string.Join(":", newPath)] = b ? "True" : "False";
            }
            else if (item.Value is byte[] bytes)
            {
                dict[string.Join(":", newPath)] = Convert.ToBase64String(bytes);
            }
            else if (item.Value is DateTime dt)
            {
                dict[string.Join(":", newPath)] = dt.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (item.Value is DateTimeOffset dto)
            {
                dict[string.Join(":", newPath)] = ToBytes.FormatTimestamp(dto);
            }
        }
    }
}

/// <summary>
/// Interface for stores that can load encrypted files.
/// </summary>
public interface ISopsStore
{
    SopsTree LoadEncrypted(byte[] fileBytes, string filePath);
}

// Wrapper classes to implement ISopsStore
public class YamlStoreWrapper : ISopsStore { public SopsTree LoadEncrypted(byte[] fileBytes, string filePath) => YamlStore.LoadEncrypted(fileBytes, filePath); }
public class JsonStoreWrapper : ISopsStore { public SopsTree LoadEncrypted(byte[] fileBytes, string filePath) => JsonStore.LoadEncrypted(fileBytes, filePath); }
public class DotenvStoreWrapper : ISopsStore { public SopsTree LoadEncrypted(byte[] fileBytes, string filePath) => DotenvStore.LoadEncrypted(fileBytes, filePath); }
public class IniStoreWrapper : ISopsStore { public SopsTree LoadEncrypted(byte[] fileBytes, string filePath) => IniStore.LoadEncrypted(fileBytes, filePath); }
public class BinaryStoreWrapper : ISopsStore { public SopsTree LoadEncrypted(byte[] fileBytes, string filePath) => BinaryStore.LoadEncrypted(fileBytes, filePath); }
