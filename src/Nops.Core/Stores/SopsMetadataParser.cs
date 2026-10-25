using System;
using System.Collections.Generic;
using System.Linq;

namespace Nops.Core.Stores;

/// <summary>
/// Parser for SOPS .sops metadata extracted from YAML/JSON files.
/// Handles both the flat key lists (age/pgp/kms/...) and the nested
/// <c>key_groups</c> form used for Shamir secret sharing.
/// </summary>
internal static class SopsMetadataParser
{
    /// <summary>
    /// Default suffix that marks a value as unencrypted. Applied when the file
    /// declares no encryption rule at all (matches sops.stores.Metadata.ToInternal).
    /// </summary>
    public const string DefaultUnencryptedSuffix = "_unencrypted";

    /// <summary>
    /// Return an empty/default SopsMetadata (used when no .sops key is present).
    /// </summary>
    public static SopsMetadata Empty() => new(
        null, Array.Empty<AgeEntry>(), Array.Empty<PgpEntry>(),
        Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
        Array.Empty<object>(), Array.Empty<object>(),
        0, null, null, null, null, null, null, false, null, null,
        Array.Empty<KeyGroup>());

    /// <summary>
    /// Parse a flat dictionary (from .sops key) into SopsMetadata.
    /// Unknown keys are ignored for forward compatibility.
    /// </summary>
    public static SopsMetadata Parse(Dictionary<string, object> dict)
    {
        var age = ExtractAgeEntries(dict);
        var pgp = ExtractPgpEntries(dict);

        // shamir_threshold == 0 means "use number of groups" (computed later).
        var shamirThreshold = ExtractInt(dict, "shamir_threshold", 0);

        var (unencryptedSuffix, encryptedSuffix, unencryptedRegex, encryptedRegex,
            unencryptedCommentRegex, encryptedCommentRegex) = ExtractCryptRules(dict);

        return new SopsMetadata(
            Mac: ExtractString(dict, "mac"),
            Age: age,
            Pgp: pgp,
            Kms: ExtractOpaqueList(dict, "kms"),
            GcpKms: ExtractOpaqueList(dict, "gcp_kms"),
            AzureKv: ExtractOpaqueList(dict, "azure_kv"),
            Hckms: ExtractOpaqueList(dict, "hckms"),
            HcVault: ExtractOpaqueList(dict, "hc_vault"),
            ShamirThreshold: shamirThreshold,
            UnencryptedSuffix: unencryptedSuffix,
            EncryptedSuffix: encryptedSuffix,
            UnencryptedRegex: unencryptedRegex,
            EncryptedRegex: encryptedRegex,
            UnencryptedCommentRegex: unencryptedCommentRegex,
            EncryptedCommentRegex: encryptedCommentRegex,
            MacOnlyEncrypted: ExtractBool(dict, "mac_only_encrypted", false),
            Version: ExtractString(dict, "version"),
            LastModified: ExtractDateTime(dict, "lastmodified"),
            KeyGroups: ExtractKeyGroups(dict));
    }

    private static (string? UnencryptedSuffix, string? EncryptedSuffix, string? UnencryptedRegex,
        string? EncryptedRegex, string? UnencryptedCommentRegex, string? EncryptedCommentRegex)
        ExtractCryptRules(Dictionary<string, object> dict)
    {
        var unencryptedSuffix = ExtractString(dict, "unencrypted_suffix");
        var encryptedSuffix = ExtractString(dict, "encrypted_suffix");
        var unencryptedRegex = ExtractString(dict, "unencrypted_regex");
        var encryptedRegex = ExtractString(dict, "encrypted_regex");
        var unencryptedCommentRegex = ExtractString(dict, "unencrypted_comment_regex");
        var encryptedCommentRegex = ExtractString(dict, "encrypted_comment_regex");

        var ruleCount = 0;
        void Count(string? value)
        {
            if (!string.IsNullOrEmpty(value))
                ruleCount++;
        }

        Count(unencryptedSuffix);
        Count(encryptedSuffix);
        Count(unencryptedRegex);
        Count(encryptedRegex);
        Count(unencryptedCommentRegex);
        Count(encryptedCommentRegex);

        if (ruleCount > 1)
        {
            throw new SopsMetadataException(
                "Cannot use more than one of encrypted_suffix, unencrypted_suffix, encrypted_regex, " +
                "unencrypted_regex, encrypted_comment_regex, or unencrypted_comment_regex in the same file.");
        }

        // No rule declared: SOPS defaults to the "_unencrypted" suffix.
        if (ruleCount == 0)
            unencryptedSuffix = DefaultUnencryptedSuffix;

        return (unencryptedSuffix, encryptedSuffix, unencryptedRegex, encryptedRegex,
            unencryptedCommentRegex, encryptedCommentRegex);
    }

    private static IReadOnlyList<KeyGroup> ExtractKeyGroups(Dictionary<string, object> dict)
    {
        // Nested form: key_groups: [ { age: [...], pgp: [...], ... }, ... ]
        if (dict.TryGetValue("key_groups", out var rawGroups) && rawGroups is List<object> groups)
        {
            var result = new List<KeyGroup>(groups.Count);
            foreach (var group in groups)
            {
                var entries = new List<KeyGroupEntry>();
                if (group is Dictionary<string, object> groupDict)
                {
                    CollectEntries(groupDict, "age", "age", entries);
                    CollectEntries(groupDict, "pgp", "pgp", entries);
                    CollectEntries(groupDict, "kms", "aws_kms", entries);
                    CollectEntries(groupDict, "gcp_kms", "gcp_kms", entries);
                    CollectEntries(groupDict, "azure_kv", "azure_kv", entries);
                    CollectEntries(groupDict, "hckms", "hckms", entries);
                    CollectEntries(groupDict, "hc_vault", "hc_vault", entries);
                }
                result.Add(new KeyGroup(entries));
            }
            return result;
        }

        // Flat form: one implicit key group containing every key in the file.
        var flat = new List<KeyGroupEntry>();
        CollectEntries(dict, "age", "age", flat);
        CollectEntries(dict, "pgp", "pgp", flat);
        CollectEntries(dict, "kms", "aws_kms", flat);
        CollectEntries(dict, "gcp_kms", "gcp_kms", flat);
        CollectEntries(dict, "azure_kv", "azure_kv", flat);
        CollectEntries(dict, "hckms", "hckms", flat);
        CollectEntries(dict, "hc_vault", "hc_vault", flat);

        return flat.Count > 0
            ? new List<KeyGroup> { new(flat) }
            : Array.Empty<KeyGroup>();
    }

    private static void CollectEntries(
        Dictionary<string, object> dict, string field, string keyType, List<KeyGroupEntry> into)
    {
        if (!dict.TryGetValue(field, out var value) || value is not List<object> list)
            return;

        foreach (var item in list)
        {
            if (item is Dictionary<string, object> entry)
            {
                var enc = ExtractString(entry, "enc");
                if (!string.IsNullOrEmpty(enc))
                    into.Add(new KeyGroupEntry(keyType, enc));
            }
        }
    }

    private static IReadOnlyList<AgeEntry> ExtractAgeEntries(Dictionary<string, object> dict)
    {
        if (dict.TryGetValue("age", out var ageObj) && ageObj is List<object> ageList)
        {
            return ageList.Select(obj =>
            {
                if (obj is Dictionary<string, object> d)
                    return new AgeEntry(
                        ExtractString(d, "recipient") ?? "",
                        ExtractString(d, "enc") ?? "");
                return new AgeEntry("", "");
            }).ToList();
        }
        return Array.Empty<AgeEntry>();
    }

    private static IReadOnlyList<PgpEntry> ExtractPgpEntries(Dictionary<string, object> dict)
    {
        if (dict.TryGetValue("pgp", out var pgpObj) && pgpObj is List<object> pgpList)
        {
            return pgpList.Select(obj =>
            {
                if (obj is Dictionary<string, object> d)
                    return new PgpEntry(
                        ExtractString(d, "fp") ?? "",
                        ExtractString(d, "enc") ?? "",
                        ExtractDateTime(d, "created_at"));
                return new PgpEntry("", "", null);
            }).ToList();
        }
        return Array.Empty<PgpEntry>();
    }

    private static IReadOnlyList<object> ExtractOpaqueList(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var obj) && obj is List<object> list)
            return list;
        return Array.Empty<object>();
    }

    private static string? ExtractString(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var val))
            return val?.ToString();
        return null;
    }

    private static int ExtractInt(Dictionary<string, object> dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return defaultValue;
    }

    private static bool ExtractBool(Dictionary<string, object> dict, string key, bool defaultValue)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is bool b) return b;
            if (bool.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return defaultValue;
    }

    private static DateTimeOffset? ExtractDateTime(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && val != null)
        {
            if (val is DateTimeOffset dt) return dt;
            if (DateTimeOffset.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return null;
    }
}
