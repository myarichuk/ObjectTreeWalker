using System.Reflection;

namespace ObjectTreeWalker;

internal record ObjectGraphNode
{
    /// <summary>
    /// Gets the name of the member (name of property/field)
    /// </summary>
    public string Name => MemberInfo.Name;

    /// <summary>
    /// Gets the type of the member
    /// </summary>
    public Type Type => MemberInfo.GetUnderlyingType()!;

    /// <summary>
    /// Gets the reflection metadata of the member
    /// </summary>
    public MemberInfo MemberInfo { get; }

    /// <summary>
    /// Gets the reference to the parent of current node (null for root members)
    /// </summary>
    public ObjectGraphNode? Parent { get; }

    /// <summary>
    /// Gets the list of the current member children
    /// </summary>
    public List<ObjectGraphNode> Children { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether we can get the value of this member. Always true for fields, properties may be set only and then this will be false.
    /// </summary>
    public bool CanGet { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether we can set the value of this member. Always true for fields, properties may be get only and then this will be false.
    /// </summary>
    public bool CanSet { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a property or a field
    /// </summary>
    public MemberType MemberType { get; internal set; }

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
