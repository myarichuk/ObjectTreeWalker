namespace ObjectTreeWalker;

/// <summary>
/// A single item in a property path.
/// </summary>
/// <param name="Name">Name of the item</param>
/// <param name="ItemIndex">Index of the item in the collection if any</param>
/// <param name="IsPartOfDictionary">Indicates whether item is part of a dictionary</param>
public record struct PropertyPathItem(string Name, int? ItemIndex = null, bool IsPartOfDictionary = false)
{
    /// <summary>
    /// Gets a value indicating whether this item is part of a collection.
    /// </summary>
    public bool IsPartOfCollection => ItemIndex.HasValue;
}