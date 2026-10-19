using System;
using System.Globalization;
using System.Text;
using Nops.Core.Stores;

namespace Nops.Core.Crypto;

/// <summary>
/// Canonical encoding for SOPS MAC input (ToBytes from sops.go:980-1003).
/// Each .NET type maps to a specific UTF-8 byte representation.
/// </summary>
public static class ToBytes
{
    /// <summary>
    /// Convert a value to its canonical SOPS byte representation.
    /// Empty strings, empty bytes, and null return empty arrays (skipped upstream).
    /// </summary>
    public static byte[] Convert(object? value)
    {
        if (value is null)
            return Array.Empty<byte>();

        return value switch
        {
            string s => EncodeString(s),
            int i => Encoding.UTF8.GetBytes(i.ToString("D", CultureInfo.InvariantCulture)),
            long l => Encoding.UTF8.GetBytes(l.ToString("D", CultureInfo.InvariantCulture)),
            float f => EncodeFloat(f),
            double d => EncodeFloat(d),
            bool b => Encoding.UTF8.GetBytes(b ? "True" : "False"),
            byte[] bytes => bytes,
            DateTime dt => EncodeDateTime(dt),
            DateTimeOffset dto => EncodeDateTimeOffset(dto),
            Comment comment => Convert(comment.Value),
            _ => throw new InvalidOperationException(
                $"Unsupported type for SOPS ToBytes: {value.GetType().Name}. " +
                "Supported types: string, int, long, float, double, bool, byte[], DateTime.")
        };
    }

    private static byte[] EncodeString(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    private static byte[] EncodeFloat(object number)
    {
        // Go FormatFloat('f', -1, 64) semantics:
        // - Shortest representation that round-trips
        // - Always fixed-point (decimal) notation - never scientific notation
        // - No trailing zeros after the decimal point
        // - No decimal point for integer values

        string str;
        if (number is double d)
        {
            if (double.IsNaN(d))
                return Encoding.UTF8.GetBytes("NaN");
            if (double.IsPositiveInfinity(d))
                return Encoding.UTF8.GetBytes("+Inf");
            if (double.IsNegativeInfinity(d))
                return Encoding.UTF8.GetBytes("-Inf");
            if (d == 0.0)
                return Encoding.UTF8.GetBytes(d < 0 ? "-0" : "0");

            str = d.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (number is float f)
        {
            if (float.IsNaN(f))
                return Encoding.UTF8.GetBytes("NaN");
            if (float.IsPositiveInfinity(f))
                return Encoding.UTF8.GetBytes("+Inf");
            if (float.IsNegativeInfinity(f))
                return Encoding.UTF8.GetBytes("-Inf");
            if (f == 0.0f)
                return Encoding.UTF8.GetBytes(f < 0 ? "-0" : "0");

            str = f.ToString("R", CultureInfo.InvariantCulture);
        }
        else
        {
            return Encoding.UTF8.GetBytes(System.Convert.ToString(number, CultureInfo.InvariantCulture)!);
        }

        // Go 'f' format never uses scientific notation; expand any exponent.
        str = ExpandExponent(str);

        // Remove trailing zeros after the decimal point (and the point itself if empty).
        if (str.Contains('.'))
            str = str.TrimEnd('0').TrimEnd('.');

        if (str.Length == 0 || str == "-")
            str = number is double dd && dd < 0 ? "-0" : "0";

        return Encoding.UTF8.GetBytes(str);
    }

    /// <summary>
    /// Convert a .NET "R"-formatted scientific-notation string (e.g. "1E+20", "1E-07")
    /// into fixed-point decimal notation, matching Go FormatFloat('f', -1, 64).
    /// </summary>
    private static string ExpandExponent(string s)
    {
        var eIdx = s.IndexOf('E');
        if (eIdx < 0)
            eIdx = s.IndexOf('e');
        if (eIdx < 0)
            return s;

        var negative = s[0] == '-';
        var mantissa = negative ? s[1..eIdx] : s[..eIdx];
        var exp = int.Parse(s[(eIdx + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        var dot = mantissa.IndexOf('.');
        string intPart, fracPart;
        if (dot < 0)
        {
            intPart = mantissa;
            fracPart = "";
        }
        else
        {
            intPart = mantissa[..dot];
            fracPart = mantissa[(dot + 1)..];
        }

        var allDigits = intPart + fracPart;
        var pointPos = intPart.Length + exp;
        var sb = new StringBuilder(negative ? "-" : "");

        if (pointPos <= 0)
        {
            sb.Append("0.");
            sb.Append('0', -pointPos);
            sb.Append(allDigits);
        }
        else if (pointPos >= allDigits.Length)
        {
            sb.Append(allDigits);
            sb.Append('0', pointPos - allDigits.Length);
        }
        else
        {
            sb.Append(allDigits, 0, pointPos);
            sb.Append('.');
            sb.Append(allDigits, pointPos, allDigits.Length - pointPos);
        }

        return sb.ToString();
    }

    private static byte[] EncodeDateTime(DateTime dt)
    {
        // Go MarshalText for time: RFC3339 format
        // e.g., "2024-01-01T00:00:00Z"
        string rfc3339;

        if (dt.Kind == DateTimeKind.Utc)
            rfc3339 = dt.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);
        else if (dt.Kind == DateTimeKind.Local)
            rfc3339 = dt.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);
        else
            // Unspecified kind - treat as UTC to match Go behavior
            rfc3339 = DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                .ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

        return Encoding.UTF8.GetBytes(rfc3339);
    }

    private static byte[] EncodeDateTimeOffset(DateTimeOffset dto)
    {
        return Encoding.UTF8.GetBytes(FormatTimestamp(dto));
    }

    /// <summary>
    /// Format a timestamp like Go's time.Time.MarshalText (RFC3339, "Z" for UTC,
    /// numeric offset otherwise, fractional seconds trimmed of trailing zeros).
    /// </summary>
    public static string FormatTimestamp(DateTimeOffset dto)
    {
        var s = dto.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        var fraction = dto.ToString("fffffff", CultureInfo.InvariantCulture).TrimEnd('0');
        if (fraction.Length > 0)
            s += "." + fraction;

        s += dto.Offset == TimeSpan.Zero
            ? "Z"
            : dto.ToString("zzz", CultureInfo.InvariantCulture);

        return s;
    }
}
