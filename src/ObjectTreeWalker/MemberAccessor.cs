namespace ObjectTreeWalker;

/// <summary>
/// A member accessor that allows to get and set member values during iteration
/// </summary>
public readonly struct MemberAccessor
{
    private readonly ObjectAccessor _objectAccessor;
    private readonly MemberInfo _memberInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemberAccessor"/> struct.
    /// </summary>
    /// <param name="memberInfo">Information about the object member (property/field)</param>
    /// <param name="objectAccessor">internal object accessor that allows accessing members</param>
    /// <exception cref="ArgumentNullException">Gets thrown if any of constructor parameters is null</exception>
    internal MemberAccessor(in MemberInfo memberInfo, ObjectAccessor objectAccessor)
    {
        _objectAccessor = objectAccessor ?? throw new ArgumentNullException(nameof(objectAccessor));
        _memberInfo = memberInfo;
    }

    /// <summary>
    /// Gets member name
    /// </summary>
    public string Name => _memberInfo.Name;

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
    /// Accesses and sets member value
    /// </summary>
    /// <param name="newValue">New member value</param>
    public void SetValue(object newValue) =>
        _objectAccessor.TrySetValue(_memberInfo.Instance, _memberInfo.Name, newValue);
}