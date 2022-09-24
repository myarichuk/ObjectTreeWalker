namespace ObjectTreeWalker;

/// <summary>
/// An object to hold information about property/field
/// </summary>
internal readonly struct ObjectMemberInfo
{
    /// <summary>
    /// Gets member name
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// Gets member type
    /// </summary>
    public readonly MemberType MemberType;

    /// <summary>
    /// Gets parent object instance
    /// </summary>
    public readonly object Instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectMemberInfo"/> struct.
    /// </summary>
    /// <param name="name">member name</param>
    /// <param name="memberType">member type (property/field)</param>
    /// <param name="instance">instance of the object the member belongs</param>
    /// <exception cref="ArgumentNullException">any of constructor parameters is null</exception>
    public ObjectMemberInfo(string name, MemberType memberType, object instance)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MemberType = memberType;
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }
}