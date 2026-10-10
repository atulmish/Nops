using System;
using System.Text.RegularExpressions;

namespace Nops.Core.Stores;

/// <summary>
/// Represents a parsed SOPS encrypted value: ENC[AES256_GCM,data:...,iv:...,tag:...,type:...].
/// </summary>
public record EncryptedValue(byte[] Data, byte[] Iv, byte[] Tag, string DataType);

/// <summary>
/// Parser for SOPS encrypted values.
/// Matches the upstream regex from sops/aes/cipher.go:55 with no trailing $ anchor.
/// </summary>
public static class EncryptedValueParser
{
    // Upstream regex (sops/aes/cipher.go:55) - NO trailing $ anchor
    private static readonly Regex EncryptedValueRegex = new(
        @"^ENC\[AES256_GCM,data:(.+),iv:(.+),tag:(.+),type:(.+)\]",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>
    /// Try to parse a SOPS encrypted value string.
    /// Returns false for empty strings or non-matching input.
    /// </summary>
    public static bool TryParse(string s, out EncryptedValue? value)
    {
        value = null;

        if (string.IsNullOrEmpty(s))
            return false;

        var match = EncryptedValueRegex.Match(s);
        if (!match.Success)
            return false;

        // Extract capture groups (group 1-4 for data, iv, tag, type)
        var dataBase64 = match.Groups[1].Value;
        var ivBase64 = match.Groups[2].Value;
        var tagBase64 = match.Groups[3].Value;
        var dataType = match.Groups[4].Value;

        // Decode base64 using standard encoding (not URL-safe)
        try
        {
            var data = Convert.FromBase64String(dataBase64);
            var iv = Convert.FromBase64String(ivBase64);
            var tag = Convert.FromBase64String(tagBase64);

            value = new EncryptedValue(data, iv, tag, dataType);
            return true;
        }
        catch (FormatException)
        {
            // Invalid base64 - not a valid encrypted value
            return false;
        }
    }
}
