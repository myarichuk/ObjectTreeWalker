namespace ObjectTreeWalker;

/// <summary>
/// Internal class to represent an object graph.
/// </summary>
internal sealed class ObjectGraph
{
    /// <summary>
    /// Gets the root object type
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets members of the root object type
    /// </summary>
    public IReadOnlyList<ObjectGraphNode> Roots { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectGraph"/> class
    /// </summary>
    /// <param name="type">type of the root object which graph this represents</param>
    /// <param name="roots">members of the root object type</param>
    public ObjectGraph(Type type, IEnumerable<ObjectGraphNode> roots)
    {
        Type = type;
        Roots = new List<ObjectGraphNode>(roots);
        IsValueType = type.IsValueType;
        IsCollection = type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    /// <summary>
    /// Gets a value indicating whether values of the type are expanded into their items
    /// (precomputed: saves an interface check per visited value)
    /// </summary>
    public bool IsCollection { get; }

    /// <summary>
    /// Gets a value indicating whether the type is a value type (precomputed)
    /// </summary>
    public bool IsValueType { get; }
}
