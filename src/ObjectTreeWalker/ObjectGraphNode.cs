using System.Reflection;

namespace ObjectTreeWalker;

/// <summary>
/// Graph node representing a member in the ObjectGraph.
/// </summary>
/// <remarks>
/// A node owns the compiled accessors of its member, so reading/writing a member during
/// traversal is a direct delegate call instead of a name-keyed lookup. Binding accessors to
/// the <see cref="MemberInfo"/> (rather than to its name) is also what keeps members hidden
/// with <c>new</c> distinct from the base member they hide.
/// </remarks>
internal sealed class ObjectGraphNode
{
    private Func<object, object?>? _getter;
    private Action<object, object?>? _setter;
    private ItemNameCache? _itemNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectGraphNode"/> class
    /// </summary>
    /// <param name="memberInfo">member info of the node</param>
    /// <param name="parent">parent object of the node</param>
    public ObjectGraphNode(MemberInfo memberInfo, ObjectGraphNode? parent)
    {
        MemberInfo = memberInfo;
        Name = memberInfo.Name;
        Type = memberInfo.GetUnderlyingType()!;
        Parent = parent;
    }

    /// <summary>
    /// Gets the name of the member (name of property/field)
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the declared type of the member
    /// </summary>
    public Type Type { get; }

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
    /// Gets a value indicating whether we can get the value of this member. Always true for fields, properties may be set only and then this will be false.
    /// </summary>
    public bool CanGet { get; init; }

    /// <summary>
    /// Gets a value indicating whether we can set the value of this member. Always true for fields, properties may be get only and then this will be false.
    /// </summary>
    public bool CanSet { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a property or a field
    /// </summary>
    public MemberType MemberType { get; init; }

    /// <summary>
    /// Gets or sets the member graph of the runtime type this member's value had last time: a monomorphic
    /// inline cache in front of the type-keyed graph cache. One reference write, so racing updates are benign;
    /// readers validate it against the value's actual type.
    /// </summary>
    public ObjectGraph? LastValueGraph { get; set; }

    /// <summary>
    /// Gets or sets the graph of the last item type seen in the collection this member holds (inline cache)
    /// </summary>
    public ObjectGraph? LastItemGraph { get; set; }

    /// <summary>
    /// Gets or sets the graph of the last dictionary key type seen in the collection this member holds (inline cache)
    /// </summary>
    public ObjectGraph? LastKeyGraph { get; set; }

    /// <summary>
    /// Gets the cached names of the items, for a member holding a collection (created on first use)
    /// </summary>
    public ItemNameCache ItemNames => _itemNames ??= new ItemNameCache(Name);

    /// <summary>
    /// Gets the compiled getter of the member (created on first use), or null if the member cannot be read
    /// </summary>
    public Func<object, object?>? Getter =>
        !CanGet ? null : _getter ??= MemberAccessorEmitter.CreateGetter(MemberInfo);

    /// <summary>
    /// Gets the compiled setter of the member (created on first use), or null if the member cannot be written
    /// </summary>
    public Action<object, object?>? Setter =>
        !CanSet ? null : _setter ??= MemberAccessorEmitter.CreateSetter(MemberInfo);
}
