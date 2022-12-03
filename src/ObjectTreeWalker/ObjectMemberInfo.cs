// ReSharper disable TooManyDependencies
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
    /// property and it's parents in-order
    /// </summary>
    public readonly IEnumerable<string> PropertyPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectMemberInfo"/> struct.
    /// </summary>
    /// <param name="name">member name</param>
    /// <param name="memberType">member type (property/field)</param>
    /// <param name="instance">instance of the object the member belongs</param>
    /// <param name="type">Type of the property/field</param>
    /// <param name="propertyPath">property and it's parents in-order</param>
    /// <exception cref="ArgumentNullException">any of constructor parameters is null</exception>
    public ObjectMemberInfo(
        string name,
        MemberType memberType,
        object instance,
        Type type,
        IEnumerable<string> propertyPath)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        PropertyPath = propertyPath ?? throw new ArgumentNullException(nameof(propertyPath));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MemberType = memberType;
        Instance = instance;
    }
}