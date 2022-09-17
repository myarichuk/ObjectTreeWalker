namespace ObjectTreeWalker;

internal record ObjectGraph
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
	}
}
