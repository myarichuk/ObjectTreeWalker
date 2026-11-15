// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ObjectTreeWalker;

/// <summary>
/// An object to hold information about property/field (or a collection item) reached during traversal
/// </summary>
internal readonly struct ObjectMemberInfo
{
    /// <summary>
    /// Gets member name
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// Declared type of the property/field; the runtime type of the value for collection items
    /// </summary>
    public readonly Type Type;

    /// <summary>
    /// Gets member type
    /// </summary>
    public readonly MemberType MemberType;

    /// <summary>
    /// The instance owning the member; for collection items this is the item value itself (may be null)
    /// </summary>
    public readonly object? Instance;

    /// <summary>
    /// Graph node of the member (holds its compiled accessors); null for collection items
    /// </summary>
    public readonly ObjectGraphNode? Node;

    /// <summary>
    /// Where a collection item lives, so that changes to a value-type item can be written back; null for members
    /// </summary>
    public readonly CollectionItemSlot? Slot;

    /// <summary>
    /// property and it's parents in-order
    /// </summary>
    public readonly IReadOnlyList<PropertyPathItem> PropertyPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectMemberInfo"/> struct for a property or a field.
    /// </summary>
    /// <param name="node">graph node of the member</param>
    /// <param name="instance">instance of the object the member belongs</param>
    /// <param name="parent">the parent ObjectMemberInfo reference</param>
    /// <param name="propertyPath">property and it's parents in-order</param>
    public ObjectMemberInfo(ObjectGraphNode node, object instance, Ref<ObjectMemberInfo>? parent, IReadOnlyList<PropertyPathItem> propertyPath)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Name = node.Name;
        Type = node.Type;
        MemberType = node.MemberType;
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Parent = parent;
        PropertyPath = propertyPath ?? throw new ArgumentNullException(nameof(propertyPath));
        Slot = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectMemberInfo"/> struct for a collection item.
    /// </summary>
    /// <param name="name">item name, e.g. <c>Items[3]</c></param>
    /// <param name="value">the item value</param>
    /// <param name="slot">where the item lives, if it can be written back</param>
    /// <param name="parent">the collection member (null for a root collection)</param>
    /// <param name="propertyPath">property and it's parents in-order</param>
    public ObjectMemberInfo(string name, object? value, CollectionItemSlot? slot, Ref<ObjectMemberInfo>? parent, IReadOnlyList<PropertyPathItem> propertyPath)
    {
        Node = null;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = value?.GetType() ?? typeof(object);
        MemberType = MemberType.CollectionItem;
        Instance = value;
        Slot = slot;
        Parent = parent;
        PropertyPath = propertyPath ?? throw new ArgumentNullException(nameof(propertyPath));
    }

    /// <summary>
    /// Gets the parent ObjectMemberInfo
    /// </summary>
    public Ref<ObjectMemberInfo>? Parent { get; }
}
