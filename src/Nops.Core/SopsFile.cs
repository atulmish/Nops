using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nops.Core.Crypto;
using Nops.Core.Keys;
using Nops.Core.Stores;

namespace Nops.Core;

/// <summary>
/// Options for SOPS file decryption.
/// </summary>
public sealed record SopsOptions(
    string? FilePath = null,
    IReadOnlyList<string>? AgeIdentities = null,
    IReadOnlyList<(byte[] Key, string? Passphrase)>? PgpKeys = null,
    bool ThrowOnMacMismatch = true,
    IReadOnlyList<IMasterKeyProvider>? MasterKeys = null);

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
        // 1. Detect store and load into tree
        var tree = DetectStore(fileKind).LoadEncrypted(fileBytes, options.FilePath ?? "");

        // 2. Resolve data key from master keys
        var providers = BuildKeyProviders(options);
        var dataKey = await DataKeyResolver.ResolveAsync(tree.Metadata, providers, ct);

        // 3. Verify MAC before returning decrypted data
        MacVerifier.VerifyMac(tree, dataKey);

        // 4. Decrypt all ENC[...] values in the tree
        return DecryptTreeValues(tree, dataKey, ct);
    }

    /// <summary>
    /// Decrypt a SOPS file and return flattened key-value pairs.
    /// Keys are ":"-joined paths (e.g., "app.db.user").
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
    /// </summary>
    public static async Task<T> DecryptAsync<T>(
        byte[] fileBytes,
        string fileKind,
        SopsOptions options,
        CancellationToken ct = default)
    {
        var tree = await DecryptTreeAsync(fileBytes, fileKind, options, ct);

        // Use System.Text.Json to deserialize from the flattened dictionary
        var dict = await DecryptToDictionaryAsync(fileBytes, fileKind, options, ct);
        var json = System.Text.Json.JsonSerializer.Serialize(dict);

        return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
    }

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

        // Add explicitly supplied master key providers
        if (options.MasterKeys != null)
        {
            providers.AddRange(options.MasterKeys);
        }

        // Add age key providers
        if (options.AgeIdentities != null)
        {
            foreach (var identity in options.AgeIdentities)
            {
                // Try to parse as bech32 private key or file path
                if (identity.StartsWith("AGE-SECRET-KEY-"))
                {
                    providers.Add(new AgeMasterKey("", identity));
                }
                else
                {
                    // Load from file
                    try
                    {
                        var content = File.ReadAllText(identity);
                        providers.Add(new AgeMasterKey("", content.Trim()));
                    }
                    catch
                    {
                        // Ignore invalid paths
                    }
                }
            }
        }

        // Add PGP key providers
        if (options.PgpKeys != null)
        {
            foreach (var (key, passphrase) in options.PgpKeys)
            {
                providers.Add(new PgpMasterKey("", key, passphrase));
            }
        }

        return providers;
    }

    private static SopsTree DecryptTreeValues(SopsTree tree, byte[] dataKey, CancellationToken ct)
    {
        // Walk tree and decrypt all ENC[...] values
        foreach (var branch in tree.Branches)
        {
            DecryptBranch(branch, dataKey);
        }
        return tree;
    }

    private static void DecryptBranch(TreeBranch branch, byte[] dataKey)
    {
        for (int i = 0; i < branch.Count; i++)
        {
            var item = branch[i];

            if (item.Value is string encValue && EncryptedValueParser.TryParse(encValue, out var ev))
            {
                var aad = TreeWalker.AdditionalData(GetPath(branch, i));
                var decrypted = AesGcmDecryptor.DecryptValue(encValue, dataKey, aad);
                branch[i] = new TreeItem(item.Key, decrypted);
            }
            else if (item.Value is TreeBranch subBranch)
            {
                DecryptBranch(subBranch, dataKey);
            }
        }
    }

    private static List<string> GetPath(TreeBranch branch, int index)
    {
        var path = new List<string>();
        // In MVP, paths are simplified - just use the key name
        if (index < branch.Count && branch[index].Key is string key)
            path.Add(key);
        return path;
    }

    private static void FlattenTree(TreeBranch branch, List<string> currentPath, Dictionary<string, string> dict)
    {
        foreach (var item in branch)
        {
            var newPath = new List<string>(currentPath);

            if (item.Key is string key)
                newPath.Add(key);

            if (item.Value is string value)
            {
                var flatKey = string.Join(":", newPath);
                dict[flatKey] = value;
            }
            else if (item.Value is TreeBranch subBranch)
            {
                FlattenTree(subBranch, newPath, dict);
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
