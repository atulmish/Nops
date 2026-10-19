using System;
using System.Collections.Generic;

namespace Nops.Core.Stores;

/// <summary>
/// Converts an ordered <see cref="TreeBranch"/> into plain CLR objects
/// (Dictionary for mappings, List for sequences) so metadata can be fed to
/// <see cref="SopsMetadataParser"/>.
/// </summary>
public static class TreeConverter
{
    public static object? ToObject(object? value)
    {
        return value switch
        {
            TreeBranch branch => BranchToObject(branch),
            Comment comment => comment.Value,
            _ => value,
        };
    }

    public static Dictionary<string, object> ToDictionary(TreeBranch branch)
    {
        var dict = new Dictionary<string, object>();
        foreach (var item in branch)
        {
            if (item.Key is string key)
                dict[key] = ToObject(item.Value) ?? "";
        }
        return dict;
    }

    /// <summary>
    /// Convert a branch into a plain CLR object graph for typed deserialization:
    /// mappings become <see cref="Dictionary{TKey,TValue}"/>, sequences become
    /// <see cref="List{T}"/>, and comment entries are dropped.
    /// </summary>
    public static object? ToPlainObject(object? value)
    {
        return value switch
        {
            TreeBranch branch => BranchToPlain(branch),
            _ => value,
        };
    }

    private static object? BranchToPlain(TreeBranch branch)
    {
        if (branch.IsSequence)
        {
            var list = new List<object?>();
            foreach (var item in branch)
            {
                if (item.Value is Comment)
                    continue;
                list.Add(ToPlainObject(item.Value));
            }
            return list;
        }

        var dict = new Dictionary<string, object?>();
        foreach (var item in branch)
        {
            if (item.Key is not string key || item.Value is Comment)
                continue;
            dict[key] = ToPlainObject(item.Value);
        }
        return dict;
    }

    private static object BranchToObject(TreeBranch branch)
    {
        if (branch.IsSequence)
        {
            var list = new List<object>();
            foreach (var item in branch)
                list.Add(ToObject(item.Value) ?? "");
            return list;
        }

        return ToDictionary(branch);
    }
}
