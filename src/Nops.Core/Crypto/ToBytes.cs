using System;
using System.Globalization;
using System.Text;

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
        // - No trailing zeros
        // - No decimal point for integer values
        // - Shortest representation that round-trips

        string str;
        if (number is double d)
        {
            // Use Roundtrip format which gives shortest round-trip representation
            str = d.ToString("R", CultureInfo.InvariantCulture);

            // Handle special cases
            if (d == 0.0 && Math.Sign(d) < 0)
                return Encoding.UTF8.GetBytes("0");

            // Ensure no trailing zeros after decimal point
            if (str.Contains('.'))
            {
                str = str.TrimEnd('0').TrimEnd('.');
            }
        }
        else if (number is float f)
        {
            str = f.ToString("R", CultureInfo.InvariantCulture);
            if (str.Contains('.'))
                str = str.TrimEnd('0').TrimEnd('.');
        }
        else
        {
            return Encoding.UTF8.GetBytes(System.Convert.ToString(number, CultureInfo.InvariantCulture)!);
        }

        // Handle the case where FormatFloat('f', -1, 64) would produce "0" for 0.0
        if (str == "" || str == "-")
            str = number is double dd && dd < 0 ? "-0" : "0";

        return Encoding.UTF8.GetBytes(str);
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
}
