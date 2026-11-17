using System.Collections;
using System.Runtime.CompilerServices;
using static ObjectTreeWalker.CollectionItemSlot;

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
    /// The traversal tracks already-expanded reference instances (objects and collections), so object graphs
    /// with circular references (or shared references) terminate. A member whose value aliases an
    /// already-expanded instance is still reported to the visitor once, but is not expanded again.
    /// </remarks>
    public class ObjectMemberIterator
    {
        // pooled traversal queues wider than this are dropped instead of returned,
        // so one huge traversal does not pin that capacity for the thread lifetime
        private const int MaxPooledQueueCapacity = 4096;
        private const int MaxPooledQueuesPerThread = 8;

        // traversal of collection items recurses, so a thread may need several queues at once
        [ThreadStatic]
        private static Stack<Queue<MemberAccessor>>? queuePool;

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
        /// Clears the per-type member graphs and compiled accessors shared by all iterators
        /// </summary>
        /// <remarks>
        /// The caches are keyed by type, so this is only needed to reclaim memory in long-lived hosts
        /// that see many dynamic or generic types — never for correctness.
        /// See also <see cref="ObjectExtensions.ClearCache"/>.
        /// </remarks>
        public static void ClearCache()
        {
            ObjectEnumerator.ClearCache();
            ObjectAccessor.ClearCache();
        }

        /// <summary>
        /// Traverse over object members and possibly apply action to mutate the data
        /// </summary>
        /// <param name="obj">object to traverse it's members</param>
        /// <param name="visitorFunc">a lambda that encapsulates an action to apply to each member property or field</param>
        /// <param name="predicate">An optional predicate to ignore some object members when traversing (return false for certain iteration item to skip it)</param>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="visitorFunc"/> is <see langword="null"/></exception>
        public void Traverse(object obj, VisitorFunc visitorFunc, PredicateFunc? predicate = null)
        {
            if (visitorFunc == null)
            {
                throw new ArgumentNullException(nameof(visitorFunc));
            }

            // the visitor and predicate travel as the context, so the static lambdas allocate nothing per call
            Traverse(
                obj,
                static (ref SimpleCallbacks callbacks, in MemberAccessor accessor) => callbacks.Visitor(accessor),
                new SimpleCallbacks(visitorFunc, predicate),
                predicate == null ? null : static (in SimpleCallbacks callbacks, in MemberAccessor accessor) => callbacks.Predicate!(accessor));
        }

        /// <summary>
        /// Traverse over object members and possibly apply action to mutate the data
        /// </summary>
        /// <typeparam name="TContext">Type of context to use in the delegate</typeparam>
        /// <param name="obj">object to traverse it's members</param>
        /// <param name="visitorFunc">a lambda that encapsulates an action to apply to each member property or field</param>
        /// <param name="predicate">An optional predicate to ignore some object members when traversing (return false for certain iteration item to skip it)</param>
        /// <returns>iteration context instance</returns>
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
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="visitorFunc"/> is <see langword="null"/></exception>
        public TContext Traverse<TContext>(
            object obj,
            VisitorWithContextFunc<TContext> visitorFunc,
            in TContext iterationContext,
            PredicateWithContextFunc<TContext>? predicate = null)
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

            // the same pooled, open-addressing identity map DeepClone uses (only its keys matter here)
            var visited = CloneContext.Rent();
            try
            {
                if (!obj.GetType().IsValueType)
                {
                    visited.GetOrAddSlot(obj, out _);
                }

                var traversal = new Traversal<TContext>(this, visitorFunc, predicate, visited);
                traversal.Run(obj, null, ref context);
                return context;
            }
            finally
            {
                CloneContext.Return(visited);
            }
        }

        private static Queue<MemberAccessor> RentQueue()
        {
            var pool = queuePool;
            return pool is { Count: > 0 } ? pool.Pop() : new Queue<MemberAccessor>();
        }

        private static void ReturnQueue(Queue<MemberAccessor> queue, int peakSize)
        {
            // the queue must go back empty: a throwing visitor/getter would otherwise
            // leave stale items behind that poison the next traversal reusing it
            if (peakSize > MaxPooledQueueCapacity)
            {
                return;
            }

            var pool = queuePool ??= new Stack<Queue<MemberAccessor>>();
            if (pool.Count < MaxPooledQueuesPerThread)
            {
                queue.Clear();
                pool.Push(queue);
            }
        }

        private static string ItemName(string prefix, int index) => ItemNameCache.Format(prefix, index, string.Empty);

        private static string ItemName(string prefix, int[] rankIndices)
        {
            var indices = string.Join(",", rankIndices);
            return prefix.Length == 0 ? $"[{indices}]" : $"{prefix}[{indices}]";
        }

        private readonly struct SimpleCallbacks
        {
            public readonly VisitorFunc Visitor;
            public readonly PredicateFunc? Predicate;

            public SimpleCallbacks(VisitorFunc visitor, PredicateFunc? predicate)
            {
                Visitor = visitor;
                Predicate = predicate;
            }
        }

        /// <summary>
        /// State of a single <c>Traverse</c> call. Passed by reference, so the many arguments every step
        /// needs are not threaded through each method signature.
        /// </summary>
        private readonly struct Traversal<TContext>
        {
            private readonly ObjectMemberIterator _iterator;
            private readonly VisitorWithContextFunc<TContext> _visitor;
            private readonly PredicateWithContextFunc<TContext>? _predicate;
            private readonly CloneContext _visited;

            public Traversal(
                ObjectMemberIterator iterator,
                VisitorWithContextFunc<TContext> visitor,
                PredicateWithContextFunc<TContext>? predicate,
                CloneContext visited)
            {
                _iterator = iterator;
                _visitor = visitor;
                _predicate = predicate;
                _visited = visited;
            }

            /// <summary>
            /// Visits the members of <paramref name="obj"/> (or the items, if it is a collection)
            /// </summary>
            /// <param name="obj">the object to visit</param>
            /// <param name="parent">the member/item <paramref name="obj"/> was reached through; null for the root</param>
            /// <param name="context">the traversal context</param>
            public void Run(object obj, MemberFrame? parent, ref TContext context)
            {
                var queue = RentQueue();
                var peakQueueSize = 0;
                try
                {
                    if (obj is IEnumerable collection and not string)
                    {
                        // a root collection has no member of its own: expand straight to its items
                        ProcessItems(collection, parent, queue, ref context);
                    }
                    else
                    {
                        EnqueueMembers(obj, _iterator._objectEnumerator.Enumerate(obj.GetType()), parent, queue);
                    }

                    peakQueueSize = Drain(queue, ref context);
                }
                finally
                {
                    ReturnQueue(queue, peakQueueSize);
                }
            }

            private static void EnqueueMembers(
                object obj,
                ObjectGraph objectGraph,
                MemberFrame? parent,
                Queue<MemberAccessor> queue)
            {
                var roots = objectGraph.Roots;
                for (var i = 0; i < roots.Count; i++)
                {
                    queue.Enqueue(new MemberAccessor(new ObjectMemberInfo(roots[i], obj, parent)));
                }
            }

            /// <summary>
            /// Records <paramref name="instance"/> as expanded
            /// </summary>
            /// <param name="instance">a reference-type instance</param>
            /// <returns>true if it was not expanded before</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool MarkVisited(object instance)
            {
                _visited.GetOrAddSlot(instance, out var alreadyVisited);
                return !alreadyVisited;
            }

            private int Drain(Queue<MemberAccessor> queue, ref TContext context)
            {
                var peakQueueSize = queue.Count;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (queue.Count > peakQueueSize)
                    {
                        peakQueueSize = queue.Count;
                    }

                    object? value;
                    try
                    {
                        if (_predicate != null && !_predicate(context, current))
                        {
                            continue;
                        }

                        // no value (null, or a set-only property) means there is nothing to expand
                        if (!current.TryGetValue(out value) || value == null)
                        {
                            _visitor(ref context, current);
                            continue;
                        }
                    }
                    catch (Exception) when (_iterator._skipThrowingMembers)
                    {
                        // resilient mode: a throwing predicate or getter skips the member
                        // instead of aborting the whole traversal
                        continue;
                    }

                    /*
                     * Always resolve members from the actual runtime type rather than the member's declared type.
                     * This both handles upcast members (declared as object/ValueType) and lets traversal recurse
                     * to arbitrary depth. Members are nearly always monomorphic, so the member's last graph is
                     * checked first (one reference compare) before the type-keyed cache.
                     */
                    var valueType = value.GetType();
                    var node = current.RawInfo.Node;
                    var childGraph = node?.LastValueGraph ?? current.RawInfo.ItemGraph;
                    if (childGraph == null || childGraph.Type != valueType)
                    {
                        childGraph = _iterator._objectEnumerator.Enumerate(valueType);
                        if (node != null)
                        {
                            node.LastValueGraph = childGraph;
                        }
                    }

                    var isReference = !childGraph.IsValueType;

                    if (childGraph.IsCollection)
                    {
                        // collections are tracked like any other instance: a collection reachable from
                        // itself (directly or through its items) must not be expanded forever
                        if (isReference && !MarkVisited(value))
                        {
                            _visitor(ref context, current);
                        }
                        else
                        {
                            ProcessItems((IEnumerable)value, new MemberFrame(current.RawInfo), queue, ref context);
                        }

                        continue;
                    }

                    if (childGraph.Roots.Count == 0)
                    {
                        // no traversable members (primitive/string/decimal/etc.) - this is a "data" leaf
                        _visitor(ref context, current);
                    }
                    else if (!isReference || MarkVisited(value))
                    {
                        EnqueueMembers(value, childGraph, new MemberFrame(current.RawInfo), queue);
                    }
                    else
                    {
                        // alias of an already-expanded instance: report it, but do not expand again
                        // (this is what makes traversal terminate on circular/shared references)
                        _visitor(ref context, current);
                    }
                }

                return peakQueueSize;
            }

            /// <summary>
            /// Reports or expands every item of <paramref name="items"/>
            /// </summary>
            /// <param name="items">the collection</param>
            /// <param name="owner">the collection member; null for a root collection</param>
            /// <param name="queue">the queue leaf items are reported through</param>
            /// <param name="context">traversal context</param>
            private void ProcessItems(IEnumerable items, MemberFrame? owner, Queue<MemberAccessor> queue, ref TContext context)
            {
                var namePrefix = owner?.Value.Name ?? string.Empty;

                // names of a member's items are the same on every traversal: format them once, reuse them.
                // Items of items (nested collections) have dynamic prefixes and are formatted on demand.
                var names = owner == null ? ItemNameCache.Root : owner.Value.Node?.ItemNames;

                switch (items)
                {
                    case IDictionary dictionary:
                    {
                        var index = 0;
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            ProcessItem(
                                entry.Key,
                                names?.Key(index) ?? ItemNameCache.Format(namePrefix, index, ".Key"),
                                index,
                                isPartOfDictionary: true,
                                new Location(dictionary, SlotKind.DictionaryKey),
                                owner,
                                queue,
                                ref context);
                            ProcessItem(
                                entry.Value,
                                names?.Value(index) ?? ItemNameCache.Format(namePrefix, index, ".Value"),
                                index,
                                isPartOfDictionary: true,
                                new Location(dictionary, SlotKind.DictionaryValue, Key: entry.Key),
                                owner,
                                queue,
                                ref context);
                            index++;
                        }

                        break;
                    }

                    case Array array when array.Rank > 1 || array.GetLowerBound(0) != 0:
                        ProcessNonSzArray(array, owner, namePrefix, queue, ref context);
                        break;

                    case IList list:
                        // indexed instead of enumerated: writing a modified struct item back
                        // (see MemberAccessor.SetValue) would invalidate an active enumerator
                        for (var index = 0; index < list.Count; index++)
                        {
                            ProcessItem(
                                list[index],
                                names?.Item(index) ?? ItemName(namePrefix, index),
                                index,
                                isPartOfDictionary: false,
                                new Location(list, SlotKind.ListIndex, Index: index),
                                owner,
                                queue,
                                ref context);
                        }

                        break;

                    default:
                    {
                        var index = 0;
                        foreach (var item in items)
                        {
                            ProcessItem(
                                item,
                                names?.Item(index) ?? ItemName(namePrefix, index),
                                index,
                                isPartOfDictionary: false,
                                new Location(items, SlotKind.NotIndexable),
                                owner,
                                queue,
                                ref context);
                            index++;
                        }

                        break;
                    }
                }
            }

            private void ProcessNonSzArray(
                Array array,
                MemberFrame? owner,
                string namePrefix,
                Queue<MemberAccessor> queue,
                ref TContext context)
            {
                // multidimensional arrays enumerate in row-major order; track per-dimension indices
                // so item paths map to rank indices (M[0,1]) instead of a flat position (M[3])
                var rank = array.Rank;
                var lengths = new int[rank];
                var lowerBounds = new int[rank];
                var indices = new int[rank];
                for (var dimension = 0; dimension < rank; dimension++)
                {
                    lengths[dimension] = array.GetLength(dimension);
                    lowerBounds[dimension] = array.GetLowerBound(dimension);
                    indices[dimension] = lowerBounds[dimension];
                }

                var index = 0;
                foreach (var item in array)
                {
                    var name = rank > 1 ? ItemName(namePrefix, indices) : ItemName(namePrefix, index);
                    ProcessItem(
                        item,
                        name,
                        index,
                        isPartOfDictionary: false,
                        new Location(array, SlotKind.ArrayIndices, Indices: indices),
                        owner,
                        queue,
                        ref context);

                    index++;
                    for (var dimension = rank - 1; dimension >= 0; dimension--)
                    {
                        if (++indices[dimension] < lowerBounds[dimension] + lengths[dimension])
                        {
                            break;
                        }

                        indices[dimension] = lowerBounds[dimension];
                    }
                }
            }

            private void ProcessItem(
                object? item,
                string itemName,
                int itemIndex,
                bool isPartOfDictionary,
                in Location location,
                MemberFrame? owner,
                Queue<MemberAccessor> queue,
                ref TContext context)
            {
                // items without traversable members (primitives, strings, collections, ...) and aliases of
                // already-expanded instances are reported as items; collections among them are expanded
                // when dequeued. Everything else recurses so its members get visited.
                if (item == null)
                {
                    queue.Enqueue(new MemberAccessor(new ObjectMemberInfo(itemName, null, itemIndex, isPartOfDictionary, null, owner, null)));
                    return;
                }

                var graph = ResolveItemGraph(item.GetType(), owner?.Value.Node, location.Kind == SlotKind.DictionaryKey);
                if (graph.Roots.Count == 0 || (!graph.IsValueType && !MarkVisited(item)))
                {
                    queue.Enqueue(new MemberAccessor(new ObjectMemberInfo(itemName, item, itemIndex, isPartOfDictionary, null, owner, graph)));
                    return;
                }

                // value-type items are boxed copies: remember where they live so member changes can be written back
                var slot = graph.IsValueType ? CollectionItemSlot.Create(location) : null;
                var itemInfo = new ObjectMemberInfo(itemName, item, itemIndex, isPartOfDictionary, slot, owner, graph);
                Run(item, new MemberFrame(itemInfo), ref context);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ObjectGraph ResolveItemGraph(Type itemType, ObjectGraphNode? collectionNode, bool isKey)
            {
                var cached = isKey ? collectionNode?.LastKeyGraph : collectionNode?.LastItemGraph;
                if (cached != null && cached.Type == itemType)
                {
                    return cached;
                }

                var graph = _iterator._objectEnumerator.Enumerate(itemType);
                if (collectionNode != null)
                {
                    if (isKey)
                    {
                        collectionNode.LastKeyGraph = graph;
                    }
                    else
                    {
                        collectionNode.LastItemGraph = graph;
                    }
                }

                return graph;
            }
        }
    }
}
