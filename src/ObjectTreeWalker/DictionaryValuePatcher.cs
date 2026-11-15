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
/// Replaces every value of a <see cref="Dictionary{TKey, TValue}"/> with its clone, in place, by walking the
/// dictionary's private entry array in emitted IL.
/// </summary>
/// <typeparam name="TKey">key type</typeparam>
/// <typeparam name="TValue">value type</typeparam>
/// <remarks>
/// This depends on the BCL's private layout (<c>_entries</c>, <c>_count</c>, <c>Entry.value</c>), so it is
/// enabled only where that layout is known, and only after verifying the fields reflectively; otherwise
/// <see cref="Patch"/> is null and the dictionary is rebuilt through its public API.
/// Every entry in <c>_entries[0.._count)</c> is patched: live entries get their value cloned, and entries on
/// the free list hold <c>default</c> (Remove clears values that contain references), which clones to itself.
/// </remarks>
internal static class DictionaryValuePatcher<TKey, TValue>
    where TKey : notnull
{
    /// <summary>
    /// The patcher, or null when the dictionary layout is not the expected one
    /// </summary>
    public static readonly Action<Dictionary<TKey, TValue>, CloneContext>? Patch = Build();

    private static Action<Dictionary<TKey, TValue>, CloneContext>? Build()
    {
#if NET8_0_OR_GREATER
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var dictionaryType = typeof(Dictionary<TKey, TValue>);
            var entriesField = dictionaryType.GetField("_entries", flags);
            var countField = dictionaryType.GetField("_count", flags);
            var entryType = entriesField?.FieldType.GetElementType();
            var valueField = entryType?.GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (entriesField == null || countField?.FieldType != typeof(int) ||
                entryType is not { IsValueType: true } || !entriesField.FieldType.IsSZArray ||
                valueField?.FieldType != typeof(TValue))
            {
                return null;
            }

            var method = MemberAccessorEmitter.CreateMethod(
                "PatchValues", dictionaryType, typeof(void), dictionaryType, typeof(CloneContext));
            var il = method.GetILGenerator();
            var entries = il.DeclareLocal(entriesField.FieldType);
            var count = il.DeclareLocal(typeof(int));
            var index = il.DeclareLocal(typeof(int));
            var loop = il.DefineLabel();
            var condition = il.DefineLabel();

            // var entries = dictionary._entries; var count = dictionary._count;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, entriesField);
            il.Emit(OpCodes.Stloc, entries);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, countField);
            il.Emit(OpCodes.Stloc, count);
            il.Emit(OpCodes.Br_S, condition);

            // ref var entry = ref entries[i]; entry.value = CloneValue(entry.value, context);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, entries);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldelema, entryType);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldfld, valueField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(CloneEngine).GetMethod(nameof(CloneEngine.CloneValue))!.MakeGenericMethod(typeof(TValue)));
            il.Emit(OpCodes.Stfld, valueField);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);

            il.MarkLabel(condition);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Blt_S, loop);
            il.Emit(OpCodes.Ret);

            RuntimeHelpers.RunClassConstructor(typeof(CloneTraits<TValue>).TypeHandle);
            return (Action<Dictionary<TKey, TValue>, CloneContext>)method.CreateDelegate(typeof(Action<Dictionary<TKey, TValue>, CloneContext>));
        }
        catch (Exception)
        {
            return null; // unexpected layout: use the public API instead
        }
#else
        return null;
#endif
    }
}
