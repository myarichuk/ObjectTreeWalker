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
/// Field-by-field cloner of an exact type <typeparamref name="T"/>, in emitted IL
/// </summary>
/// <typeparam name="T">the exact (runtime) type being cloned</typeparam>
internal static class FieldCloner<T>
{
    /// <summary>
    /// Shallow-copies the value, then replaces every field that can hold references with its clone
    /// </summary>
    public static readonly Func<T, CloneContext, T> Clone = CloneEngine.BuildFieldCloner<T>();
}
