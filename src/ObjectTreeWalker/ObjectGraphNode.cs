using System.Reflection;

namespace ObjectTreeWalker;

internal record ObjectGraphNode
{
	public string Name => MemberInfo.Name;

	public Type Type => MemberInfo.GetUnderlyingType()!;

	public MemberInfo MemberInfo { get; }

	public ObjectGraphNode? Parent { get; }

	public List<ObjectGraphNode> Children { get; } = new();

	public bool CanGet { get; internal set; }

	public bool CanSet { get; internal set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="ObjectGraphNode"/> class
	/// </summary>
	/// <param name="memberInfo">member info of the node</param>
	/// <param name="parent">parent object of the node</param>
	/// <param name="children">children of the node</param>
	public ObjectGraphNode(MemberInfo memberInfo, ObjectGraphNode? parent, IEnumerable<ObjectGraphNode>? children = null)
	{
		MemberInfo = memberInfo;
		Parent = parent;

		if (children != null)
		{
			Children.AddRange(children);
		}
	}
}
