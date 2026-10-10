using System;
using System.Collections.Generic;
using System.Linq;

namespace Nops.Core.Stores;

/// <summary>
/// Parser for SOPS .sops metadata extracted from YAML/JSON files.
/// Handles the flat dict format used in .sops top-level key.
/// </summary>
public static class SopsMetadataParser
{
    /// <summary>
    /// Parse a flat dictionary (from .sops key) into SopsMetadata.
    /// Unknown keys are ignored for forward compatibility.
    /// </summary>
    public static SopsMetadata Parse(Dictionary<string, object> dict)
    {
        var age = ExtractAgeEntries(dict);
        var pgp = ExtractPgpEntries(dict);

        // shamir_threshold == 0 means "use number of groups" (computed later)
        var shamirThreshold = ExtractInt(dict, "shamir_threshold", 0);

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
            UnencryptedSuffix: ExtractString(dict, "unencrypted_suffix"),
            EncryptedSuffix: ExtractString(dict, "encrypted_suffix"),
            UnencryptedRegex: ExtractString(dict, "unencrypted_regex"),
            EncryptedRegex: ExtractString(dict, "encrypted_regex"),
            UnencryptedCommentRegex: ExtractString(dict, "unencrypted_comment_regex"),
            EncryptedCommentRegex: ExtractString(dict, "encrypted_comment_regex"),
            MacOnlyEncrypted: ExtractBool(dict, "mac_only_encrypted", false),
            Version: ExtractString(dict, "version"),
            LastModified: ExtractDateTime(dict, "lastmodified")
        );
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
