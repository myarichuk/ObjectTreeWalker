using System.Collections;

namespace ObjectTreeWalker;

/// <summary>
/// The location of a collection item, used to write a modified value-type item back into its collection.
/// </summary>
/// <remarks>
/// Enumerating a collection of structs yields boxed <em>copies</em>. When a member of such an item is set,
/// the modified copy has to be stored back, otherwise the change would be silently lost.
/// </remarks>
internal sealed class CollectionItemSlot
{
    private readonly Location _location;

    private CollectionItemSlot(in Location location)
    {
        _location = location;
    }

    /// <summary>
    /// Kind of the item location
    /// </summary>
    internal enum SlotKind
    {
        /// <summary>An item of an <see cref="IList"/>, addressed by index</summary>
        ListIndex,

        /// <summary>An item of a non-SZ array, addressed by rank indices</summary>
        ArrayIndices,

        /// <summary>A dictionary value, addressed by its key</summary>
        DictionaryValue,

        /// <summary>A dictionary key (cannot be written back)</summary>
        DictionaryKey,

        /// <summary>An item of a collection without positional access (cannot be written back)</summary>
        NotIndexable,
    }

    /// <summary>
    /// Allocates a slot for <paramref name="location"/>
    /// </summary>
    /// <param name="location">the item location</param>
    /// <returns>slot instance</returns>
    public static CollectionItemSlot Create(in Location location) =>
        new(location.Kind == SlotKind.ArrayIndices
            ? location with { Indices = (int[])location.Indices!.Clone() } // the caller keeps advancing its index array
            : location);

    /// <summary>
    /// Stores <paramref name="value"/> back into the collection
    /// </summary>
    /// <param name="value">the (modified) item value</param>
    /// <exception cref="InvalidOperationException">The item is a dictionary key, or lives in a collection without positional access</exception>
    public void Write(object? value)
    {
        switch (_location.Kind)
        {
            case SlotKind.ListIndex:
                ((IList)_location.Container)[_location.Index] = value;
                break;
            case SlotKind.ArrayIndices:
                ((Array)_location.Container).SetValue(value, _location.Indices!);
                break;
            case SlotKind.DictionaryValue:
                ((IDictionary)_location.Container)[_location.Key!] = value;
                break;
            case SlotKind.DictionaryKey:
                throw new InvalidOperationException(
                    "Cannot persist a change to a member of a value-type dictionary key: dictionary keys cannot be modified in place.");
            default:
                throw new InvalidOperationException(
                    $"Cannot persist a change to a member of a value-type item of {_location.Container.GetType()}: the collection has no positional access to store the modified copy back.");
        }
    }

    /// <summary>
    /// A non-allocating description of where an item lives; turned into a slot only when needed
    /// </summary>
    /// <param name="Container">the collection</param>
    /// <param name="Kind">how the item is addressed</param>
    /// <param name="Index">index of the item, for <see cref="SlotKind.ListIndex"/></param>
    /// <param name="Indices">rank indices of the item, for <see cref="SlotKind.ArrayIndices"/></param>
    /// <param name="Key">key of the entry, for <see cref="SlotKind.DictionaryValue"/></param>
    internal readonly record struct Location(object Container, SlotKind Kind, int Index = 0, int[]? Indices = null, object? Key = null);
}
