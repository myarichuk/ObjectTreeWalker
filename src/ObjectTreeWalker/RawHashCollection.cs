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
/// A raw copy of a BCL hash collection (<see cref="Dictionary{TKey, TValue}"/>, <see cref="HashSet{T}"/>):
/// the object is allocated uninitialized, its storage arrays are copied with a memmove, scalar fields and
/// the comparer are copied as they are, and lazily created views (Keys/Values) are left null.
/// </summary>
/// <typeparam name="TCollection">the exact collection type</typeparam>
/// <remarks>
/// Valid only when the elements/keys are not cloned (so every stored hash code and chain stays correct).
/// Depends on private BCL layout: the copier is built only if the type's instance fields are <em>exactly</em>
/// the known set - any unknown field (e.g. one added by a future runtime) disables it, and cloning falls back
/// to the public API.
/// </remarks>
internal static class RawHashCollection<TCollection>
    where TCollection : class
{
    /// <summary>
    /// The copier, or null when the collection layout is not the expected one
    /// </summary>
    public static readonly Func<TCollection, TCollection>? Copy = Build();

    private static Func<TCollection, TCollection>? Build()
    {
#if NET8_0_OR_GREATER
        try
        {
            // locals, not static fields: static initializers run in textual order, and Copy (above) is built first
            string[] storageArrays = { "_buckets", "_entries" };
            string[] scalars = { "_fastModMultiplier", "_count", "_freeList", "_freeCount", "_version", "_comparer" };
            string[] lazyViews = { "_keys", "_values" };

            var type = typeof(TCollection);
            if (type.BaseType != typeof(object))
            {
                return null;
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length == 0 ||
                !fields.All(f => storageArrays.Contains(f.Name) || scalars.Contains(f.Name) || lazyViews.Contains(f.Name)) ||
                !storageArrays.All(name => fields.Any(f => f.Name == name && f.FieldType.IsSZArray)))
            {
                return null;
            }

            var method = MemberAccessorEmitter.CreateMethod("RawCopy", type, type, type);
            var il = method.GetILGenerator();
            var clone = il.DeclareLocal(type);

            il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
            il.Emit(OpCodes.Call, typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.GetUninitializedObject))!);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Stloc, clone);

            var copyArray = typeof(CloneEngine).GetMethod(nameof(CloneEngine.CopyArray), BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var field in fields)
            {
                if (lazyViews.Contains(field.Name))
                {
                    continue; // views reference their owner: leave them null, they are recreated on demand
                }

                il.Emit(OpCodes.Ldloc, clone);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                if (storageArrays.Contains(field.Name))
                {
                    il.Emit(OpCodes.Call, copyArray.MakeGenericMethod(field.FieldType.GetElementType()!));
                }

                il.Emit(OpCodes.Stfld, field);
            }

            il.Emit(OpCodes.Ldloc, clone);
            il.Emit(OpCodes.Ret);
            return (Func<TCollection, TCollection>)method.CreateDelegate(typeof(Func<TCollection, TCollection>));
        }
        catch (Exception)
        {
            return null;
        }
#else
        return null;
#endif
    }
}
