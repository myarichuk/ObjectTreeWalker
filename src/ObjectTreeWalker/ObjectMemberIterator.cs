using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;

// ReSharper disable ComplexConditionExpression
namespace ObjectTreeWalker
{
    /// <summary>
    /// Signature of the visitor function
    /// </summary>
    /// <param name="memberAccessor">member accessor instance for currently visited member</param>
    public delegate void VisitorFunc(in MemberAccessor memberAccessor);

    /// <summary>
    /// Signature of the visitor function
    /// </summary>
    /// <typeparam name="TContext">Type of context to use in the delegate</typeparam>
    /// <param name="context">context to provide to the iteration</param>
    /// <param name="memberAccessor">current accessor instance for currently visited member</param>
    public delegate void VisitorWithContextFunc<TContext>(ref TContext context, in MemberAccessor memberAccessor);

    /// <summary>
    /// Signature of the visit predicate function
    /// </summary>
    /// <param name="memberAccessor">member accessor instance for currently visited member</param>
    /// <returns>True if we should continue traversing, false otherwise</returns>
    public delegate bool PredicateFunc(in MemberAccessor memberAccessor);

    /// <summary>
    /// Signature of the visit predicate function
    /// </summary>
    /// <typeparam name="TContext">Type of context to use in the delegate</typeparam>
    /// <param name="context">context to provide to the iteration</param>
    /// <param name="memberAccessor">member accessor instance for currently visited member</param>
    /// <returns>True if we should continue traversing, false otherwise</returns>
    public delegate bool PredicateWithContextFunc<TContext>(in TContext context, in MemberAccessor memberAccessor);

    /// <summary>
    /// A class that allows recursive iteration over object members (BFS traversal)
    /// </summary>
    /// <remarks>
    /// The traversal tracks already-expanded reference instances, so object graphs with
    /// circular references (or shared references) terminate. A member whose value aliases
    /// an already-expanded instance is still reported to the visitor once, but is not
    /// expanded again.
    /// </remarks>
    public class ObjectMemberIterator
    {
        // pooled traversal queues wider than this are dropped instead of returned,
        // so one huge traversal does not pin that capacity for the process lifetime
        private const int MaxPooledQueueCapacity = 4096;

        private static readonly object EmptyContext = new();

        private static readonly ObjectPool<Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)>> TraversalQueuePool =
            new DefaultObjectPoolProvider().Create<Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)>>();

        private readonly ObjectEnumerator _objectEnumerator;
        private readonly bool _skipThrowingMembers;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectMemberIterator"/> class.
        /// </summary>
        /// <param name="ignoreCompilerGenerated">ignore compiler generated fields (like auto properties)</param>
        /// <param name="skipThrowingMembers">when true, members whose predicate evaluation or value retrieval throws are skipped instead of aborting the whole traversal</param>
        public ObjectMemberIterator(bool ignoreCompilerGenerated = true, bool skipThrowingMembers = false)
        {
            _skipThrowingMembers = skipThrowingMembers;
            _objectEnumerator = new ObjectEnumerator(new ObjectEnumerator.Settings(IgnoreCompilerGenerated: ignoreCompilerGenerated, SkipKeyValuePairFields: true));
        }

        /// <summary>
        /// Traverse over object members and possibly apply action to mutate the data
        /// </summary>
        /// <param name="obj">object to traverse it's members</param>
        /// <param name="visitorFunc">a lambda that encapsulates an action to apply to each member property or field</param>
        /// <param name="predicate">An optional predicate to ignore some object members when traversing (return false for certain iteration item to skip it)</param>
        /// <exception cref="InvalidOperationException">Invalid (null) item in the iteration queue. This is not supposed to happen and is likely an issue that should be reported.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="visitorFunc"/> is <see langword="null"/></exception>
        public void Traverse(object obj, VisitorFunc visitorFunc, PredicateFunc? predicate = null) =>
            Traverse(
                obj,
                (ref object _, in MemberAccessor accessor) => visitorFunc(accessor),
                EmptyContext,
                (in object _, in MemberAccessor memberAccessor) => predicate?.Invoke(memberAccessor) ?? true);

        /// <summary>
        /// Enqueues the direct members ("roots") of <paramref name="objectGraph"/> for traversal.
        /// </summary>
        /// <param name="obj">the object instance whose members are being enqueued</param>
        /// <param name="objectGraph">the member graph of <paramref name="obj"/>'s type</param>
        /// <param name="parent">the parent member (if any) that <paramref name="obj"/> was reached through; its property path is used as a prefix</param>
        /// <param name="traversalQueue">the queue to enqueue members into</param>
        private static void EnqueueObjectRoots(
            object obj,
            ObjectGraph objectGraph,
            Ref<ObjectMemberInfo>? parent,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue)
        {
            var rootObjectAccessor = ObjectAccessor.GetOrCreate(objectGraph.Type);
            var parentPath = parent?.Value.PropertyPath;

            foreach (var root in objectGraph.Roots)
            {
                var pathItem = new PropertyPathItem(root.Name);
                PropertyPathItem[] propertyPath =
                    parentPath == null
                        ? [pathItem]
                        : [.. parentPath, pathItem];

                traversalQueue.Enqueue(
                    (new(
                        new ObjectMemberInfo(
                            root.Name,
                            root.MemberType,
                            obj,
                            parent,
                            root.MemberInfo.GetUnderlyingType()!,
                            propertyPath),
                        rootObjectAccessor), root));
            }
        }

        /// <summary>
        /// Traverse over object members and possibly apply action to mutate the data
        /// </summary>
        /// <typeparam name="TContext">Type of context to use in the delegate</typeparam>
        /// <param name="obj">object to traverse it's members</param>
        /// <param name="visitorFunc">a lambda that encapsulates an action to apply to each member property or field</param>
        /// <param name="predicate">An optional predicate to ignore some object members when traversing (return false for certain iteration item to skip it)</param>
        /// <returns>iteration context instance</returns>
        /// <exception cref="InvalidOperationException">Invalid (null) item in the iteration queue. This is not supposed to happen and is likely an issue that should be reported.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="visitorFunc"/> is <see langword="null"/></exception>
        public TContext Traverse<TContext>(
            object obj,
            VisitorWithContextFunc<TContext> visitorFunc,
            PredicateWithContextFunc<TContext>? predicate = null)
            where TContext : new() =>
            Traverse(obj, visitorFunc, new TContext(), predicate);

        /// <summary>
        /// Traverse over object members and possibly apply action to mutate the data
        /// </summary>
        /// <typeparam name="TContext">Type of context to use in the delegate</typeparam>
        /// <param name="obj">object to traverse it's members</param>
        /// <param name="visitorFunc">a lambda that encapsulates an action to apply to each member property or field</param>
        /// <param name="iterationContext">initial value of the context</param>
        /// <param name="predicate">An optional predicate to ignore some object members when traversing (return false for certain iteration item to skip it)</param>
        /// <returns>iteration context instance</returns>
        /// <exception cref="InvalidOperationException">Invalid (null) item in the iteration queue. This is not supposed to happen and is likely an issue that should be reported.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="visitorFunc"/> is <see langword="null"/></exception>
        public TContext Traverse<TContext>(
            object obj,
            VisitorWithContextFunc<TContext> visitorFunc,
            in TContext iterationContext,
            PredicateWithContextFunc<TContext>? predicate = null)
            where TContext : new()
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (visitorFunc == null)
            {
                throw new ArgumentNullException(nameof(visitorFunc));
            }

            var context = iterationContext;
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!obj.GetType().IsValueType)
            {
                visited.Add(obj);
            }

            TraverseCore(obj, visitorFunc, ref context, predicate, [], visited);
            return context;
        }

        private void TraverseCore<TContext>(
            object obj,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext>? predicate,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            HashSet<object> visited)
            where TContext : new()
        {
            predicate ??= (in TContext _, in MemberAccessor _) => true;

            var traversalQueue = TraversalQueuePool.Get();
            var peakQueueSize = 0;
            try
            {
                if (obj is IEnumerable rootEnumerable && obj is not string)
                {
                    // a root collection has no member of its own: expand straight to its items
                    var rootAccessor = ObjectAccessor.GetOrCreate(obj.GetType());
                    if (obj is IDictionary rootDictionary)
                    {
                        ProcessDictionaryItems(
                            string.Empty, [], 0, propertyPathPrefix,
                            null, null, rootAccessor, rootDictionary,
                            traversalQueue, visitorFunc, ref context, predicate, visited);
                    }
                    else
                    {
                        ProcessEnumerableItems(
                            string.Empty, [], 0, propertyPathPrefix,
                            null, null, rootAccessor, rootEnumerable,
                            traversalQueue, visitorFunc, ref context, predicate, visited);
                    }
                }
                else
                {
                    var objectGraph = _objectEnumerator.Enumerate(obj.GetType());
                    EnqueueObjectRoots(obj, objectGraph, null, traversalQueue);
                }

                peakQueueSize = DrainTraversalQueue(traversalQueue, visitorFunc, ref context, predicate, propertyPathPrefix, visited);
            }
            finally
            {
                // the queue must go back empty: a throwing visitor/getter would otherwise
                // leave stale items behind that poison the next traversal reusing it.
                // A huge traversal must not pin its capacity in the pool forever, so
                // queues that grew past the threshold are dropped instead of returned.
                if (peakQueueSize <= MaxPooledQueueCapacity)
                {
                    traversalQueue.Clear();
                    TraversalQueuePool.Return(traversalQueue);
                }
            }
        }

        private int DrainTraversalQueue<TContext>(
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext> predicate,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            HashSet<object> visited)
            where TContext : new()
        {
            var peakQueueSize = traversalQueue.Count;
#if NET8_0_OR_GREATER
            while (traversalQueue.TryDequeue(out var current))
            {
#else
            while (traversalQueue.Count > 0)
            {
                var current = traversalQueue.Dequeue();
#endif
                if (traversalQueue.Count > peakQueueSize)
                {
                    peakQueueSize = traversalQueue.Count;
                }

                object? nodeInstance;
                try
                {
                    if (!predicate(context, current.IterationItem))
                    {
                        continue;
                    }

                    // nodeInstance null means no iteration is necessary
                    if (!current.IterationItem.TryGetValue(out nodeInstance) || nodeInstance == null)
                    {
                        visitorFunc(ref context, current.IterationItem);
                        continue;
                    }
                }
                catch (Exception) when (_skipThrowingMembers)
                {
                    // resilient mode: a throwing predicate or getter skips the member
                    // instead of aborting the whole traversal
                    continue;
                }

                var actualType = nodeInstance.GetType();

                if (nodeInstance is IDictionary dictionary)
                {
                    var objectAccessor = ObjectAccessor.GetOrCreate(current.Node.Type);
                    ProcessDictionary(
                        current,
                        dictionary,
                        propertyPathPrefix,
                        objectAccessor,
                        traversalQueue,
                        visitorFunc,
                        ref context,
                        predicate,
                        visited);

                    continue;
                }

                if (nodeInstance is IEnumerable instanceAsEnumerable and not string)
                {
                    var objectAccessor = ObjectAccessor.GetOrCreate(current.Node.Type);
                    ProcessEnumerable(
                        current,
                        instanceAsEnumerable,
                        propertyPathPrefix,
                        objectAccessor,
                        traversalQueue,
                        visitorFunc,
                        ref context,
                        predicate,
                        visited);

                    continue;
                }

                /*
                   * Always resolve members from the actual runtime type rather than the member's declared type.
                   * This both handles upcast members (declared as object/ValueType) and lets traversal recurse
                   * to arbitrary depth instead of stopping after the first level - ObjectEnumerator.Enumerate
                   * is cached per-type, so re-resolving here on every level is cheap.
                */
                var childGraph = _objectEnumerator.Enumerate(actualType);

                // no traversable members (primitive/string/decimal/etc.) - this is a "data" leaf
                if (childGraph.Roots.Count == 0)
                {
                    visitorFunc(ref context, current.IterationItem);
                }
                else if (actualType.IsValueType || visited.Add(nodeInstance))
                {
                    EnqueueObjectRoots(nodeInstance, childGraph, new Ref<ObjectMemberInfo>(current.IterationItem.RawInfo), traversalQueue);
                }
                else
                {
                    // alias of an already-expanded instance: report it, but do not expand again
                    // (this is what makes traversal terminate on circular/shared references)
                    visitorFunc(ref context, current.IterationItem);
                }
            }

            return peakQueueSize;
        }

        private void ProcessEnumerable<TContext>(
            (MemberAccessor IterationItem, ObjectGraphNode Node) current,
            IEnumerable instanceAsEnumerable,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            ObjectAccessor objectAccessor,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext> predicate,
            HashSet<object> visited)
            where TContext : new()
        {
            // PropertyPath is [..parentPath, ownPathItem]; the per-item path replaces the collection
            // member's own trailing entry with the indexed item entry, so only the first PropertyPath.Count - 1
            // entries are kept from it.
            var basePath = current.IterationItem.PropertyPath;
            ProcessEnumerableItems(
                current.Node.Name, basePath, basePath.Count - 1, propertyPathPrefix,
                new Ref<ObjectMemberInfo>(current.IterationItem.RawInfo), current.Node,
                objectAccessor, instanceAsEnumerable,
                traversalQueue, visitorFunc, ref context, predicate, visited);
        }

        private void ProcessDictionary<TContext>(
            (MemberAccessor IterationItem, ObjectGraphNode Node) current,
            IDictionary dictionary,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            ObjectAccessor objectAccessor,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext> predicate,
            HashSet<object> visited)
            where TContext : new()
        {
            var basePath = current.IterationItem.PropertyPath;
            ProcessDictionaryItems(
                current.Node.Name, basePath, basePath.Count - 1, propertyPathPrefix,
                new Ref<ObjectMemberInfo>(current.IterationItem.RawInfo), current.Node,
                objectAccessor, dictionary,
                traversalQueue, visitorFunc, ref context, predicate, visited);
        }

        private void ProcessEnumerableItems<TContext>(
            string namePrefix,
            IReadOnlyList<PropertyPathItem> baseItems,
            int baseCount,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            Ref<ObjectMemberInfo>? parentRef,
            ObjectGraphNode? parentNode,
            ObjectAccessor objectAccessor,
            IEnumerable items,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext>? predicate,
            HashSet<object> visited)
            where TContext : new()
        {
            static void AdvanceRankIndices(int[]? rankIndices, int[]? rankLengths, int[]? rankLowerBounds)
            {
                if (rankIndices == null || rankLengths == null || rankLowerBounds == null)
                {
                    return;
                }

                for (var dimension = rankIndices.Length - 1; dimension >= 0; dimension--)
                {
                    rankIndices[dimension]++;
                    if (rankIndices[dimension] < rankLowerBounds[dimension] + rankLengths[dimension])
                    {
                        break;
                    }

                    rankIndices[dimension] = rankLowerBounds[dimension];
                }
            }

            var index = 0;
            Dictionary<Type, ObjectGraphNode>? leafNodeCache = null;

            // multidimensional arrays enumerate in row-major order; track per-dimension indices
            // so item paths map to rank indices (M[0,1]) instead of a flat position (M[3])
            int[]? rankIndices = null;
            int[]? rankLengths = null;
            int[]? rankLowerBounds = null;
            if (items is Array array && array.Rank > 1)
            {
                rankLengths = new int[array.Rank];
                rankLowerBounds = new int[array.Rank];
                rankIndices = new int[array.Rank];
                for (var dimension = 0; dimension < array.Rank; dimension++)
                {
                    rankLengths[dimension] = array.GetLength(dimension);
                    rankLowerBounds[dimension] = array.GetLowerBound(dimension);
                    rankIndices[dimension] = rankLowerBounds[dimension];
                }
            }

            foreach (var arrayItem in items)
            {
                string itemName;
                if (rankIndices != null)
                {
                    var indices = string.Join(",", rankIndices);
                    itemName = string.IsNullOrEmpty(namePrefix) ? $"[{indices}]" : $"{namePrefix}[{indices}]";
                }
                else
                {
                    itemName = string.IsNullOrEmpty(namePrefix) ? $"[{index}]" : $"{namePrefix}[{index}]";
                }

                EnqueueOrRecurseItem(
                    arrayItem, itemName, index, isPartOfDictionary: false,
                    propertyPathPrefix, baseItems, baseCount,
                    parentRef, parentNode, objectAccessor,
                    traversalQueue, visitorFunc, ref context, predicate, visited,
                    ref leafNodeCache);

                index++;
                AdvanceRankIndices(rankIndices, rankLengths, rankLowerBounds);
            }
        }

        private void ProcessDictionaryItems<TContext>(
            string namePrefix,
            IReadOnlyList<PropertyPathItem> baseItems,
            int baseCount,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            Ref<ObjectMemberInfo>? parentRef,
            ObjectGraphNode? parentNode,
            ObjectAccessor objectAccessor,
            IDictionary dictionary,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext>? predicate,
            HashSet<object> visited)
            where TContext : new()
        {
            var index = 0;
            Dictionary<Type, ObjectGraphNode>? leafNodeCache = null;

            foreach (DictionaryEntry entry in dictionary)
            {
                var itemBase = string.IsNullOrEmpty(namePrefix) ? $"[{index}]" : $"{namePrefix}[{index}]";

                EnqueueOrRecurseItem(
                    entry.Key, $"{itemBase}.Key", index, isPartOfDictionary: true,
                    propertyPathPrefix, baseItems, baseCount,
                    parentRef, parentNode, objectAccessor,
                    traversalQueue, visitorFunc, ref context, predicate, visited,
                    ref leafNodeCache);

                EnqueueOrRecurseItem(
                    entry.Value, $"{itemBase}.Value", index, isPartOfDictionary: true,
                    propertyPathPrefix, baseItems, baseCount,
                    parentRef, parentNode, objectAccessor,
                    traversalQueue, visitorFunc, ref context, predicate, visited,
                    ref leafNodeCache);

                index++;
            }
        }

        private void EnqueueOrRecurseItem<TContext>(
            object? itemValue,
            string itemName,
            int itemIndex,
            bool isPartOfDictionary,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            IReadOnlyList<PropertyPathItem> baseItems,
            int baseCount,
            Ref<ObjectMemberInfo>? parentRef,
            ObjectGraphNode? parentNode,
            ObjectAccessor objectAccessor,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            ref TContext context,
            PredicateWithContextFunc<TContext>? predicate,
            HashSet<object> visited,
            ref Dictionary<Type, ObjectGraphNode>? leafNodeCache)
            where TContext : new()
        {
            var prefixCount = propertyPathPrefix.Count;
            var propertyPath = new PropertyPathItem[prefixCount + baseCount + 1];
            var pos = 0;
            for (var i = 0; i < prefixCount; i++)
            {
                propertyPath[pos++] = propertyPathPrefix[i];
            }

            for (var i = 0; i < baseCount; i++)
            {
                propertyPath[pos++] = baseItems[i];
            }

            propertyPath[pos] = new PropertyPathItem(itemName, itemIndex, isPartOfDictionary);

            if (itemValue == null)
            {
                // null items are reported like null members instead of being silently skipped
                EnqueueLeafItem(
                    null, itemName, propertyPath,
                    parentRef, parentNode, objectAccessor, traversalQueue, ref leafNodeCache);
                return;
            }

            var itemType = itemValue.GetType();

            // items without traversable members (primitives, strings, decimals, ...) are
            // reported directly; everything else recurses so its members get visited
            if (_objectEnumerator.Enumerate(itemType).Roots.Count == 0 ||
                (!itemType.IsValueType && !visited.Add(itemValue)))
            {
                EnqueueLeafItem(
                    itemValue, itemName, propertyPath,
                    parentRef, parentNode, objectAccessor, traversalQueue, ref leafNodeCache);
                return;
            }

            TraverseCore(itemValue, visitorFunc, ref context, predicate, propertyPath, visited);
        }

        private static void EnqueueLeafItem(
            object? itemValue,
            string itemName,
            PropertyPathItem[] propertyPath,
            Ref<ObjectMemberInfo>? parentRef,
            ObjectGraphNode? parentNode,
            ObjectAccessor objectAccessor,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            ref Dictionary<Type, ObjectGraphNode>? leafNodeCache)
        {
            // Same-typed items (the overwhelmingly common case - a homogeneous List<int>, int[], etc.) can
            // share one ObjectGraphNode instead of allocating a new node (and its backing Children list) per item.
            var itemType = itemValue?.GetType() ?? typeof(object);
            leafNodeCache ??= new Dictionary<Type, ObjectGraphNode>();
            if (!leafNodeCache.TryGetValue(itemType, out var itemGraphNode))
            {
                itemGraphNode = new ObjectGraphNode(itemType, parentNode);
                leafNodeCache[itemType] = itemGraphNode;
            }

            traversalQueue.Enqueue(
                (new(
                    new ObjectMemberInfo(
                        itemName,
                        MemberType.CollectionItem,
                        itemValue!,
                        parentRef,
                        itemType,
                        propertyPath),
                    objectAccessor),
                    itemGraphNode));
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
