using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nops.Core.Stores;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlComment = YamlDotNet.Core.Events.Comment;

namespace Nops.Core.Stores;

/// <summary>
/// Store for YAML files. Parses YAML into ordered TreeBranches (preserving nested
/// mappings, sequences, and comments) and extracts .sops metadata.
///
/// Uses YamlDotNet's event parser (comments enabled) so that the tree matches the
/// semantics of sops' yaml store: scalars are typed exactly like gopkg.in/yaml.v3,
/// comments become <see cref="Comment"/> items, and comment-based encryption rules
/// (encrypted_comment_regex / unencrypted_comment_regex) work.
/// </summary>
public static class YamlStore
{
    /// <summary>
    /// Load a SOPS-encrypted YAML file and parse it into an ordered tree.
    /// </summary>
    public static SopsTree LoadEncrypted(byte[] fileBytes, string filePath)
    {
        var text = Encoding.UTF8.GetString(fileBytes);
        var reader = new EventReader(text);

        var branch = reader.ReadRootBranch();
        var metadata = ExtractMetadata(branch);

        var sopsKeys = branch.Count(item => item.Key is string key && key == "sops");
        if (sopsKeys > 1)
            throw new SopsMetadataException("Found more than one 'sops' key at the top level of the file.");

        // The .sops key is metadata and must not appear in the decrypted data tree.
        branch.RemoveAll(item => item.Key is string key && key == "sops");

        return new SopsTree(new[] { branch }, metadata, filePath);
    }

    private static SopsMetadata ExtractMetadata(TreeBranch root)
    {
        var sopsItem = root.FirstOrDefault(item => item.Key is string key && key == "sops");

        if (sopsItem?.Value is TreeBranch sopsBranch)
            return SopsMetadataParser.Parse(TreeConverter.ToDictionary(sopsBranch));

        return SopsMetadataParser.Empty();
    }

    /// <summary>
    /// Event-driven YAML reader that mirrors sops' node -&gt; tree conversion, including
    /// comment capture. Comments are attached to the item that follows them (matching
    /// the HeadComment/LineComment handling in sops' yaml store).
    /// </summary>
    private sealed class EventReader
    {
        private readonly Parser _parser;
        private readonly string[] _lines;

        // Anchored nodes by name, so aliases can be expanded like yaml.v3/sops do.
        private readonly Dictionary<string, object?> _anchors = new(StringComparer.Ordinal);

        // Comments found just before the end of a nested block that belong to the
        // enclosing block (they are indented less than the nested block's content).
        private readonly List<YamlComment> _carryEvents = new();

        public EventReader(string text)
        {
            _lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var scanner = new Scanner(new StringReader(text), skipComments: false);
            _parser = new Parser(scanner);
        }

        private ParsingEvent? Current => _parser.Current;

        private bool Advance() => _parser.MoveNext();

        public TreeBranch ReadRootBranch()
        {
            if (!Advance() || Current is not StreamStart)
                throw new InvalidOperationException("YAML document is empty");

            if (!Advance())
                throw new InvalidOperationException("YAML document is empty");

            // Comments before the document marker (explicit "---") become leading comments.
            var leading = new List<Comment>();
            while (Current is YamlComment c)
            {
                leading.Add(DecodeComment(c));
                if (!Advance())
                    throw new InvalidOperationException("YAML document is empty");
            }

            if (Current is StreamEnd or DocumentEnd)
                throw new InvalidOperationException("YAML document is empty");

            if (Current is not DocumentStart)
                throw new InvalidOperationException("YAML root is not a mapping");

            if (!Advance())
                throw new InvalidOperationException("YAML document is empty");

            // Comments between the document marker and the root node.
            while (Current is YamlComment c)
            {
                leading.Add(DecodeComment(c));
                if (!Advance())
                    throw new InvalidOperationException("YAML document is empty");
            }

            if (Current is not MappingStart)
                throw new InvalidOperationException("YAML root is not a mapping");

            var branch = ReadMapping();

            foreach (var comment in TakeCarry())
                branch.Add(new TreeItem(DecodeComment(comment), null));

            if (leading.Count > 0)
                branch.InsertRange(0, leading.Select(comment => new TreeItem(comment, null)));

            return branch;
        }

        private TreeBranch ReadMapping()
        {
            var start = (MappingStart)Current!;
            Advance(); // consume MappingStart
            var branch = new TreeBranch(isSequence: false);
            RegisterAnchor(start.Anchor, branch);
            long? contentColumn = null;

            while (true)
            {
                var pending = TakeCarry();

                while (Current is YamlComment c)
                {
                    pending.Add(c);
                    if (!Advance())
                        break;
                }

                if (Current is MappingEnd)
                {
                    foreach (var comment in KeepFootComments(pending, contentColumn))
                        branch.Add(new TreeItem(comment, null));
                    Advance();
                    break;
                }

                contentColumn ??= Current?.Start.Column;
                var key = ReadKey();

                // Comments between the key and its value (e.g. "key: # note").
                while (Current is YamlComment c)
                {
                    pending.Add(c);
                    if (!Advance())
                        break;
                }

                var value = ReadValue();

                // A trailing inline comment (same line as the value) belongs to this value.
                if (Current is YamlComment inline && inline.IsInline)
                {
                    pending.Add(inline);
                    Advance();
                }

                foreach (var comment in pending)
                    branch.Add(new TreeItem(DecodeComment(comment), null));
                branch.Add(new TreeItem(key, value));
            }

            return branch;
        }

        private TreeBranch ReadSequence()
        {
            var start = (SequenceStart)Current!;
            Advance(); // consume SequenceStart
            var branch = new TreeBranch(isSequence: true);
            RegisterAnchor(start.Anchor, branch);
            var index = 0;

            while (true)
            {
                var pending = TakeCarry();

                while (Current is YamlComment c)
                {
                    pending.Add(c);
                    if (!Advance())
                        break;
                }

                if (Current is SequenceEnd)
                {
                    // Comment items share the index of the element that follows them,
                    // so element indices are the same as with comments removed.
                    foreach (var comment in KeepFootComments(pending, start.Start.Column))
                        branch.Add(new TreeItem(index, comment));
                    Advance();
                    break;
                }

                var value = ReadValue();

                if (Current is YamlComment inline && inline.IsInline)
                {
                    pending.Add(inline);
                    Advance();
                }

                foreach (var comment in pending)
                    branch.Add(new TreeItem(index, DecodeComment(comment)));
                branch.Add(new TreeItem(index++, value));
            }

            return branch;
        }

        private string ReadKey()
        {
            if (Current is Scalar scalar)
            {
                Advance();
                RegisterAnchor(scalar.Anchor, scalar.Value);
                return scalar.Value;
            }

            if (Current is AnchorAlias alias)
            {
                Advance();
                return ResolveAlias(alias) switch
                {
                    string s => s,
                    TreeBranch => throw new InvalidOperationException(
                        $"YAML mapping key alias '*{alias.Value.Value}' does not refer to a scalar."),
                    var other => Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty,
                };
            }

            throw new InvalidOperationException(
                $"YAML mapping key is not a scalar (found {Current?.GetType().Name ?? "end of stream"}).");
        }

        private object? ReadValue()
        {
            switch (Current)
            {
                case Scalar scalar:
                {
                    Advance();
                    var value = ParseScalar(scalar);
                    RegisterAnchor(scalar.Anchor, value);
                    return value;
                }
                case MappingStart:
                    return ReadMapping();
                case SequenceStart:
                    return ReadSequence();
                case AnchorAlias alias:
                    Advance();
                    return ResolveAlias(alias);
                default:
                    throw new InvalidOperationException(
                        $"Unexpected YAML event: {Current?.GetType().Name ?? "end of stream"}.");
            }
        }

        private void RegisterAnchor(AnchorName anchor, object? value)
        {
            if (!anchor.IsEmpty)
                _anchors[anchor.Value] = value;
        }

        /// <summary>
        /// Expand an alias to a copy of its anchored node (sops resolves aliases, so the
        /// expanded content is decrypted and hashed into the MAC). Branches are copied
        /// because the tree walker rewrites them in place.
        /// </summary>
        private object? ResolveAlias(AnchorAlias alias)
        {
            if (!_anchors.TryGetValue(alias.Value.Value, out var value))
                throw new InvalidOperationException($"YAML alias '*{alias.Value.Value}' refers to an unknown anchor.");

            return value is TreeBranch branch ? DeepCopy(branch) : value;
        }

        private static TreeBranch DeepCopy(TreeBranch branch)
        {
            var copy = new TreeBranch(branch.IsSequence);
            foreach (var item in branch)
                copy.Add(new TreeItem(item.Key, item.Value is TreeBranch sub ? DeepCopy(sub) : item.Value));
            return copy;
        }

        private List<YamlComment> TakeCarry()
        {
            var taken = new List<YamlComment>(_carryEvents);
            _carryEvents.Clear();
            return taken;
        }

        /// <summary>
        /// Split the comments found right before the end of a block: those indented at
        /// least as far as the block's content are its foot comments (returned); the rest
        /// belong to the enclosing block - typically the head comment of the next key - and
        /// are carried out to it.
        /// </summary>
        private List<Comment> KeepFootComments(List<YamlComment> pending, long? contentColumn)
        {
            var kept = new List<Comment>();
            for (var i = 0; i < pending.Count; i++)
            {
                if (contentColumn.HasValue && pending[i].Start.Column < contentColumn.Value)
                {
                    _carryEvents.AddRange(pending.Skip(i));
                    break;
                }

                kept.Add(DecodeComment(pending[i]));
            }

            return kept;
        }

        private Comment DecodeComment(YamlComment comment)
        {
            return new Comment(CommentText(comment), comment.IsInline);
        }

        /// <summary>
        /// Reconstruct the comment text exactly as sops sees it: the raw text after
        /// the leading '#', including any whitespace. sops strips only the '#'.
        /// </summary>
        private string CommentText(YamlComment comment)
        {
            var lineIndex = (int)(comment.Start.Line - 1);
            if (lineIndex >= 0 && lineIndex < _lines.Length)
            {
                var line = _lines[lineIndex];
                var column = (int)(comment.Start.Column - 1);
                if (column >= 0 && column < line.Length && line[column] == '#')
                    return line[(column + 1)..];
            }

            return comment.Value;
        }
    }

    private static readonly Regex DateOnlyRegex = new(
        @"^(?<y>\d{4})-(?<mo>\d{1,2})-(?<d>\d{1,2})$", RegexOptions.Compiled);

    private static readonly Regex TimestampTzRegex = new(
        @"^(?<y>\d{4})-(?<mo>\d{1,2})-(?<d>\d{1,2})[Tt](?<h>\d{1,2}):(?<mi>\d{1,2}):(?<s>\d{1,2})(?<f>\.\d+)?(?<z>[Zz]|[+-]\d{2}:[0-9]{2})$",
        RegexOptions.Compiled);

    private static readonly Regex TimestampSpaceRegex = new(
        @"^(?<y>\d{4})-(?<mo>\d{1,2})-(?<d>\d{1,2}) (?<h>\d{1,2}):(?<mi>\d{1,2}):(?<s>\d{1,2})(?<f>\.\d+)?$",
        RegexOptions.Compiled);

    private static readonly Regex YamlStyleFloatRegex = new(
        @"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.Compiled);

    /// <summary>
    /// Type a YAML scalar the same way gopkg.in/yaml.v3 does (see resolve.go).
    /// Quoted and block scalars are always strings.
    /// </summary>
    private static object? ParseScalar(Scalar scalar)
    {
        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted
            or ScalarStyle.Literal or ScalarStyle.Folded)
        {
            return scalar.Value;
        }

        // Honor an explicit !!str tag; other explicit tags fall through to resolution.
        if (!scalar.Tag.IsEmpty && !scalar.Tag.IsNonSpecific)
        {
            var tag = scalar.Tag.Value;
            if (tag is "tag:yaml.org,2002:str" or "!!str")
                return scalar.Value;
        }

        return ResolveScalar(scalar.Value);
    }

    private static object? ResolveScalar(string input)
    {
        if (input.Length == 0)
            return null;

        var hint = ResolveHint(input[0]);
        if (hint == '\0')
            return input;

        switch (input)
        {
            case "true" or "True" or "TRUE":
                return true;
            case "false" or "False" or "FALSE":
                return false;
            case "~" or "null" or "Null" or "NULL":
                return null;
            case ".nan" or ".NaN" or ".NAN":
                return double.NaN;
            case ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF":
                return double.PositiveInfinity;
            case "-.inf" or "-.Inf" or "-.INF":
                return double.NegativeInfinity;
        }

        if (hint == '.')
        {
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                return floatValue;
            return input;
        }

        if (hint is 'D' or 'S')
        {
            if (TryParseTimestamp(input, out var timestamp))
                return timestamp;

            var plain = input.Replace("_", "");
            if (TryParseGoInt(plain, out var intValue))
                return intValue;

            if (YamlStyleFloatRegex.IsMatch(plain)
                && double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            {
                return floatValue;
            }
        }

        return input;
    }

    /// <summary>
    /// Mirror of yaml.v3's resolveTable hinting: which first characters can start a
    /// resolvable scalar.
    /// </summary>
    private static char ResolveHint(char c)
    {
        switch (c)
        {
            case '+':
            case '-':
                return 'S';
            case '.':
                return '.';
            case 'y':
            case 'Y':
            case 'n':
            case 'N':
            case 't':
            case 'T':
            case 'f':
            case 'F':
            case 'o':
            case 'O':
            case '~':
                return 'M';
        }

        return c is >= '0' and <= '9' ? 'D' : '\0';
    }

    /// <summary>
    /// Parse an integer like Go's strconv.ParseInt(s, 0, 64): base inferred from the
    /// prefix (0x/0o/0b/leading-zero octal), optional sign, underscores already removed.
    /// </summary>
    private static bool TryParseGoInt(string s, out long value)
    {
        value = 0;
        if (s.Length == 0)
            return false;

        var negative = false;
        var start = 0;
        if (s[0] is '+' or '-')
        {
            negative = s[0] == '-';
            start = 1;
        }

        if (start >= s.Length)
            return false;

        var digits = s[start..];
        var radix = 10;

        if (digits.Length > 1 && digits[0] == '0')
        {
            switch (digits[1])
            {
                case 'x':
                case 'X':
                    radix = 16;
                    digits = digits[2..];
                    break;
                case 'o':
                case 'O':
                    radix = 8;
                    digits = digits[2..];
                    break;
                case 'b':
                case 'B':
                    radix = 2;
                    digits = digits[2..];
                    break;
                default:
                    radix = 8;
                    digits = digits[1..];
                    break;
            }
        }

        if (digits.Length == 0)
            return false;

        ulong accumulator = 0;
        foreach (var c in digits)
        {
            var digit = DigitValue(c);
            if (digit < 0 || digit >= radix)
                return false;

            if (accumulator > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
                return false;

            accumulator = accumulator * (ulong)radix + (ulong)digit;
        }

        if (negative)
        {
            // Allow long.MinValue magnitude.
            if (accumulator > 9223372036854775808UL)
                return false;
            value = accumulator == 9223372036854775808UL ? long.MinValue : -(long)accumulator;
            return true;
        }

        // Values above long.MaxValue are uint64 in Go; Nops keeps them as strings.
        if (accumulator > long.MaxValue)
            return false;

        value = (long)accumulator;
        return true;
    }

    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// Timestamp resolution matching yaml.v3's allowedTimestampFormats.
    /// </summary>
    private static bool TryParseTimestamp(string s, out DateTimeOffset result)
    {
        result = default;
        if (s.Length < 5 || s[4] != '-' || !IsAllDigits(s, 0, 4))
            return false;

        var match = DateOnlyRegex.Match(s);
        if (match.Success)
            return TryBuildTimestamp(match, hasTime: false, hasZone: false, out result);

        match = TimestampTzRegex.Match(s);
        if (match.Success)
            return TryBuildTimestamp(match, hasTime: true, hasZone: true, out result);

        match = TimestampSpaceRegex.Match(s);
        if (match.Success)
            return TryBuildTimestamp(match, hasTime: true, hasZone: false, out result);

        return false;
    }

    private static bool TryBuildTimestamp(Match match, bool hasTime, bool hasZone, out DateTimeOffset result)
    {
        result = default;

        if (!int.TryParse(match.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(match.Groups["mo"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(match.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        var hour = 0;
        var minute = 0;
        var second = 0;
        if (hasTime
            && (!int.TryParse(match.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out hour)
                || !int.TryParse(match.Groups["mi"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minute)
                || !int.TryParse(match.Groups["s"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out second)))
        {
            return false;
        }

        var ticks = 0L;
        var fraction = match.Groups["f"].Value;
        if (fraction.Length > 1)
        {
            var digits = fraction[1..];
            if (digits.Length > 7)
                digits = digits[..7];
            digits = digits.PadRight(7, '0');
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ticks))
                return false;
        }

        var offset = TimeSpan.Zero;
        if (hasZone)
        {
            var zone = match.Groups["z"].Value;
            if (zone is not ("Z" or "z"))
            {
                var sign = zone[0] == '-' ? -1 : 1;
                var hours = int.Parse(zone.Substring(1, 2), CultureInfo.InvariantCulture);
                var minutes = int.Parse(zone.Substring(4, 2), CultureInfo.InvariantCulture);
                offset = new TimeSpan(sign * hours, sign * minutes, 0);
            }
        }

        try
        {
            result = new DateTimeOffset(year, month, day, hour, minute, second, offset).AddTicks(ticks);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsAllDigits(string s, int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            if (s[i] is < '0' or > '9')
                return false;
        }
        return true;
    }
}
