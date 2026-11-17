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
/// The deep clone engine: strategy selection, emitted per-type cloners and typed collection cloners.
/// </summary>
internal static class CloneEngine
{
    // past this many fields a single MemberwiseClone bulk copy beats per-field copying
    private const int MaxFieldWiseCopyFields = 32;

    private const BindingFlags DeclaredInstanceFields =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> DeepCloneFieldsCache = new();
    private static readonly ConcurrentDictionary<Type, bool> NeedsDeepCloneCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, CloneContext, object>> RuntimeClonerCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, CloneContext, object>> RuntimeFieldClonerCache = new();

    private static readonly Func<Type, Func<object, CloneContext, object>> RuntimeClonerFactory =
        static t => CreateBoxedDelegate(nameof(CloneBoxed), t);

    private static readonly Func<Type, Func<object, CloneContext, object>> RuntimeFieldClonerFactory =
        static t => CreateBoxedDelegate(nameof(FieldCloneBoxed), t);

    private static readonly MethodInfo CloneValueMethod = GetMethod(nameof(CloneValue));
    private static readonly MethodInfo GetOrAddSlotMethod = typeof(CloneContext).GetMethod(nameof(CloneContext.GetOrAddSlot))!;
    private static readonly MethodInfo GetTypeFromHandleMethod = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
    private static readonly MethodInfo GetUninitializedObjectMethod =
        typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.GetUninitializedObject))!;

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Clears the type-keyed caches. The generic static caches (<see cref="Cloner{T}"/> and the other generic per-type classes) live for the
    /// process lifetime, exactly like <c>EqualityComparer&lt;T&gt;.Default</c>.
    /// </summary>
    public static void ClearCache()
    {
        DeepCloneFieldsCache.Clear();
        NeedsDeepCloneCache.Clear();
        RuntimeClonerCache.Clear();
        RuntimeFieldClonerCache.Clear();
    }

    /// <summary>
    /// Clones a value statically typed as <typeparamref name="T"/> (a field, an element, ...)
    /// </summary>
    /// <typeparam name="T">static type of the value</typeparam>
    /// <param name="value">the value</param>
    /// <param name="context">clone context</param>
    /// <returns>the clone</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T CloneValue<T>(T value, CloneContext context)
    {
        if (!CloneTraits<T>.NeedsClone)
        {
            return value; // folded to a plain return for strings, primitives, blittable structs, ...
        }

        if (typeof(T).IsValueType)
        {
            return Cloner<T>.Clone(value, context);
        }

        if (value is null)
        {
            return value;
        }

        // the runtime type usually *is* the static type: a method-table compare and a direct call.
        // Otherwise (a subclass, an interface/object-typed field, a covariant array) dispatch dynamically.
        return value.GetType() == typeof(T)
            ? Cloner<T>.Clone(value, context)
            : (T)CloneDynamic(value, context)!;
    }

    /// <summary>
    /// Clones a value of statically unknown type
    /// </summary>
    /// <param name="source">the value</param>
    /// <param name="context">clone context</param>
    /// <returns>the clone</returns>
    public static object? CloneDynamic(object? source, CloneContext context)
    {
        if (source is null)
        {
            return null;
        }

        var type = source.GetType();
        if (IsImmutable(type) || IsSharedReference(source))
        {
            return source;
        }

        return RuntimeClonerCache.GetOrAdd(type, RuntimeClonerFactory)(source, context);
    }

    /// <summary>
    /// Clones <paramref name="source"/> field by field, whatever its type (used by collection fallbacks)
    /// </summary>
    /// <param name="source">the value</param>
    /// <param name="context">clone context</param>
    /// <returns>the clone</returns>
    public static object CloneFieldWise(object source, CloneContext context) =>
        RuntimeFieldClonerCache.GetOrAdd(source.GetType(), RuntimeFieldClonerFactory)(source, context);

    /// <summary>
    /// Whether values of <paramref name="type"/> can hold object references that need cloning
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>true if cloning has to visit values of this type</returns>
    public static bool NeedsDeepClone(Type type) =>
        NeedsDeepCloneCache.GetOrAdd(type, static t =>
        {
            if (IsImmutable(t) || IsSharedReferenceType(t) || t.IsPointer)
            {
                return false;
            }

            if (!t.IsValueType)
            {
                return true;
            }

            // a struct only needs a visit if one of its fields (recursively) can hold an object reference
            foreach (var field in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
            {
                if (NeedsDeepClone(field.FieldType))
                {
                    return true;
                }
            }

            return false;
        });

    /// <summary>
    /// Every instance field of the type (private fields of base classes included) that can hold an
    /// object reference. Deliberately independent of the traversal's ObjectEnumerator: cloning needs the
    /// raw storage of every type - collections, KeyValuePair backing fields and compiler-generated fields included.
    /// </summary>
    /// <param name="type">the type</param>
    /// <returns>fields to clone</returns>
    public static FieldInfo[] GetDeepCloneFields(Type type) =>
        DeepCloneFieldsCache.GetOrAdd(type, static t =>
        {
            var fields = new List<FieldInfo>();
            for (var current = t; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (var field in current.GetFields(DeclaredInstanceFields))
                {
                    if (NeedsDeepClone(field.FieldType))
                    {
                        fields.Add(field);
                    }
                }
            }

            return fields.ToArray();
        });

    /// <summary>
    /// Whether instances of <paramref name="type"/> are immutable, so never copied.
    /// Called for every dynamically dispatched value, so it is kept to cheap identity checks.
    /// </summary>
    /// <param name="type">the runtime type</param>
    /// <returns>true for immutable types</returns>
    public static bool IsImmutable(Type type) =>
        type.IsPrimitive ||
        type == typeof(string) ||
        type.IsEnum ||
        type == typeof(decimal) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan) ||
        type == typeof(Guid);

    /// <summary>
    /// Reflection objects are runtime singletons whose identity matters: a MemberwiseClone'd RuntimeType is
    /// a distinct, broken Type. Instance type checks are much cheaper than Type.IsAssignableFrom.
    /// </summary>
    /// <param name="instance">the value</param>
    /// <returns>true if the value must be shared, not cloned</returns>
    public static bool IsSharedReference(object instance) => instance is MemberInfo or Assembly or Module;

    /// <summary>
    /// Builds the cloner of an exact type: picks the strategy once, so cloning never re-decides it
    /// </summary>
    /// <typeparam name="T">the exact type</typeparam>
    /// <returns>the cloner</returns>
    internal static Func<T, CloneContext, T> BuildCloner<T>()
    {
        try
        {
            return SelectCloner<T>();
        }
        catch (Exception e)
        {
            // never let a failure escape a type initializer (that would turn into a permanent, opaque
            // TypeInitializationException): report the real error on every clone attempt instead
            return (_, _) => throw new NotSupportedException($"Cannot deep clone values of type {typeof(T)}: {e.Message}", e);
        }
    }

    /// <summary>
    /// Builds the context-free shallow root clone for <typeparamref name="T"/>, if applicable
    /// </summary>
    /// <typeparam name="T">the exact type</typeparam>
    /// <returns>the shallow cloner, or null</returns>
    internal static Func<T, T>? BuildRootShallow<T>()
    {
        var type = typeof(T);
        if (type.IsValueType || type.IsAbstract)
        {
            return null;
        }

        // A root holding nothing that needs cloning has no outgoing references to follow (so it cannot
        // cycle) and, being the root, cannot be aliased yet: a shallow copy is the complete deep clone, and
        // no clone context is needed at all.
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            return type == element.MakeArrayType() && !NeedsDeepClone(element)
                ? CreateTypedShallow<T>(nameof(CopyArray), element)
                : null;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (arguments.Any(NeedsDeepClone))
            {
                return null;
            }

            if (definition == typeof(List<>))
            {
                return CreateTypedShallow<T>(nameof(ShallowList), arguments);
            }

            if (definition == typeof(Dictionary<,>))
            {
                return CreateTypedShallow<T>(nameof(ShallowDictionary), arguments);
            }

            if (definition == typeof(HashSet<>))
            {
                return CreateTypedShallow<T>(nameof(ShallowHashSet), arguments);
            }
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) || GetDeepCloneFields(type).Length != 0)
        {
            return null;
        }

        var method = MemberAccessorEmitter.CreateMethod("ShallowClone", type, type, type);
        var il = method.GetILGenerator();
        var clone = il.DeclareLocal(type);
        EmitShallowCopy(il, type, clone, cloneFields: Array.Empty<FieldInfo>());
        il.Emit(OpCodes.Ldloc, clone);
        il.Emit(OpCodes.Ret);
        return (Func<T, T>)method.CreateDelegate(typeof(Func<T, T>));
    }

    /// <summary>
    /// Emits the field-by-field cloner of <typeparamref name="T"/>
    /// </summary>
    /// <typeparam name="T">the exact type</typeparam>
    /// <returns>the cloner</returns>
    /// <remarks>
    /// For a class:
    /// <code>
    /// ref var slot = ref ctx.GetOrAddSlot(source, out var found); if (found) return (T)slot;
    /// var clone = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    /// clone.plain1 = source.plain1;                 // for each reference-free field
    /// slot = clone;                                 // before the fields: self-references resolve to the clone
    /// clone.f1 = CloneValue&lt;F1&gt;(source.f1, ctx); // for each field that can hold references
    /// return clone;
    /// </code>
    /// For a struct the by-value argument is the copy, and its fields are replaced in place.
    /// The IL stores readonly fields directly, the way only the runtime's own serializers usually can.
    /// </remarks>
    internal static Func<T, CloneContext, T> BuildFieldCloner<T>()
    {
        var type = typeof(T);
        var fields = GetDeepCloneFields(type);

        // Dynamic methods are compiled once, fully optimized, and never re-JITted by tiered compilation.
        // Initializing the traits of every field type *before* that compilation lets the JIT see
        // CloneTraits<F>.NeedsClone as an initialized static readonly - a constant - when inlining CloneValue<F>.
        foreach (var field in fields)
        {
            EnsureTraitsInitialized(field.FieldType);
        }

        var method = MemberAccessorEmitter.CreateMethod("DeepClone", type, type, type, typeof(CloneContext));
        var il = method.GetILGenerator();

        if (type.IsValueType)
        {
            foreach (var field in fields)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, CloneValueMethod.MakeGenericMethod(field.FieldType));
                il.Emit(OpCodes.Stfld, field);
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            var found = il.DeclareLocal(typeof(bool));
            var slot = il.DeclareLocal(typeof(object).MakeByRefType());
            var clone = il.DeclareLocal(type);
            var notCloned = il.DefineLabel();

            // ref object slot = ref ctx.GetOrAddSlot(source, out found); if (found) return (T)slot;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca_S, found);
            il.Emit(OpCodes.Call, GetOrAddSlotMethod);
            il.Emit(OpCodes.Stloc, slot);
            il.Emit(OpCodes.Ldloc, found);
            il.Emit(OpCodes.Brfalse_S, notCloned);
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Ret);

            // the shallow copy touches no context, so the slot reference is still valid afterwards
            il.MarkLabel(notCloned);
            EmitShallowCopy(il, type, clone, fields);
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Ldloc, clone);
            il.Emit(OpCodes.Stind_Ref);

            foreach (var field in fields)
            {
                il.Emit(OpCodes.Ldloc, clone);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, CloneValueMethod.MakeGenericMethod(field.FieldType));
                il.Emit(OpCodes.Stfld, field);
            }

            il.Emit(OpCodes.Ldloc, clone);
            il.Emit(OpCodes.Ret);
        }

        return (Func<T, CloneContext, T>)method.CreateDelegate(typeof(Func<T, CloneContext, T>));
    }

    /// <summary>
    /// Allocates a copy of <paramref name="source"/> (null stays null) with a memmove
    /// </summary>
    /// <typeparam name="T">element type</typeparam>
    /// <param name="source">the array</param>
    /// <returns>the copy</returns>
    internal static T[]? CopyArray<T>(T[]? source)
    {
        if (source == null)
        {
            return null;
        }

        var copy = new T[source.Length];
        source.AsSpan().CopyTo(copy);
        return copy;
    }

    private static Func<T, CloneContext, T> SelectCloner<T>()
    {
        var type = typeof(T);

        if (!CloneTraits<T>.NeedsClone)
        {
            return static (value, _) => value;
        }

        if (type.IsAbstract)
        {
            // interfaces/abstract classes are never runtime types: only reachable through dynamic dispatch
            return static (value, context) => (T)CloneDynamic(value, context)!;
        }

        if (type.IsArray)
        {
            // only true single-dimension, zero-based arrays are T[] - not T[*] or T[,]
            if (type == type.GetElementType()!.MakeArrayType())
            {
                return CreateTyped<T>(nameof(CloneSzArray), type.GetElementType()!);
            }

            return static (value, context) => (T)ObjectExtensions.CloneCollection(value!, typeof(T), context);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(List<>))
            {
                return CreateTyped<T>(nameof(CloneList), arguments);
            }

            if (definition == typeof(Dictionary<,>))
            {
                return CreateTyped<T>(nameof(CloneDictionary), arguments);
            }

            if (definition == typeof(HashSet<>))
            {
                return CreateTyped<T>(nameof(CloneHashSet), arguments);
            }
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            // every other collection: rebuilt through its public API (comparers, subclass state, ...),
            // or field by field when it cannot be constructed
            return static (value, context) => (T)ObjectExtensions.CloneCollection(value!, typeof(T), context);
        }

        return FieldCloner<T>.Clone;
    }

    private static T[] CloneSzArray<T>(T[] source, CloneContext context)
    {
        ref var slot = ref context.GetOrAddSlot(source, out var found);
        if (found)
        {
            return (T[])slot!;
        }

        var clone = new T[source.Length];
        slot = clone;

        if (!CloneTraits<T>.NeedsClone)
        {
            // a memmove; Array.Clone() measured ~5x slower for small arrays (it goes through MemberwiseClone)
            source.AsSpan().CopyTo(clone);
            return clone;
        }

        for (var i = 0; i < source.Length; i++)
        {
            clone[i] = CloneValue(source[i], context);
        }

        return clone;
    }

    private static List<T> CloneList<T>(List<T> source, CloneContext context)
    {
        ref var slot = ref context.GetOrAddSlot(source, out var found);
        if (found)
        {
            return (List<T>)slot!;
        }

        var count = source.Count;
        var clone = new List<T>(count);
        slot = clone;

#if NET8_0_OR_GREATER
        // write straight into the clone's backing array: no per-item Add, no interface hops
        CollectionsMarshal.SetCount(clone, count);
        var target = CollectionsMarshal.AsSpan(clone);
        var items = CollectionsMarshal.AsSpan(source);
        if (!CloneTraits<T>.NeedsClone)
        {
            items.CopyTo(target);
            return clone;
        }

        for (var i = 0; i < items.Length; i++)
        {
            target[i] = CloneValue(items[i], context);
        }
#else
        if (!CloneTraits<T>.NeedsClone)
        {
            clone.AddRange(source);
            return clone;
        }

        for (var i = 0; i < count; i++)
        {
            clone.Add(CloneValue(source[i], context));
        }
#endif
        return clone;
    }

    private static Dictionary<TKey, TValue> CloneDictionary<TKey, TValue>(Dictionary<TKey, TValue> source, CloneContext context)
        where TKey : notnull
    {
        ref var slot = ref context.GetOrAddSlot(source, out var found);
        if (found)
        {
            return (Dictionary<TKey, TValue>)slot!;
        }

        Dictionary<TKey, TValue> clone;
        // the raw copy and the value patcher are validated independently: both must be available
        if (!CloneTraits<TKey>.NeedsClone &&
            RawHashCollection<Dictionary<TKey, TValue>>.Copy is { } rawCopy &&
            (!CloneTraits<TValue>.NeedsClone || DictionaryValuePatcher<TKey, TValue>.Patch != null))
        {
            // keys stay as they are, so the hash structure (buckets, entries, chains) is copied verbatim
            // and only the values are replaced, in place - no constructor, no hashing, no inserts
            clone = rawCopy(source);
            slot = clone;
            if (CloneTraits<TValue>.NeedsClone)
            {
                DictionaryValuePatcher<TKey, TValue>.Patch!(clone, context);
            }

            return clone;
        }

        clone = new Dictionary<TKey, TValue>(source.Count, source.Comparer);
        slot = clone;
        foreach (var pair in source)
        {
            clone[CloneValue(pair.Key, context)] = CloneValue(pair.Value, context);
        }

        return clone;
    }

    private static HashSet<T> CloneHashSet<T>(HashSet<T> source, CloneContext context)
    {
        ref var slot = ref context.GetOrAddSlot(source, out var found);
        if (found)
        {
            return (HashSet<T>)slot!;
        }

        HashSet<T> clone;
        if (!CloneTraits<T>.NeedsClone)
        {
            clone = RawHashCollection<HashSet<T>>.Copy?.Invoke(source) ?? new HashSet<T>(source, source.Comparer);
            slot = clone;
            return clone;
        }

        // cloned items are new identities: they must be hashed again
        clone = new HashSet<T>(source.Comparer);
        slot = clone;
        foreach (var item in source)
        {
            clone.Add(CloneValue(item, context));
        }

        return clone;
    }

    private static List<T> ShallowList<T>(List<T> source)
    {
#if NET8_0_OR_GREATER
        var clone = new List<T>(source.Count);
        CollectionsMarshal.SetCount(clone, source.Count);
        CollectionsMarshal.AsSpan(source).CopyTo(CollectionsMarshal.AsSpan(clone));
        return clone;
#else
        return new List<T>(source);
#endif
    }

    private static Dictionary<TKey, TValue> ShallowDictionary<TKey, TValue>(Dictionary<TKey, TValue> source)
        where TKey : notnull =>
        RawHashCollection<Dictionary<TKey, TValue>>.Copy?.Invoke(source) ?? new Dictionary<TKey, TValue>(source, source.Comparer);

    private static HashSet<T> ShallowHashSet<T>(HashSet<T> source) =>
        RawHashCollection<HashSet<T>>.Copy?.Invoke(source) ?? new HashSet<T>(source, source.Comparer);

    private static Func<T, T> CreateTypedShallow<T>(string methodName, params Type[] typeArguments) =>
        (Func<T, T>)GetMethod(methodName)
            .MakeGenericMethod(typeArguments)
            .CreateDelegate(typeof(Func<T, T>));

    /// <summary>
    /// Emits <c>clone = shallow copy of arg0</c>, leaving the fields in <paramref name="cloneFields"/> unset
    /// (the caller assigns their clones)
    /// </summary>
    /// <param name="il">IL generator</param>
    /// <param name="type">exact class being copied</param>
    /// <param name="clone">local receiving the copy</param>
    /// <param name="cloneFields">fields the caller will assign</param>
    /// <remarks>
    /// <c>object.MemberwiseClone()</c> measured ~4x slower than allocating the object uninitialized and
    /// copying its fields with typed loads/stores (47 ns vs 12 ns for a two-field class on .NET 10): it goes
    /// through a generic size lookup and a bulk copy with write barriers. It still wins for wide objects, and
    /// is the only correct option for explicit layouts (overlapping fields).
    /// </remarks>
    private static void EmitShallowCopy(ILGenerator il, Type type, LocalBuilder clone, FieldInfo[] cloneFields)
    {
        var allFields = GetAllInstanceFields(type);
        if (type.IsExplicitLayout || allFields.Length > MaxFieldWiseCopyFields)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, MemberwiseCloneMethod);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Stloc, clone);
            return;
        }

        il.Emit(OpCodes.Ldtoken, type); // GetTypeFromHandle(ldtoken) is folded to a constant by the JIT
        il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
        il.Emit(OpCodes.Call, GetUninitializedObjectMethod);
        il.Emit(OpCodes.Castclass, type);
        il.Emit(OpCodes.Stloc, clone);

        foreach (var field in allFields)
        {
            if (Array.IndexOf(cloneFields, field) >= 0)
            {
                continue;
            }

            il.Emit(OpCodes.Ldloc, clone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Stfld, field);
        }
    }

    private static FieldInfo[] GetAllInstanceFields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            fields.AddRange(current.GetFields(DeclaredInstanceFields));
        }

        return fields.ToArray();
    }

    private static void EnsureTraitsInitialized(Type type) =>
        RuntimeHelpers.RunClassConstructor(typeof(CloneTraits<>).MakeGenericType(type).TypeHandle);

    private static object CloneBoxed<T>(object source, CloneContext context) => Cloner<T>.Clone((T)source, context)!;

    private static object FieldCloneBoxed<T>(object source, CloneContext context) => FieldCloner<T>.Clone((T)source, context)!;

    private static Func<T, CloneContext, T> CreateTyped<T>(string methodName, params Type[] typeArguments) =>
        (Func<T, CloneContext, T>)GetMethod(methodName)
            .MakeGenericMethod(typeArguments)
            .CreateDelegate(typeof(Func<T, CloneContext, T>));

    private static Func<object, CloneContext, object> CreateBoxedDelegate(string methodName, Type type) =>
        (Func<object, CloneContext, object>)GetMethod(methodName)
            .MakeGenericMethod(type)
            .CreateDelegate(typeof(Func<object, CloneContext, object>));

    private static bool IsSharedReferenceType(Type type) =>
        typeof(MemberInfo).IsAssignableFrom(type) ||
        typeof(Assembly).IsAssignableFrom(type) ||
        typeof(Module).IsAssignableFrom(type);

    private static MethodInfo GetMethod(string name) =>
        typeof(CloneEngine).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
}
