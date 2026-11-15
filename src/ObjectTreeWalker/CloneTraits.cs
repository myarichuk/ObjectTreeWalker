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
/// Per-type facts about cloning, separate from <see cref="Cloner{T}"/> so that asking whether a type
/// needs cloning at all never builds a cloner for it.
/// </summary>
/// <typeparam name="T">the type</typeparam>
internal static class CloneTraits<T>
{
    /// <summary>
    /// Whether a value of <typeparamref name="T"/> can hold object references that need cloning.
    /// Static readonly: once initialized, the JIT treats it as a constant and folds the branches on it away.
    /// </summary>
    public static readonly bool NeedsClone = CloneEngine.NeedsDeepClone(typeof(T));
}
