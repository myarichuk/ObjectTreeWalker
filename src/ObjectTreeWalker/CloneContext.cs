// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace ObjectTreeWalker;

/// <summary>
/// State of a single <c>DeepClone</c> call: the source-to-clone identity map that preserves
/// shared references and terminates cycles.
/// </summary>
/// <remarks>
/// A purpose-built open-addressing map (linear probing, power-of-two capacity) keyed by reference identity.
/// Compared to <c>Dictionary&lt;object, object&gt;</c> with a reference comparer it has no interface
/// dispatch to a comparer, no entry/bucket indirection and probes touch a single array.
/// Instances are pooled per thread; see <see cref="Rent"/>.
/// </remarks>
internal sealed class CloneContext
{
    private const int InitialCapacity = 32; // power of two
    private const int MaxPooledCapacity = 1 << 14;

    [ThreadStatic]
    private static CloneContext? cached;

    private object?[] _keys = new object?[InitialCapacity];
    private object?[] _values = new object?[InitialCapacity];

    // occupied slot indices, so a reset costs O(entries used) instead of O(capacity)
    private int[] _used = new int[InitialCapacity / 2];
    private int _count;
    private int _growThreshold = InitialCapacity / 2;

    /// <summary>
    /// Gets a context for one clone operation: the thread's cached instance, or a new one if it is in use
    /// (a re-entrant clone, e.g. from a collection constructor that itself deep clones).
    /// </summary>
    /// <returns>an empty context</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CloneContext Rent()
    {
        var context = cached;
        if (context != null)
        {
            cached = null;
            return context;
        }

        return new CloneContext();
    }

    /// <summary>
    /// Returns a context obtained from <see cref="Rent"/>
    /// </summary>
    /// <param name="context">the context</param>
    public static void Return(CloneContext context)
    {
        if (context._keys.Length > MaxPooledCapacity)
        {
            // one huge clone must not pin its map for the thread lifetime
            return;
        }

        var keys = context._keys;
        var values = context._values;
        var used = context._used;
        for (var i = 0; i < context._count; i++)
        {
            var slot = used[i];
            keys[slot] = null;
            values[slot] = null;
        }

        context._count = 0;

        cached = context;
    }

    /// <summary>
    /// Looks up the clone already made for <paramref name="source"/>
    /// </summary>
    /// <param name="source">the source instance</param>
    /// <param name="clone">its clone, if any</param>
    /// <returns>true if <paramref name="source"/> was already cloned</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(object source, out object? clone)
    {
        if (_count != 0)
        {
            var keys = _keys;
            var mask = keys.Length - 1;
            var index = RuntimeHelpers.GetHashCode(source) & mask;
            object? key;
            while ((key = keys[index]) != null)
            {
                if (ReferenceEquals(key, source))
                {
                    clone = _values[index];
                    return true;
                }

                index = (index + 1) & mask;
            }
        }

        clone = null;
        return false;
    }

    /// <summary>
    /// Finds the slot of <paramref name="source"/>, adding it (with a null clone) if it is not there yet:
    /// one hash and one probe instead of a <see cref="TryGet"/> followed by an <see cref="Add"/>.
    /// </summary>
    /// <param name="source">the source instance</param>
    /// <param name="found">true if <paramref name="source"/> was already cloned; the slot then holds its clone</param>
    /// <returns>
    /// A reference to the clone slot. When <paramref name="found"/> is false the caller must store the clone
    /// through it <em>before</em> any other use of this context: a later insertion may grow (and move) the storage.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref object? GetOrAddSlot(object source, out bool found)
    {
        if (_count >= _growThreshold)
        {
            Grow();
        }

        var keys = _keys;
        var mask = keys.Length - 1;
        var index = RuntimeHelpers.GetHashCode(source) & mask;
        object? key;
        while ((key = keys[index]) != null)
        {
            if (ReferenceEquals(key, source))
            {
                found = true;
                return ref _values[index];
            }

            index = (index + 1) & mask;
        }

        keys[index] = source;
        _used[_count++] = index;
        found = false;
        return ref _values[index];
    }

    /// <summary>
    /// Records <paramref name="clone"/> as the clone of <paramref name="source"/>
    /// </summary>
    /// <param name="source">the source instance (not already recorded)</param>
    /// <param name="clone">its clone</param>
    public void Add(object source, object clone)
    {
        if (_count >= _growThreshold)
        {
            Grow();
        }

        _used[_count++] = Insert(_keys, _values, source, clone);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Insert(object?[] keys, object?[] values, object source, object? clone)
    {
        var mask = keys.Length - 1;
        var index = RuntimeHelpers.GetHashCode(source) & mask;
        while (keys[index] != null)
        {
            index = (index + 1) & mask;
        }

        keys[index] = source;
        values[index] = clone;
        return index;
    }

    private void Grow()
    {
        var oldKeys = _keys;
        var oldValues = _values;
        var keys = new object?[oldKeys.Length * 2];
        var values = new object?[oldKeys.Length * 2];
        var used = new int[keys.Length / 2];
        for (var i = 0; i < _count; i++)
        {
            var slot = _used[i];
            used[i] = Insert(keys, values, oldKeys[slot]!, oldValues[slot]);
        }

        _keys = keys;
        _values = values;
        _used = used;
        _growThreshold = keys.Length / 2;
    }
}
