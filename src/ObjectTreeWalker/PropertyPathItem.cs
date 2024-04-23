namespace ObjectTreeWalker;

public record struct PropertyPathItem(string Name, int? ItemIndex = null, bool IsPartOfDictionary = false)
{
    public bool IsPartOfCollection => ItemIndex.HasValue;
}