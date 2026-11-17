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
    /// For collection items: the member graph of the item's runtime type (resolved when the item was reached)
    /// </summary>
    public readonly ObjectGraph? ItemGraph;

    /// <summary>
    /// The member's own (last) property path entry
    /// </summary>
    public readonly PropertyPathItem PathItem;

    /// <summary>
    /// The frame whose path precedes <see cref="PathItem"/>: the parent for members; for collection items
    /// the collection member's parent (an item entry <em>replaces</em> its collection's entry: 'Items' -> 'Items[3]')
    /// </summary>
    public readonly MemberFrame? PathParent;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectMemberInfo"/> struct for a property or a field.
    /// </summary>
    /// <param name="node">graph node of the member</param>
    /// <param name="instance">instance of the object the member belongs</param>
    /// <param name="parent">the frame the instance was reached through (null for root members)</param>
    public ObjectMemberInfo(ObjectGraphNode node, object instance, MemberFrame? parent)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Name = node.Name;
        Type = node.Type;
        MemberType = node.MemberType;
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Parent = parent;
        PathParent = parent;
        PathItem = new PropertyPathItem(node.Name);
        Slot = null;
        ItemGraph = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectMemberInfo"/> struct for a collection item.
    /// </summary>
    /// <param name="name">item name, e.g. <c>Items[3]</c></param>
    /// <param name="value">the item value</param>
    /// <param name="slot">where the item lives, if it can be written back</param>
    /// <param name="index">position of the item in its collection</param>
    /// <param name="isPartOfDictionary">whether the item is a dictionary key or value</param>
    /// <param name="parent">the collection member (null for a root collection)</param>
    /// <param name="itemGraph">member graph of the item's runtime type (null for a null item)</param>
    public ObjectMemberInfo(string name, object? value, int index, bool isPartOfDictionary, CollectionItemSlot? slot, MemberFrame? parent, ObjectGraph? itemGraph)
    {
        Node = null;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = value?.GetType() ?? typeof(object);
        MemberType = MemberType.CollectionItem;
        Instance = value;
        Slot = slot;
        Parent = parent;
        PathParent = parent?.Value.PathParent;
        PathItem = new PropertyPathItem(name, index, isPartOfDictionary);
        ItemGraph = itemGraph;
    }

    /// <summary>
    /// Gets the parent ObjectMemberInfo
    /// </summary>
    public MemberFrame? Parent { get; }

    /// <summary>
    /// Gets the property path of the member: its parents' entries, in order, then its own
    /// </summary>
    /// <remarks>Built on demand; the parents' prefix is cached by their frame.</remarks>
    public IReadOnlyList<PropertyPathItem> PropertyPath => BuildPropertyPath();

    /// <summary>
    /// Materializes the property path
    /// </summary>
    /// <returns>a new array holding the path</returns>
    internal PropertyPathItem[] BuildPropertyPath()
    {
        if (PathParent == null)
        {
            return new[] { PathItem };
        }

        var prefix = PathParent.Path;
        var path = new PropertyPathItem[prefix.Length + 1];
        Array.Copy(prefix, path, prefix.Length);
        path[prefix.Length] = PathItem;
        return path;
    }
}
