namespace ObjectTreeWalker;

/// <summary>
/// A member accessor that allows to get and set member values during iteration
/// </summary>
public readonly struct MemberAccessor
{
    private readonly ObjectAccessor _objectAccessor;
    private readonly ObjectMemberInfo _memberInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemberAccessor"/> struct.
    /// </summary>
    /// <param name="memberInfo">Information about the object member (property/field)</param>
    /// <param name="objectAccessor">internal object accessor that allows accessing members</param>
    /// <exception cref="ArgumentNullException">Gets thrown if any of constructor parameters is null</exception>
    internal MemberAccessor(in ObjectMemberInfo memberInfo, ObjectAccessor objectAccessor)
    {
        _objectAccessor = objectAccessor ?? throw new ArgumentNullException(nameof(objectAccessor));
        _memberInfo = memberInfo;
    }

    /// <summary>
    /// Gets member name
    /// </summary>
    public string Name => _memberInfo.Name;

    /// <summary>
    /// Gets the type of the property/field
    /// </summary>
    public Type Type => _memberInfo.Type;

    /// <summary>
    /// Gets member type (property/field)
    /// </summary>
    public MemberType MemberType => _memberInfo.MemberType;

    /// <summary>
    /// Accesses and fetches member value
    /// </summary>
    /// <returns>Member value</returns>
    public object? GetValue() =>
        !_objectAccessor.TryGetValue(_memberInfo.Instance, _memberInfo.Name, out var value) ?
            null : value;

    /// <summary>
    /// Try fetch member value
    /// </summary>
    /// <param name="value">member value to be returned</param>
    /// <returns>true if the member is found, false otherwise</returns>
    public bool TryGetValue(out object? value) =>
        _objectAccessor.TryGetValue(_memberInfo.Instance, _memberInfo.Name, out value);

    /// <summary>
    /// Gets the list of property name and it's parents in-order
    /// </summary>
    public IEnumerable<string> PropertyPath => _memberInfo.PropertyPath;

    /// <summary>
    /// Accesses and sets member value
    /// </summary>
    /// <param name="newValue">New member value</param>
    public void SetValue(object newValue) =>
        _objectAccessor.TrySetValue(_memberInfo.Instance, _memberInfo.Name, newValue);
}