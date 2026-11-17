// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ObjectTreeWalker;

/// <summary>
/// The names of a collection member's items (<c>Items[3]</c>, <c>Map[0].Key</c>, ...), formatted once and
/// reused by every traversal instead of being re-formatted (and re-allocated) for every item, every time.
/// </summary>
/// <remarks>
/// Thread-safe without locks: concurrent growth may drop a cached entry, never corrupt one (names are
/// immutable and equal whoever formats them). Indices past <see cref="MaxCachedIndex"/> are formatted on
/// demand, so one huge collection cannot pin unbounded memory.
/// </remarks>
internal sealed class ItemNameCache
{
    private const int MaxCachedIndex = 1024;

    private readonly string _prefix;
    private string?[] _items = Array.Empty<string?>();
    private string?[] _keys = Array.Empty<string?>();
    private string?[] _values = Array.Empty<string?>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemNameCache"/> class
    /// </summary>
    /// <param name="prefix">the collection member name</param>
    public ItemNameCache(string prefix)
    {
        _prefix = prefix;
    }

    /// <summary>
    /// Gets the names of the items of a root collection (<c>[3]</c>)
    /// </summary>
    public static ItemNameCache Root { get; } = new(string.Empty);

    /// <summary>
    /// Formats an item name without caching
    /// </summary>
    /// <param name="prefix">collection member name</param>
    /// <param name="index">item index</param>
    /// <param name="suffix">suffix (<c>.Key</c>, <c>.Value</c> or empty)</param>
    /// <returns>the name</returns>
    public static string Format(string prefix, int index, string suffix) =>
        prefix.Length == 0 ? $"[{index}]{suffix}" : $"{prefix}[{index}]{suffix}";

    /// <summary>Gets the name of item <paramref name="index"/>: <c>Prefix[index]</c></summary>
    /// <param name="index">item index</param>
    /// <returns>the name</returns>
    public string Item(int index) => Get(ref _items, index, string.Empty);

    /// <summary>Gets the name of the key of entry <paramref name="index"/>: <c>Prefix[index].Key</c></summary>
    /// <param name="index">entry index</param>
    /// <returns>the name</returns>
    public string Key(int index) => Get(ref _keys, index, ".Key");

    /// <summary>Gets the name of the value of entry <paramref name="index"/>: <c>Prefix[index].Value</c></summary>
    /// <param name="index">entry index</param>
    /// <returns>the name</returns>
    public string Value(int index) => Get(ref _values, index, ".Value");

    private string Get(ref string?[] cache, int index, string suffix)
    {
        var names = cache;
        if ((uint)index < (uint)names.Length && names[index] is { } cached)
        {
            return cached;
        }

        var name = Format(_prefix, index, suffix);
        if (index < MaxCachedIndex)
        {
            if (index >= names.Length)
            {
                var grown = new string?[Math.Min(MaxCachedIndex, Math.Max(index + 1, Math.Max(8, names.Length * 2)))];
                Array.Copy(names, grown, names.Length);
                names = grown;
                cache = grown;
            }

            names[index] = name;
        }

        return name;
    }
}
