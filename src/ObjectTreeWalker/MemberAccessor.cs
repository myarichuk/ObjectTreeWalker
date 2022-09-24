namespace ObjectTreeWalker;

/// <summary>
/// A member accessor that allows to get and set member values during iteration
/// </summary>
public readonly struct MemberAccessor
{
    private readonly ObjectAccessor _objectAccessor;
    private readonly object _object;
    private readonly string _memberName;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemberAccessor"/> struct.
    /// </summary>
    /// <param name="memberName">Member (field/property) name</param>
    /// <param name="obj">parent object instance</param>
    /// <param name="objectAccessor">internal object accessor that allows accessing members</param>
    /// <exception cref="ArgumentNullException">Gets thrown if any of constructor parameters is null</exception>
    internal MemberAccessor(string memberName, object obj, ObjectAccessor objectAccessor, MemberType memberType)
    {
        _memberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
        _object = obj ?? throw new ArgumentNullException(nameof(obj));
        _objectAccessor = objectAccessor ?? throw new ArgumentNullException(nameof(objectAccessor));
        MemberType = memberType;
    }

    /// <summary>
    /// Gets member name
    /// </summary>
    public string Name => _memberName;

    /// <summary>
    /// Gets member type (property/field)
    /// </summary>
    public MemberType MemberType { get; }

    /// <summary>
    /// Accesses and fetches member value
    /// </summary>
    /// <returns>Member value</returns>
    public object? GetValue() =>
        !_objectAccessor.TryGetValue(_object, _memberName, out var value) ?
            null : value;

    /// <summary>
    /// Accesses and sets member value
    /// </summary>
    /// <param name="newValue">New member value</param>
    public void SetValue(object newValue) =>
        _objectAccessor.TrySetValue(_object, _memberName, newValue);
}