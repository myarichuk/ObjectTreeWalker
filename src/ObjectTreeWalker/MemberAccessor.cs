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
    /// Exposing raw data, needed for internal functionality
    /// </summary>
    internal ObjectMemberInfo RawInfo => _memberInfo;

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
    /// <exception cref="InvalidOperationException">Failed to fetch parent property name. This is not supposed to happen and is likely an issue.</exception>
    public void SetValue(object newValue)
    {
        // struct properties get special treatment
        if (_memberInfo.Parent is
            {
                Value.Type: { IsPrimitive: false, IsValueType: true } // make sure our "client" is indeed a struct
            })
        {
            if (_memberInfo.Parent is
                {
                    Value.Parent: { } parentOfParentRef, // if the struct has a parent, this means it is embedded
                })
            {
                var parentPropertyName = parentOfParentRef.Value.PropertyPath.LastOrDefault();
                if (parentPropertyName == null)
                {
                    throw new InvalidOperationException(
                        "Failed to fetch parent property name. This is not supposed to happen and is likely a bug.");
                }

                _objectAccessor.TrySetValue(_memberInfo.Instance, _memberInfo.Name, newValue);

                var objectType = parentOfParentRef.Value.Instance.GetType();
                var parentOfParentRefAccessor = new ObjectAccessor(objectType);

                if (!parentOfParentRefAccessor.TryGetValue(parentOfParentRef.Value.Instance,
                        parentOfParentRef.Value.Name,
                        out var properParentInstance))
                {
                    throw new InvalidOperationException(
                        "Failed to set embedded struct value, this is not supposed to happen and is likely a bug.");
                }

                var properParentObjectAccessor = new ObjectAccessor(properParentInstance!.GetType());

                properParentObjectAccessor.TrySetValue(
                    properParentInstance,
                    _memberInfo.Parent.Value.Name,
                    _memberInfo.Instance);
            }
            else
            {
                _objectAccessor.TrySetValue(_memberInfo.Instance, _memberInfo.Name, newValue);

                var parentPropertyName = _memberInfo.Parent.Value.PropertyPath.LastOrDefault();
                if (parentPropertyName == null)
                {
                    throw new InvalidOperationException(
                        "Failed to fetch parent property name. This is not supposed to happen and is likely a bug.");
                }

                var parentInstance = _memberInfo.Parent.Value.Instance;
                var properParentObjectAccessor = new ObjectAccessor(parentInstance.GetType());

                properParentObjectAccessor.TrySetValue(parentInstance, parentPropertyName, _memberInfo.Instance);
            }
        }
        else
        {
            _objectAccessor.TrySetValue(_memberInfo.Instance, _memberInfo.Name, newValue);
        }
    }
}