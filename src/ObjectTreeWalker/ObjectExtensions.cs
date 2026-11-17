using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ObjectTreeWalker
{
    /// <summary>
    /// Extension methods for object operations like deep cloning.
    /// </summary>
    public static class ObjectExtensions
    {
        private static readonly ConcurrentDictionary<Type, Func<object>?> CollectionFactoryCache = new();

        // Collections that expose a (int capacity) constructor get pre-sized to the source's item count,
        // so List/Dictionary/HashSet clones don't repeatedly regrow (and re-allocate) their backing arrays
        // as items are added one at a time.
        private static readonly ConcurrentDictionary<Type, Func<int, object>?> CollectionCapacityFactoryCache = new();

        // The collection's comparer (Dictionary<TKey,TValue>.Comparer, HashSet<T>.Comparer, ...), if any.
        // Clones reuse the source's comparer instead of silently falling back to the default one.
        private static readonly ConcurrentDictionary<Type, PropertyInfo?> CollectionComparerPropertyCache = new();

        // compiled getter of the Comparer property above (null when there is none, or it is not interface-typed)
        private static readonly ConcurrentDictionary<Type, Func<object, object?>?> CollectionComparerGetterCache = new();

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

        /// <summary>
        /// Clears all internal DeepClone caches shared by every thread.
        /// </summary>
        /// <remarks>
        /// The caches are keyed by type, so this is only needed to reclaim memory in
        /// long-lived hosts that see many dynamic or generic types — never for correctness.
        /// The compiled per-type cloners themselves are held in generic static caches and live for the
        /// process lifetime (like <c>EqualityComparer&lt;T&gt;.Default</c>).
        /// See also <see cref="ObjectMemberIterator.ClearCache"/>.
        /// </remarks>
        public static void ClearCache()
        {
            CloneEngine.ClearCache();
            CollectionFactoryCache.Clear();
            CollectionCapacityFactoryCache.Clear();
            CollectionComparerPropertyCache.Clear();
            CollectionComparerGetterCache.Clear();
            CollectionSizedComparerFactoryCache.Clear();
            CollectionComparerFactoryCache.Clear();
            CollectionAdderCache.Clear();
            CollectionElementTypeCache.Clear();
            FastCollectionCopyCache.Clear();
        }

        /// <summary>
        /// Creates a deep clone of the object.
        /// </summary>
        /// <typeparam name="T">The type of the object.</typeparam>
        /// <param name="source">The object to clone.</param>
        /// <returns>A deep clone of the object.</returns>
        public static T DeepClone<T>(this T source)
        {
            if (!CloneTraits<T>.NeedsClone || source is null)
            {
                return source; // immutable/reference-free types are folded to this by the JIT
            }

            if (typeof(T).IsValueType || source.GetType() == typeof(T))
            {
                // the static type is the exact runtime type: no dynamic dispatch at all
                if (Cloner<T>.RootShallow is { } shallow)
                {
                    return shallow(source);
                }

                var context = CloneContext.Rent();
                try
                {
                    return Cloner<T>.Clone(source, context);
                }
                finally
                {
                    CloneContext.Return(context);
                }
            }

            object boxed = source;
            if (CloneEngine.IsImmutable(boxed.GetType()) || CloneEngine.IsSharedReference(boxed))
            {
                return source;
            }

            var dynamicContext = CloneContext.Rent();
            try
            {
                return (T)CloneEngine.CloneDynamic(boxed, dynamicContext)!;
            }
            finally
            {
                CloneContext.Return(dynamicContext);
            }
        }

        /// <summary>
        /// Clones any collection (or array) other than the ones <see cref="CloneEngine"/> clones with typed code:
        /// rebuilt through its public API so hash-based collections re-hash their cloned keys, keeping its
        /// comparer and the extra state of user subclasses; field by field when it cannot be constructed.
        /// </summary>
        /// <param name="source">the collection</param>
        /// <param name="type">its exact type</param>
        /// <param name="visited">clone context</param>
        /// <returns>the clone</returns>
        internal static object CloneCollection(object source, Type type, CloneContext visited)
        {
            // check for cyclic references
            if (!type.IsValueType && visited.TryGet(source, out var existingClone))
            {
                return existingClone!;
            }

            if (type.IsArray)
            {
                return CloneArray((Array)source, visited);
            }

            // collections are rebuilt through their public API (so hash-based ones re-hash their cloned
            // keys); a collection that cannot be constructed that way (ReadOnlyCollection<T>, immutable
            // collections, ...) falls through to a field-by-field clone below
            if (source is IDictionary sourceDictionary && CanConstruct(type))
            {
                return CloneDictionary(sourceDictionary, type, visited);
            }

            if (source is IList sourceList && CanConstruct(type))
            {
                return CloneList(sourceList, type, visited);
            }

            if (source is IEnumerable sourceEnumerable)
            {
                if (TryGetCollectionAdder(type, out var adder) && CanConstruct(type))
                {
                    return CloneCollection(sourceEnumerable, type, adder!, visited);
                }

                if (type.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                {
                    throw new NotSupportedException(
                        $"Cannot deep clone enumerable of type {type.AssemblyQualifiedName}: compiler-generated iterators and generator outputs cannot be reconstructed. Materialize them into a List<T> or array first.");
                }
            }

            return CloneEngine.CloneFieldWise(source, visited);
        }

        private static object CloneArray(Array array, CloneContext visited)
        {
            var cloneArray = (Array)array.Clone();

            // arrays are always reference types
            visited.Add(array, cloneArray);

            // Array.Clone() already produced a correct, allocation-free bitwise copy of every element. If the
            // element type can't hold an object reference (int, an enum, a struct with no reference fields,
            // etc.), that bitwise copy already *is* the deep clone - walking it element by element would only
            // re-box and re-unbox each value for no benefit.
            var elementType = array.GetType().GetElementType()!;
            if (!CloneEngine.NeedsDeepClone(elementType))
            {
                return cloneArray;
            }

            if (cloneArray is object[] cloneObjects)
            {
                // zero-based, single-dimension array of a reference element type: index it directly
                // instead of going through Array.GetValue/SetValue
                for (var i = 0; i < cloneObjects.Length; i++)
                {
                    var item = cloneObjects[i];
                    if (item != null)
                    {
                        cloneObjects[i] = CloneEngine.CloneDynamic(item, visited)!;
                    }
                }
            }
            else if (array.Rank == 1)
            {
                // rank-1 arrays may have a non-zero lower bound (Array.CreateInstance);
                // indices must be offset or GetValue/SetValue throw
                var lowerBound = array.GetLowerBound(0);
                for (int i = 0; i < array.Length; i++)
                {
                    var item = array.GetValue(i + lowerBound);
                    if (item != null)
                    {
                        var clonedItem = CloneEngine.CloneDynamic(item, visited);
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

        private static object CloneDictionary(IDictionary source, Type type, CloneContext visited)
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
                var clonedKey = CloneEngine.CloneDynamic(entry.Key, visited);
                var clonedValue = CloneEngine.CloneDynamic(entry.Value, visited);
                clone[clonedKey!] = clonedValue;
            }

            CopyCollectionExtraState(source, clone, type, visited);

            return clone;
        }

        private static object CloneList(IList source, Type type, CloneContext visited)
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
                clone.Add(CloneEngine.CloneDynamic(item, visited));
            }

            CopyCollectionExtraState(source, clone, type, visited);

            return clone;
        }

        private static object CloneCollection(IEnumerable source, Type type, Action<object, object?> adder, CloneContext visited)
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
                adder(clone, CloneEngine.CloneDynamic(item, visited));
            }

            CopyCollectionExtraState(source, clone, type, visited);

            return clone;
        }

        private static void CopyMultidimensionalArray(Array source, Array dest, int[] indices, int dimension, CloneContext visited)
        {
            if (dimension == source.Rank)
            {
                var item = source.GetValue(indices);
                if (item != null)
                {
                    var clonedItem = CloneEngine.CloneDynamic(item, visited);
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

        private static void RegisterVisited(object source, object clone, Type type, CloneContext visited)
        {
            if (!type.IsValueType)
            {
                visited.Add(source, clone);
            }
        }

        private static object CreateCollectionInstance(Type type) =>
            GetParameterlessFactory(type)?.Invoke()
            ?? throw new InvalidOperationException(
                $"Cannot deep clone collection of type {type.AssemblyQualifiedName}: no accessible parameterless constructor was found.");

        // Every collection construction path ends with the parameterless constructor as its last resort,
        // so a type without one is cloned field by field instead.
        private static bool CanConstruct(Type type) => GetParameterlessFactory(type) != null;

        private static Func<object>? GetParameterlessFactory(Type type) =>
            CollectionFactoryCache.GetOrAdd(type, static t =>
            {
                // value-type collections (ImmutableArray<T>, ...) are cloned field by field
                if (t.IsAbstract || t.IsValueType)
                {
                    return null;
                }

                var ctor = t.GetConstructor(Type.EmptyTypes);
                return ctor == null
                    ? null
                    : Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
            });

        private static object CreateCollectionInstance(Type type, object? comparer) =>
            TryCreateCollectionInstanceWithComparer(type, comparer, useCapacity: false, capacity: 0, out var instance)
                ? instance!
                : CreateCollectionInstance(type);

        private static PropertyInfo? GetCollectionComparerProperty(Type type) =>
            CollectionComparerPropertyCache.GetOrAdd(
                type, static t => t.GetProperty("Comparer", BindingFlags.Instance | BindingFlags.Public));

        private static object? GetCollectionComparerValue(Type type, object source)
        {
            var getter = CollectionComparerGetterCache.GetOrAdd(type, static t =>
            {
                var comparerProperty = GetCollectionComparerProperty(t);
                if (comparerProperty?.GetMethod == null || !comparerProperty.PropertyType.IsInterface)
                {
                    return null;
                }

                var sourceParam = Expression.Parameter(typeof(object), "source");
                return Expression.Lambda<Func<object, object?>>(
                    Expression.Convert(Expression.Property(Expression.Convert(sourceParam, t), comparerProperty), typeof(object)),
                    sourceParam).Compile();
            });

            if (getter == null)
            {
                return null;
            }

            try
            {
                return getter(source);
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

        // Pre-sizes the clone to the source's item count when the collection type has a (int capacity)
        // constructor (List<T>, Dictionary<TKey,TValue>, HashSet<T>, etc.), avoiding the repeated backing-array
        // resizes that adding items one at a time into a zero-capacity instance would otherwise cause.
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
        private static void CopyCollectionExtraState(object source, object clone, Type type, CloneContext visited)
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
            CloneContext visited,
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
                var clonedValue = CloneEngine.CloneDynamic(originalValue, visited);
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

                var clonedFieldValue = CloneEngine.CloneDynamic(field.GetValue(source), visited);
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
                var collectionInterface = FindGenericInterface(t, typeof(ICollection<>));
                var addMethod = collectionInterface?.GetMethod("Add");

                // concurrent collections (ConcurrentBag<T>, ConcurrentQueue<T>) have no ICollection<T>.Add.
                // They must be rebuilt rather than copied field by field: e.g. ConcurrentBag<T> keeps its
                // items in a ThreadLocal, and a field-wise copy would share (and corrupt) the source's storage.
                // ConcurrentStack<T> is the exception - re-adding enumerated (top-first) items would reverse
                // it, and its storage is a plain linked list of nodes that clones field by field correctly.
                if (addMethod == null &&
                    !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ConcurrentStack<>)))
                {
                    collectionInterface = FindGenericInterface(t, typeof(IProducerConsumerCollection<>));
                    addMethod = collectionInterface?.GetMethod(nameof(IProducerConsumerCollection<object>.TryAdd));
                }

                if (addMethod == null)
                {
                    return null;
                }

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var itemParam = Expression.Parameter(typeof(object), "item");
                var itemType = addMethod.GetParameters()[0].ParameterType;
                Expression call = Expression.Call(
                    Expression.Convert(instanceParam, collectionInterface!),
                    addMethod,
                    Expression.Convert(itemParam, itemType));

                if (call.Type != typeof(void))
                {
                    // TryAdd returns bool; an Action<,> body must be void
                    call = Expression.Block(typeof(void), call);
                }

                return Expression.Lambda<Action<object, object?>>(call, instanceParam, itemParam).Compile();
            });

            return adder != null;
        }

        private static Type? FindGenericInterface(Type type, Type genericInterface) =>
            type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface);

        private static bool TryGetFastCollectionCopy(Type type, out Action<object, object>? copier)
        {
            copier = FastCollectionCopyCache.GetOrAdd(type, static t =>
            {
                var elementType = GetCollectionElementType(t);
                return elementType != null && !CloneEngine.NeedsDeepClone(elementType)
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
    }
}
