using System;
using System.Collections.Generic;

namespace Nops.Core.Stores;

/// <summary>
/// Decodes the "flat" .sops metadata keys used by Dotenv and INI stores back into a
/// nested Dictionary/List structure that <see cref="SopsMetadataParser"/> can consume.
/// Flat keys use "__" as a path separator with "list_&lt;N&gt;" for array indices and
/// "map_&lt;name&gt;" for map keys (e.g. "age__list_0__map_enc").
/// </summary>
internal static class FlatMetadataDecoder
{
    // Real files hold a handful of entries per list (key groups, recipients). The cap stops a corrupted
    // index such as "list_2000000000" from allocating gigabytes.
    private const int MaxListIndex = 1024;

    public static Dictionary<string, object> Decode(Dictionary<string, string> flatKeys)
    {
        var root = new Dictionary<string, object>();
        foreach (var (key, value) in flatKeys)
        {
            var path = key.Split("__", StringSplitOptions.RemoveEmptyEntries);
            Insert(root, path, value);
        }
        return root;
    }

    private static void Insert(Dictionary<string, object> root, string[] path, string value)
    {
        var current = (object)root;
        for (int i = 0; i < path.Length; i++)
        {
            var seg = path[i];
            var isLast = i == path.Length - 1;
            var next = i + 1 < path.Length ? path[i + 1] : null;

            if (seg.StartsWith("list_", StringComparison.Ordinal))
            {
                var list = current as List<object>
                    ?? throw new InvalidOperationException($"Expected a list at path segment '{seg}'");
                if (!int.TryParse(seg.AsSpan(5), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var idx) || idx >= MaxListIndex)
                    throw new InvalidOperationException($"Invalid list index in path segment '{seg}'");
                while (list.Count <= idx)
                    list.Add(new Dictionary<string, object>());

                if (isLast)
                {
                    list[idx] = value;
                    return;
                }
                current = list[idx];
            }
            else
            {
                var key = seg.StartsWith("map_", StringComparison.Ordinal) ? seg[4..] : seg;
                var dict = current as Dictionary<string, object>
                    ?? throw new InvalidOperationException($"Expected a map at path segment '{seg}'");

                if (isLast)
                {
                    dict[key] = value;
                    return;
                }

                // Determine container type from the next segment.
                if (next != null && next.StartsWith("list_", StringComparison.Ordinal))
                {
                    if (!dict.TryGetValue(key, out var v) || v is not List<object> list)
                    {
                        list = new List<object>();
                        dict[key] = list;
                    }
                    current = list;
                }
                else
                {
                    if (!dict.TryGetValue(key, out var v) || v is not Dictionary<string, object> nested)
                    {
                        nested = new Dictionary<string, object>();
                        dict[key] = nested;
                    }
                    current = nested;
                }
            }
        }
    }
}
