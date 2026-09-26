using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sigil;

namespace ObjectTreeWalker
{
    /// <summary>
    /// Extension methods for object operations like deep cloning.
    /// </summary>
    public static class ObjectExtensions
    {
        private static readonly ObjectEnumerator DefaultEnumerator = new ObjectEnumerator(new ObjectEnumerator.Settings { IgnoreCompilerGenerated = false });

        // Object.MemberwiseClone() is not virtual, so a single emitted delegate works for every type -
        // it does a fast, allocation-minimal shallow field copy (including value-type fields) without
        // going through FormatterServices/RuntimeHelpers.GetUninitializedObject + manual field-by-field sets.
        private static readonly Func<object, object> MemberwiseCloneFunc = CreateMemberwiseCloneFunc();

        // Per-type cache of the fields that actually need to be visited during a deep clone (i.e. fields whose
        // value can contain object references). Plain value-type fields (int, DateTime, an enum, a struct made
        // entirely of such fields, etc.) are already correctly copied by MemberwiseClone and can be skipped
        // entirely, avoiding the box/unbox round trip that a generic get/set accessor would otherwise incur.
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<ObjectGraphNode>> DeepCloneFieldsCache = new();

        private static readonly ConcurrentDictionary<Type, bool> NeedsDeepCloneCache = new();

        private static readonly ConcurrentDictionary<Type, Func<object>> CollectionFactoryCache = new();

        // Collections that expose a (int capacity) constructor get pre-sized to the source's item count,
        // so List/Dictionary/HashSet clones don't repeatedly regrow (and re-allocate) their backing arrays
        // as items are added one at a time.
        private static readonly ConcurrentDictionary<Type, Func<int, object>?> CollectionCapacityFactoryCache = new();

        // The collection's comparer (Dictionary<TKey,TValue>.Comparer, HashSet<T>.Comparer, ...), if any.
        // Clones reuse the source's comparer instead of silently falling back to the default one.
        private static readonly ConcurrentDictionary<Type, PropertyInfo?> CollectionComparerPropertyCache = new();

        // (capacity, comparer) -> collection factories for comparer-aware construction.
        private static readonly ConcurrentDictionary<Type, Func<int, object?, object>?> CollectionSizedComparerFactoryCache = new();

        // (comparer) -> collection factories for comparer-aware construction without pre-sizing.
        private static readonly ConcurrentDictionary<Type, Func<object?, object>?> CollectionComparerFactoryCache = new();

        // Compiled Add(item) delegate per collection type, replacing MethodInfo.Invoke reflection (which boxes
        // its argument array on every call) for the generic ICollection<T> cloning fallback path.
        private static readonly ConcurrentDictionary<Type, Action<object, object?>?> CollectionAdderCache = new();

        // The element type of a collection's IEnumerable<T>, used to decide whether its items need visiting
        // at all during a clone (see FastCollectionCopyCache below).
        private static readonly ConcurrentDictionary<Type, Type?> CollectionElementTypeCache = new();

        // When a collection's element type needs no deep clone (int, string, an enum, a struct with no
        // reference fields, etc.), its items can be bulk-copied straight from source to clone via a compiled,
        // strongly-typed enumerate-and-Add loop - skipping both the per-item DeepCloneInternal call *and* the
        // object-boxing that iterating through the non-generic IList/IEnumerable interface would incur.
        private static readonly ConcurrentDictionary<Type, Action<object, object>?> FastCollectionCopyCache = new();

        // Per-type compiled cloning strategy: a delegate that directly gets/sets each field needing a deep
        // clone via Expression Trees, instead of going through ObjectAccessor's string-keyed dictionary lookups
        // and box/unbox round trips on every field, every call. Built once per type and cached.
        private static readonly ConcurrentDictionary<Type, Func<object, Dictionary<object, object>, object>> TypedClonerCache = new();

        private static readonly MethodInfo DeepCloneInternalMethod =
            typeof(ObjectExtensions).GetMethod(nameof(DeepCloneInternal), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo SetFieldValueMethod =
            typeof(ObjectExtensions).GetMethod(nameof(SetFieldValue), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static void SetFieldValue(object obj, FieldInfo field, object? value) => field.SetValue(obj, value);

        private static readonly MethodInfo ReferenceEqualsMethod =
            typeof(object).GetMethod(nameof(ReferenceEquals), BindingFlags.Public | BindingFlags.Static)!;

        private static readonly PropertyInfo VisitedIndexerProperty =
            typeof(Dictionary<object, object>).GetProperty("Item")!;

        // the comparer is stateless, so a single shared instance avoids allocating one on every DeepClone<T>() call
        private static readonly ReferenceEqualityComparer SharedReferenceEqualityComparer = new();

        // Per-thread pool of "visited" dictionaries. A Dictionary<,> never shrinks its backing arrays on
        // Clear(), so reusing one across DeepClone<T>() calls on the same thread turns what would otherwise be
        // repeated bucket/entry-array allocations (as the dictionary grows back to its previous size) into a
        // single Clear() with zero allocation, once the pool has warmed up.
        [ThreadStatic]
        private static Stack<Dictionary<object, object>>? visitedPool;

        /// <summary>
        /// Clears all internal DeepClone caches shared by every thread.
        /// </summary>
        /// <remarks>
        /// The caches are keyed by type, so this is only needed to reclaim memory in
        /// long-lived hosts that see many dynamic or generic types — never for correctness.
        /// See also <see cref="ObjectEnumerator.ClearCache"/> and <see cref="ObjectAccessor.ClearCache"/>.
        /// </remarks>
        public static void ClearCache()
        {
            DeepCloneFieldsCache.Clear();
            NeedsDeepCloneCache.Clear();
            CollectionFactoryCache.Clear();
            CollectionCapacityFactoryCache.Clear();
            CollectionComparerPropertyCache.Clear();
            CollectionSizedComparerFactoryCache.Clear();
            CollectionComparerFactoryCache.Clear();
            CollectionAdderCache.Clear();
            CollectionElementTypeCache.Clear();
            FastCollectionCopyCache.Clear();
            TypedClonerCache.Clear();
        }

        /// <summary>
        /// Creates a deep clone of the object.
        /// </summary>
        /// <typeparam name="T">The type of the object.</typeparam>
        /// <param name="source">The object to clone.</param>
        /// <returns>A deep clone of the object.</returns>
        public static T DeepClone<T>(this T source)
        {
            // box once (a no-op for reference types) and use that single boxed reference throughout,
            // so a primitive/string/decimal/DateTime/enum clone never allocates the visited dictionary at all
            object? boxed = source;
            if (boxed is null)
            {
                return source;
            }

            var type = boxed.GetType();
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type.IsEnum)
            {
                return source;
            }

            // a root that isn't an array/collection and has no fields needing a deep clone can't cycle back to
            // itself (it has no outgoing references to walk) and, being the root, can't yet be aliased by
            // anything else in this call - so the visited-tracking dictionary can be skipped entirely for it.
            if (!type.IsArray && boxed is not IEnumerable && GetDeepCloneFields(type).Count == 0)
            {
                return (T)MemberwiseCloneFunc(boxed);
            }

            var visited = RentVisited();
            try
            {
                return (T)DeepCloneInternal(boxed, visited)!;
            }
            finally
            {
                ReturnVisited(visited);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Dictionary<object, object> RentVisited()
        {
            var pool = visitedPool ??= new Stack<Dictionary<object, object>>();
            return pool.Count > 0 ? pool.Pop() : new Dictionary<object, object>(SharedReferenceEqualityComparer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnVisited(Dictionary<object, object> visited)
        {
            var pool = visitedPool!;
            if (pool.Count < 8)
            {
                visited.Clear();
                pool.Push(visited);
            }
        }

        private static object? DeepCloneInternal(object? source, Dictionary<object, object> visited)
        {
            if (source is null)
            {
                return null;
            }

            var type = source.GetType();

            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type.IsEnum)
            {
                return source;
            }

            // check for cyclic references
            if (!type.IsValueType && visited.TryGetValue(source, out var existingClone))
            {
                return existingClone;
            }

            if (type.IsArray)
            {
                return CloneArray((Array)source, visited);
            }

            if (source is IDictionary sourceDictionary)
            {
                return CloneDictionary(sourceDictionary, type, visited);
            }

            if (source is IList sourceList)
            {
                return CloneList(sourceList, type, visited);
            }

            if (source is IEnumerable sourceEnumerable)
            {
                if (TryGetCollectionAdder(type, out var adder))
                {
                    return CloneCollection(sourceEnumerable, type, adder!, visited);
                }

                if (type.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                {
                    throw new NotSupportedException(
                        $"Cannot deep clone enumerable of type {type.AssemblyQualifiedName}: compiler-generated iterators and generator outputs cannot be reconstructed. Materialize them into a List<T> or array first.");
                }
            }

            return CloneObject(source, type, visited);
        }

        private static object CloneArray(Array array, Dictionary<object, object> visited)
        {
            var cloneArray = (Array)array.Clone();

            // arrays are always reference types
            visited[array] = cloneArray;

            // Array.Clone() already produced a correct, allocation-free bitwise copy of every element. If the
            // element type can't hold an object reference (int, an enum, a struct with no reference fields,
            // etc.), that bitwise copy already *is* the deep clone - walking it element by element would only
            // re-box and re-unbox each value for no benefit.
            var elementType = array.GetType().GetElementType()!;
            if (!NeedsDeepClone(elementType))
            {
                return cloneArray;
            }

            if (array.Rank == 1)
            {
                // rank-1 arrays may have a non-zero lower bound (Array.CreateInstance);
                // indices must be offset or GetValue/SetValue throw
                var lowerBound = array.GetLowerBound(0);
                for (int i = 0; i < array.Length; i++)
                {
                    var item = array.GetValue(i + lowerBound);
                    if (item != null)
                    {
                        var clonedItem = DeepCloneInternal(item, visited);
                        cloneArray.SetValue(clonedItem, i + lowerBound);
                    }
                }
            }
            else
            {
                // multidimensional array handling
                var indices = new int[array.Rank];
                CopyMultidimensionalArray(array, cloneArray, indices, 0, visited);
            }

            return cloneArray;
        }

        private static object CloneDictionary(IDictionary source, Type type, Dictionary<object, object> visited)
        {
            var clone = (IDictionary)CreateCollectionInstance(type, source.Count, GetCollectionComparerValue(type, source));
            RegisterVisited(source, clone, type, visited);

            if (TryGetFastCollectionCopy(type, out var fastCopy))
            {
                fastCopy!(source, clone);
                CopyCollectionExtraState(source, clone, type, visited);
                return clone;
            }

            foreach (DictionaryEntry entry in source)
            {
                var clonedKey = DeepCloneInternal(entry.Key, visited);
                var clonedValue = DeepCloneInternal(entry.Value, visited);
                clone[clonedKey!] = clonedValue;
            }

            CopyCollectionExtraState(source, clone, type, visited);

            return clone;
        }

        private static object CloneList(IList source, Type type, Dictionary<object, object> visited)
        {
            var clone = (IList)CreateCollectionInstance(type, source.Count, GetCollectionComparerValue(type, source));
            RegisterVisited(source, clone, type, visited);

            if (TryGetFastCollectionCopy(type, out var fastCopy))
            {
                fastCopy!(source, clone);
                CopyCollectionExtraState(source, clone, type, visited);
                return clone;
            }

            foreach (var item in source)
            {
                clone.Add(DeepCloneInternal(item, visited));
            }

            CopyCollectionExtraState(source, clone, type, visited);

            return clone;
        }

        private static object CloneCollection(IEnumerable source, Type type, Action<object, object?> adder, Dictionary<object, object> visited)
        {
            var comparer = GetCollectionComparerValue(type, source);
            var clone = source is ICollection sourceCollection
                ? CreateCollectionInstance(type, sourceCollection.Count, comparer)
                : CreateCollectionInstance(type, comparer);
            RegisterVisited(source, clone, type, visited);

            if (TryGetFastCollectionCopy(type, out var fastCopy))
            {
                fastCopy!(source, clone);
                CopyCollectionExtraState(source, clone, type, visited);
                return clone;
            }

            foreach (var item in source)
            {
                adder(clone, DeepCloneInternal(item, visited));
            }

            CopyCollectionExtraState(source, clone, type, visited);

            return clone;
        }

        private static object CloneObject(object source, Type type, Dictionary<object, object> visited)
        {
            // MemberwiseClone already copies every field bit-for-bit (including value-type fields, which
            // require no further work at all), so only fields that can hold object references need revisiting.
            var fieldsToClone = GetDeepCloneFields(type);
            if (fieldsToClone.Count == 0)
            {
                var clone = MemberwiseCloneFunc(source);
                RegisterVisited(source, clone, type, visited);
                return clone;
            }

            // readonly fields cannot be assigned from a compiled expression tree when the clone
            // itself is a value type (the assignment target would be a copy); fall back to a
            // reflection loop for those structs (classes are handled inside the typed cloner).
            if (type.IsValueType && fieldsToClone.Any(static node => ((FieldInfo)node.MemberInfo).IsInitOnly))
            {
                return CloneObjectViaReflection(source, type, visited);
            }

            // types with fields to revisit get a compiled per-type delegate that accesses those fields
            // directly (no string-keyed dictionary lookup, no box/unbox round trip through ObjectAccessor).
            return GetTypedCloner(type)(source, visited);
        }

        private static object CloneObjectViaReflection(object source, Type type, Dictionary<object, object> visited)
        {
            var clone = MemberwiseCloneFunc(source);
            RegisterVisited(source, clone, type, visited);

            foreach (var node in GetDeepCloneFields(type))
            {
                var field = (FieldInfo)node.MemberInfo;
                var origValue = field.GetValue(source);
                var clonedValue = DeepCloneInternal(origValue, visited);

                // MemberwiseClone already copied this reference (e.g. an immutable string or a null) -
                // no need to set it again.
                if (!ReferenceEquals(clonedValue, origValue))
                {
                    field.SetValue(clone, clonedValue);
                }
            }

            return clone;
        }

        private static Func<object, Dictionary<object, object>, object> GetTypedCloner(Type type) =>
            TypedClonerCache.GetOrAdd(type, CreateTypedCloner);

        private static Func<object, Dictionary<object, object>, object> CreateTypedCloner(Type type)
        {
            var fields = GetDeepCloneFields(type)
                .Select(node => (FieldInfo)node.MemberInfo)
                .ToArray();

            var sourceParam = Expression.Parameter(typeof(object), "source");
            var visitedParam = Expression.Parameter(typeof(Dictionary<object, object>), "visited");

            var typedSource = Expression.Variable(type, "typedSource");
            var cloneObj = Expression.Variable(typeof(object), "clone");
            var typedClone = Expression.Variable(type, "typedClone");

            var variables = new List<ParameterExpression> { typedSource, cloneObj, typedClone };
            var statements = new List<Expression>
            {
                Expression.Assign(typedSource, Expression.Convert(sourceParam, type)),
                Expression.Assign(cloneObj, Expression.Invoke(Expression.Constant(MemberwiseCloneFunc), sourceParam)),
            };

            if (!type.IsValueType)
            {
                // visited[source] = clone - registered before recursing into fields so self-references resolve
                statements.Add(Expression.Assign(Expression.Property(visitedParam, VisitedIndexerProperty, sourceParam), cloneObj));
            }

            statements.Add(Expression.Assign(typedClone, Expression.Convert(cloneObj, type)));

            foreach (var field in fields)
            {
                var origVar = Expression.Variable(typeof(object), "orig_" + field.Name);
                var clonedVar = Expression.Variable(typeof(object), "cloned_" + field.Name);
                variables.Add(origVar);
                variables.Add(clonedVar);

                statements.Add(Expression.Assign(origVar, Expression.Convert(Expression.Field(typedSource, field), typeof(object))));
                statements.Add(Expression.Assign(clonedVar, Expression.Call(DeepCloneInternalMethod, origVar, visitedParam)));

                // MemberwiseClone already copied this reference (e.g. an immutable string, a null, or a cyclic
                // reference resolved back to the very clone being built) - no need to set it again.
                // Readonly fields (e.g. the backing field of a get-only auto-property) cannot be assigned
                // from an expression tree, so they go through a small reflection helper instead.
                Expression setField = field.IsInitOnly
                    ? Expression.Call(
                        SetFieldValueMethod,
                        Expression.Convert(typedClone, typeof(object)),
                        Expression.Constant(field),
                        clonedVar)
                    : Expression.Assign(Expression.Field(typedClone, field), Expression.Convert(clonedVar, field.FieldType));

                statements.Add(Expression.IfThen(
                    Expression.Not(Expression.Call(ReferenceEqualsMethod, clonedVar, origVar)),
                    setField));
            }

            if (type.IsValueType)
            {
                // struct fields were mutated on a local copy (typedClone); write it back into the boxed clone
                statements.Add(Expression.Assign(cloneObj, Expression.Convert(typedClone, typeof(object))));
            }

            statements.Add(cloneObj);

            var body = Expression.Block(typeof(object), variables, statements);
            return Expression.Lambda<Func<object, Dictionary<object, object>, object>>(body, sourceParam, visitedParam).Compile();
        }

        private static void CopyMultidimensionalArray(Array source, Array dest, int[] indices, int dimension, Dictionary<object, object> visited)
        {
            if (dimension == source.Rank)
            {
                var item = source.GetValue(indices);
                if (item != null)
                {
                    var clonedItem = DeepCloneInternal(item, visited);
                    dest.SetValue(clonedItem, indices);
                }
                return;
            }

            int length = source.GetLength(dimension);
            int lowerBound = source.GetLowerBound(dimension);

            for (int i = 0; i < length; i++)
            {
                indices[dimension] = i + lowerBound;
                CopyMultidimensionalArray(source, dest, indices, dimension + 1, visited);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RegisterVisited(object source, object clone, Type type, Dictionary<object, object> visited)
        {
            if (!type.IsValueType)
            {
                visited[source] = clone;
            }
        }

        private static IReadOnlyList<ObjectGraphNode> GetDeepCloneFields(Type type) =>
            DeepCloneFieldsCache.GetOrAdd(type, static t =>
            {
                var objectGraph = DefaultEnumerator.Enumerate(t);
                return (IReadOnlyList<ObjectGraphNode>)objectGraph.Roots
                    .Where(root => root.MemberType == MemberType.Field && NeedsDeepClone(root.Type))
                    .ToList();
            });

        // Mirrors RuntimeHelpers.IsReferenceOrContainsReferences<T>() without the generic-invoke overhead and
        // without requiring the netstandard2.1 target to support that intrinsic.
        private static bool NeedsDeepClone(Type type) =>
            NeedsDeepCloneCache.GetOrAdd(type, static t =>
            {
                if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t.IsEnum)
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

        private static object CreateCollectionInstance(Type type) =>
            CollectionFactoryCache.GetOrAdd(type, static t =>
            {
                var ctor = t.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot deep clone collection of type {t.AssemblyQualifiedName}: no accessible parameterless constructor was found.");
                }

                return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
            })();

        private static object CreateCollectionInstance(Type type, object? comparer) =>
            TryCreateCollectionInstanceWithComparer(type, comparer, useCapacity: false, capacity: 0, out var instance)
                ? instance!
                : CreateCollectionInstance(type);

        private static PropertyInfo? GetCollectionComparerProperty(Type type) =>
            CollectionComparerPropertyCache.GetOrAdd(
                type, static t => t.GetProperty("Comparer", BindingFlags.Instance | BindingFlags.Public));

        private static object? GetCollectionComparerValue(Type type, object source)
        {
            var comparerProperty = GetCollectionComparerProperty(type);
            if (comparerProperty == null || !comparerProperty.PropertyType.IsInterface)
            {
                return null;
            }

            try
            {
                return comparerProperty.GetValue(source);
            }
            catch (Exception)
            {
                // a throwing Comparer getter must not break cloning the collection's items;
                // fall back to default construction (previous behavior)
                return null;
            }
        }

        private static bool TryCreateCollectionInstanceWithComparer(
            Type type, object? comparer, bool useCapacity, int capacity, out object? instance)
        {
            instance = null;
            if (comparer == null)
            {
                return false;
            }

            var comparerType = GetCollectionComparerProperty(type)?.PropertyType;
            if (comparerType == null || !comparerType.IsInterface || !comparerType.IsInstanceOfType(comparer))
            {
                return false;
            }

            if (useCapacity)
            {
                var sizedFactory = CollectionSizedComparerFactoryCache.GetOrAdd(
                    type, t => BuildSizedComparerFactory(t, comparerType));
                if (sizedFactory != null)
                {
                    instance = sizedFactory(capacity, comparer);
                    return true;
                }
            }

            var factory = CollectionComparerFactoryCache.GetOrAdd(
                type, t => BuildComparerFactory(t, comparerType));
            if (factory == null)
            {
                return false;
            }

            instance = factory(comparer);
            return true;
        }

        private static Func<int, object?, object>? BuildSizedComparerFactory(Type type, Type comparerType)
        {
            var ctor = type.GetConstructor(new[] { typeof(int), comparerType });
            if (ctor == null)
            {
                return null;
            }

            var capacityParam = Expression.Parameter(typeof(int), "capacity");
            var comparerParam = Expression.Parameter(typeof(object), "comparer");
            return Expression.Lambda<Func<int, object?, object>>(
                Expression.Convert(
                    Expression.New(ctor, capacityParam, Expression.Convert(comparerParam, comparerType)),
                    typeof(object)),
                capacityParam, comparerParam).Compile();
        }

        private static Func<object?, object>? BuildComparerFactory(Type type, Type comparerType)
        {
            var ctor = type.GetConstructor(new[] { comparerType });
            if (ctor == null)
            {
                return null;
            }

            var comparerParam = Expression.Parameter(typeof(object), "comparer");
            return Expression.Lambda<Func<object?, object>>(
                Expression.Convert(
                    Expression.New(ctor, Expression.Convert(comparerParam, comparerType)),
                    typeof(object)),
                comparerParam).Compile();
        }

        // Pre-sizes the clone to the source's item count when the collection type has a (int capacity)
        // constructor (List<T>, Dictionary<TKey,TValue>, HashSet<T>, etc.), avoiding the repeated backing-array
        // resizes that adding items one at a time into a zero-capacity instance would otherwise cause.
        private static object CreateCollectionInstance(Type type, int capacity)
        {
            var capacityFactory = CollectionCapacityFactoryCache.GetOrAdd(type, static t =>
            {
                var ctor = t.GetConstructor(new[] { typeof(int) });
                if (ctor == null)
                {
                    return null;
                }

                var capacityParam = Expression.Parameter(typeof(int), "capacity");
                return Expression.Lambda<Func<int, object>>(
                    Expression.Convert(Expression.New(ctor, capacityParam), typeof(object)), capacityParam).Compile();
            });

            return capacityFactory != null ? capacityFactory(capacity) : CreateCollectionInstance(type);
        }

        private static object CreateCollectionInstance(Type type, int capacity, object? comparer)
        {
            // prefer (capacity, comparer), then (comparer), so collections without a
            // pre-sizing constructor (e.g. SortedSet<T>) still keep the source comparer
            // instead of silently falling back to the default one
            if (comparer != null &&
                TryCreateCollectionInstanceWithComparer(type, comparer, useCapacity: true, capacity, out var instance))
            {
                return instance!;
            }

            if (comparer != null &&
                TryCreateCollectionInstanceWithComparer(type, comparer, useCapacity: false, capacity: 0, out var comparerInstance))
            {
                return comparerInstance!;
            }

            return CreateCollectionInstanceWithCapacityFallback(type, capacity);
        }

        private static object CreateCollectionInstanceWithCapacityFallback(Type type, int capacity)
        {
            var capacityFactory = CollectionCapacityFactoryCache.GetOrAdd(type, static t =>
            {
                var ctor = t.GetConstructor(new[] { typeof(int) });
                if (ctor == null)
                {
                    return null;
                }

                var capacityParam = Expression.Parameter(typeof(int), "capacity");
                return Expression.Lambda<Func<int, object>>(
                    Expression.Convert(Expression.New(ctor, capacityParam), typeof(object)), capacityParam).Compile();
            });

            return capacityFactory != null ? capacityFactory(capacity) : CreateCollectionInstance(type);
        }

        /// <summary>
        /// Copies state declared on a collection subclass (e.g. an extra property on a
        /// <c>List&lt;T&gt;</c> derivative) from source to clone. Plain framework collections
        /// are skipped: their storage lives on the framework type itself and is already cloned.
        /// </summary>
        private static void CopyCollectionExtraState(object source, object clone, Type type, Dictionary<object, object> visited)
        {
            if (IsFrameworkCollectionType(type))
            {
                return;
            }

            // walk up to (excluding) the framework base: in an A : B : List<int> chain where
            // the extra state lives on B, looking only at the concrete type would miss it.
            // Derived members win when a name is hidden with 'new'.
            var copiedProperties = new HashSet<string>();
            var copiedFields = new HashSet<string>();
            for (var current = type; current != null && !IsFrameworkCollectionType(current); current = current.BaseType)
            {
                CopyDeclaredCollectionState(source, clone, current, visited, copiedProperties, copiedFields);
            }
        }

        private static void CopyDeclaredCollectionState(
            object source,
            object clone,
            Type type,
            Dictionary<object, object> visited,
            HashSet<string> copiedProperties,
            HashSet<string> copiedFields)
        {
            foreach (var property in type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead || !copiedProperties.Add(property.Name))
                {
                    continue;
                }

                var getMethod = property.GetMethod;
                var setMethod = property.SetMethod;
                if (getMethod == null || getMethod.IsStatic || setMethod == null || setMethod.IsStatic)
                {
                    continue;
                }

                object? originalValue;
                try
                {
                    originalValue = getMethod.Invoke(source, null);
                }
                catch (Exception)
                {
                    // best effort: a throwing getter must not break cloning the collection's items
                    continue;
                }

                // NOTE: no ReferenceEquals skip here (unlike CloneObject): the clone was built
                // with a fresh constructor, not MemberwiseClone, so even identical references
                // (e.g. an immutable string) still need to be assigned onto the new instance.
                var clonedValue = DeepCloneInternal(originalValue, visited);
                setMethod.Invoke(clone, new[] { clonedValue });
            }

            foreach (var field in type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.IsStatic || !copiedFields.Add(field.Name))
                {
                    continue;
                }

                // auto-property backing fields are covered through their property above
                if (field.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                {
                    continue;
                }

                var clonedFieldValue = DeepCloneInternal(field.GetValue(source), visited);
                field.SetValue(clone, clonedFieldValue);
            }
        }

        private static bool IsFrameworkCollectionType(Type type)
        {
            var assembly = type.Assembly;
            if (assembly == typeof(object).Assembly ||
                assembly == typeof(ArrayList).Assembly ||
                assembly == typeof(System.Collections.ObjectModel.Collection<>).Assembly ||
                assembly == typeof(System.Collections.Specialized.StringCollection).Assembly)
            {
                return true;
            }

            // other framework collections (e.g. ConcurrentDictionary, whose lock objects must
            // never be copied by field) live in System.* assemblies; user subclasses live in
            // user assemblies and still get their extra state copied
            return type.Namespace != null &&
                (type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal)) &&
                assembly.GetName().Name!.StartsWith("System.", StringComparison.Ordinal);
        }

        private static bool TryGetCollectionAdder(Type type, out Action<object, object?>? adder)
        {
            adder = CollectionAdderCache.GetOrAdd(type, static t =>
            {
                var collectionInterface = t.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));

                var addMethod = collectionInterface?.GetMethod("Add");
                if (addMethod == null)
                {
                    return null;
                }

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var itemParam = Expression.Parameter(typeof(object), "item");
                var itemType = addMethod.GetParameters()[0].ParameterType;
                var call = Expression.Call(
                    Expression.Convert(instanceParam, collectionInterface!),
                    addMethod,
                    Expression.Convert(itemParam, itemType));

                return Expression.Lambda<Action<object, object?>>(call, instanceParam, itemParam).Compile();
            });

            return adder != null;
        }

        private static bool TryGetFastCollectionCopy(Type type, out Action<object, object>? copier)
        {
            copier = FastCollectionCopyCache.GetOrAdd(type, static t =>
            {
                var elementType = GetCollectionElementType(t);
                return elementType != null && !NeedsDeepClone(elementType)
                    ? BuildFastCollectionCopy(t, elementType)
                    : null;
            });

            return copier != null;
        }

        private static Type? GetCollectionElementType(Type type) =>
            CollectionElementTypeCache.GetOrAdd(type, static t =>
            {
                foreach (var i in t.GetInterfaces())
                {
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        return i.GetGenericArguments()[0];
                    }
                }

                return null;
            });

        // Builds a compiled `(source, clone) => foreach (var item in (T)source) ((ICollection<TItem>)clone).Add(item)`
        // delegate. Enumerating through the collection's own strongly-typed GetEnumerator() (a struct, for every
        // built-in BCL collection) rather than the non-generic IEnumerable/IList interface means no per-item
        // boxing occurs for value-type elements - only the loop itself is emitted once, and JIT-compiled like
        // ordinary code from then on.
        private static Action<object, object>? BuildFastCollectionCopy(Type collectionType, Type itemType)
        {
            var collectionInterface = typeof(ICollection<>).MakeGenericType(itemType);
            var addMethod = collectionInterface.GetMethod("Add");
            if (addMethod == null || !collectionInterface.IsAssignableFrom(collectionType))
            {
                return null;
            }

            var enumerableInterface = typeof(IEnumerable<>).MakeGenericType(itemType);
            var getEnumeratorMethod = collectionType.GetMethod("GetEnumerator", Type.EmptyTypes)
                ?? enumerableInterface.GetMethod("GetEnumerator")!;
            var enumeratorType = getEnumeratorMethod.ReturnType;
            var moveNextMethod = enumeratorType.GetMethod("MoveNext", Type.EmptyTypes) ?? typeof(IEnumerator).GetMethod("MoveNext")!;
            var currentProperty = enumeratorType.GetProperty("Current")!;

            var sourceParam = Expression.Parameter(typeof(object), "source");
            var cloneParam = Expression.Parameter(typeof(object), "clone");

            var typedSource = Expression.Variable(collectionType, "typedSource");
            var typedClone = Expression.Variable(collectionInterface, "typedClone");
            var enumerator = Expression.Variable(enumeratorType, "enumerator");
            var breakLabel = Expression.Label("LoopBreak");

            var loopBody = Expression.Block(
                new[] { typedSource, typedClone, enumerator },
                Expression.Assign(typedSource, Expression.Convert(sourceParam, collectionType)),
                Expression.Assign(typedClone, Expression.Convert(cloneParam, collectionInterface)),
                Expression.Assign(enumerator, Expression.Call(typedSource, getEnumeratorMethod)),
                Expression.Loop(
                    Expression.IfThenElse(
                        Expression.Call(enumerator, moveNextMethod),
                        Expression.Call(typedClone, addMethod, Expression.Property(enumerator, currentProperty)),
                        Expression.Break(breakLabel)),
                    breakLabel));

            return Expression.Lambda<Action<object, object>>(loopBody, sourceParam, cloneParam).Compile();
        }

        private static Func<object, object> CreateMemberwiseCloneFunc()
        {
            var memberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var emitter = Emit<Func<object, object>>.NewDynamicMethod("ObjectTreeWalker_MemberwiseClone");

            emitter.LoadArgument(0);
            emitter.Call(memberwiseCloneMethod);
            emitter.Return();

            return emitter.CreateDelegate();
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
