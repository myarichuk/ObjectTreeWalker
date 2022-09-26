namespace ObjectTreeWalker;

/// <summary>
/// An object to hold information about property/field
/// </summary>
internal readonly struct ObjectMemberInfo
{
    /// <summary>
    /// Type of the property/field
    /// </summary>
    public readonly Type Type;

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
    /// <param name="type">Type of the property/field</param>
    /// <exception cref="ArgumentNullException">any of constructor parameters is null</exception>
    // ReSharper disable once TooManyDependencies
    public ObjectMemberInfo(string name, MemberType memberType, object instance, Type type)
    {
        Type = type;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MemberType = memberType;
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }
}