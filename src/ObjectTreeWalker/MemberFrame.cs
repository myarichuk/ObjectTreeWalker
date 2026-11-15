// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ObjectTreeWalker;

/// <summary>
/// A member (or collection item) that traversal expanded: the parent of the members reached through it.
/// </summary>
/// <remarks>
/// Frames link a visited member back to the root (for struct write-back and property paths). A frame
/// caches its materialized property path, so all of its children share the prefix instead of each
/// building it again - and nothing is built at all unless someone reads a path.
/// </remarks>
internal sealed class MemberFrame
{
    private PropertyPathItem[]? _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemberFrame"/> class
    /// </summary>
    /// <param name="value">the expanded member</param>
    public MemberFrame(in ObjectMemberInfo value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the expanded member
    /// </summary>
    public ObjectMemberInfo Value { get; }

    /// <summary>
    /// Gets the property path of the expanded member (materialized once)
    /// </summary>
    public PropertyPathItem[] Path => _path ??= Value.BuildPropertyPath();
}
