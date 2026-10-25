using System;
using System.Text;

namespace Nops.Core.Keys;

/// <summary>
/// Decoder for the strict ASCII armor around age files (a canonical subset of PEM with
/// 64-column base64 lines). LF and CRLF line endings are accepted; whitespace is
/// tolerated only around the whole file.
/// </summary>
internal static class AgeArmor
{
    private const string Begin = "-----BEGIN AGE ENCRYPTED FILE-----";
    private const string End = "-----END AGE ENCRYPTED FILE-----";
    private const int ColumnWidth = 64;

    public static bool LooksArmored(byte[] data)
    {
        var i = 0;
        while (i < data.Length && IsWhitespace(data[i]))
            i++;
        return data.AsSpan(i).StartsWith("-----BEGIN"u8);
    }

    public static byte[] Decode(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data).Trim(' ', '\t', '\r', '\n');
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
                lines[i] = lines[i][..^1];
        }

        if (lines.Length < 3 || lines[0] != Begin || lines[^1] != End)
            throw new InvalidDataException("Malformed age armor: missing BEGIN or END line.");

        var body = new StringBuilder();
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var last = i == lines.Length - 2;
            var line = lines[i];
            if (last ? line.Length is < 1 or > ColumnWidth : line.Length != ColumnWidth)
                throw new InvalidDataException("Malformed age armor: bad line length.");
            body.Append(line);
        }

        var encoded = body.ToString();
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (Convert.ToBase64String(bytes) != encoded)
                throw new InvalidDataException("Malformed age armor: base64 is not canonical.");
            return bytes;
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Malformed age armor: invalid base64.", ex);
        }
    }

    private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
