// Copyright (c) Michael Yarichuk. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace ObjectTreeWalker;

/// <summary>
/// The compiled cloner of an exact type <typeparamref name="T"/>. A generic static class is the fastest
/// possible per-type cache: reaching the cloner is a static field load, with no dictionary lookup.
/// </summary>
/// <typeparam name="T">the exact (runtime) type being cloned</typeparam>
/// <remarks>Only ever invoked with values whose runtime type is exactly <typeparamref name="T"/>.</remarks>
internal static class Cloner<T>
{
    /// <summary>
    /// Clones a value whose runtime type is exactly <typeparamref name="T"/>
    /// </summary>
    public static readonly Func<T, CloneContext, T> Clone = CloneEngine.BuildCloner<T>();

    /// <summary>
    /// For a root value whose clone is a plain shallow copy (no fields need cloning, not a collection),
    /// a delegate that makes that copy without setting up a clone context; otherwise null.
    /// </summary>
    public static readonly Func<T, T>? RootShallow = CloneEngine.BuildRootShallow<T>();
}
