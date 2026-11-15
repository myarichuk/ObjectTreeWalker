// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ObjectTreeWalker;

/// <summary>
/// A member accessor that allows to get and set member values during iteration
/// </summary>
public readonly struct MemberAccessor
{
    private readonly ObjectMemberInfo _memberInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemberAccessor"/> struct.
    /// </summary>
    /// <param name="memberInfo">Information about the object member (property/field) or collection item</param>
    internal MemberAccessor(in ObjectMemberInfo memberInfo)
    {
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
    /// Gets the list of property name and it's parents in-order
    /// </summary>
    public IReadOnlyList<PropertyPathItem> PropertyPath => _memberInfo.PropertyPath;

    /// <summary>
    /// Gets exposing raw data, needed for internal functionality
    /// </summary>
    internal ObjectMemberInfo RawInfo => _memberInfo;

    /// <summary>
    /// Accesses and fetches member value
    /// </summary>
    /// <returns>Member value, or null if the member cannot be read (a set-only property)</returns>
    public object? GetValue() => TryGetValue(out var value) ? value : null;

    /// <summary>
    /// Try fetch member value
    /// </summary>
    /// <param name="value">member value to be returned</param>
    /// <returns>true if the member could be read, false otherwise (a set-only property)</returns>
    public bool TryGetValue(out object? value)
    {
        // collection items carry their value directly (it may legitimately be null)
        if (_memberInfo.MemberType == MemberType.CollectionItem)
        {
            value = _memberInfo.Instance;
            return true;
        }

        var getter = _memberInfo.Node!.Getter;
        if (getter == null)
        {
            value = null;
            return false;
        }

        value = getter(_memberInfo.Instance!);
        return true;
    }

    /// <summary>
    /// Accesses and sets member value
    /// </summary>
    /// <param name="newValue">New member value (null sets a value-type member to its default)</param>
    /// <remarks>
    /// Members of structs are reached through boxed copies. After the member is set, the modified copy is
    /// written back up the chain of enclosing structs (and into the enclosing collection, for struct items),
    /// so the change is visible on the traversed object.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The accessor represents a collection item, the member has no setter, the value type does not match,
    /// or the change to a struct cannot be persisted (e.g. it is reached through a get-only struct property).
    /// </exception>
    public void SetValue(object? newValue)
    {
        // collection items carry no settable member (there is no member named 'Items[0]'):
        // fail loudly instead of silently ignoring the set
        if (_memberInfo.MemberType == MemberType.CollectionItem)
        {
            throw new InvalidOperationException(
                $"Cannot set value of collection item '{_memberInfo.Name}': collection items are read-only during traversal. Mutate the collection itself instead.");
        }

        var node = _memberInfo.Node!;
        var setter = node.Setter ?? throw new InvalidOperationException(
            $"Cannot set member '{_memberInfo.Name}' of {node.MemberInfo.DeclaringType}: it has no setter.");

        ObjectAccessor.AssignChecked(setter, node.Type, _memberInfo.Instance!, _memberInfo.Name, newValue);
        WriteBackEnclosingValueTypes();
    }

    private void WriteBackEnclosingValueTypes()
    {
        var current = _memberInfo;

        // current.Instance was just mutated. If it is a boxed struct it may be a detached copy of the value
        // held by its parent - store it back, and repeat for the parent, until a reference type is reached.
        while (current.Instance is { } instance && instance.GetType().IsValueType && current.Parent is { } parentRef)
        {
            var parent = parentRef.Value;
            if (parent.MemberType == MemberType.CollectionItem)
            {
                // a struct item enumerated out of a collection - its container is a reference
                // (or the root), so storing the item back is the last step
                parent.Slot?.Write(instance);
                return;
            }

            var parentNode = parent.Node!;

            // a member declared as a reference type (object, an interface) holds the very box we mutated
            if (!parentNode.Type.IsValueType)
            {
                return;
            }

            var parentSetter = parentNode.Setter ?? throw new InvalidOperationException(
                $"Cannot persist the change to '{_memberInfo.Name}': it was made to a copy of struct member '{parent.Name}', which has no setter.");

            parentSetter(parent.Instance!, instance);
            current = parent;
        }
    }
}
