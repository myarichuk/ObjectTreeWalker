using System.Collections;
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
    public class ObjectMemberIterator
    {
        private static readonly object EmptyContext = new();

        private static readonly ObjectPool<Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)>> TraversalQueuePool =
            new DefaultObjectPoolProvider().Create<Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)>>();

        private readonly ObjectEnumerator _objectEnumerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectMemberIterator"/> class.
        /// </summary>
        /// <param name="ignoreCompilerGenerated">ignore compiler generated fields (like auto properties)</param>
        public ObjectMemberIterator(bool ignoreCompilerGenerated = true) =>
            _objectEnumerator = new ObjectEnumerator(new ObjectEnumerator.Settings(IgnoreCompilerGenerated: ignoreCompilerGenerated));

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
            where TContext : new() =>
            Traverse(obj, visitorFunc, in iterationContext, predicate, []);

        private TContext Traverse<TContext>(
            object obj,
            VisitorWithContextFunc<TContext> visitorFunc,
            in TContext iterationContext,
            PredicateWithContextFunc<TContext>? predicate,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix)
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

            var objectGraph = _objectEnumerator.Enumerate(obj.GetType());
            var context = iterationContext ?? new TContext();

            predicate ??= (in TContext _, in MemberAccessor _) => true;

            var traversalQueue = TraversalQueuePool.Get();
            try
            {
                EnqueueObjectRoots(obj, objectGraph, null, traversalQueue);

#if NET8_0_OR_GREATER
                while (traversalQueue.TryDequeue(out var current))
                {
#else
                while (traversalQueue.Count > 0)
                {
                    var current = traversalQueue.Dequeue();
#endif
                    if (!predicate(context, current.IterationItem))
                    {
                        continue;
                    }

                    // nodeInstance null means no iteration is necessary
                    if (!current.IterationItem.TryGetValue(out var nodeInstance) || nodeInstance == null)
                    {
                        visitorFunc(ref context, current.IterationItem);
                        continue;
                    }

                    var actualType = nodeInstance.GetType();

                    // a bare boxed object/ValueType with no more specific runtime type - nothing to visit
                    if (actualType == typeof(object) || actualType == typeof(ValueType))
                    {
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
                            in iterationContext,
                            predicate);

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
                    else
                    {
                        EnqueueObjectRoots(nodeInstance, childGraph, new Ref<ObjectMemberInfo>(current.IterationItem.RawInfo), traversalQueue);
                    }
                }
            }
            finally
            {
                TraversalQueuePool.Return(traversalQueue);
            }

            return context;
        }

        private void ProcessEnumerable<TContext>(
            (MemberAccessor IterationItem, ObjectGraphNode Node) current,
            IEnumerable instanceAsEnumerable,
            IReadOnlyList<PropertyPathItem> propertyPathPrefix,
            ObjectAccessor objectAccessor,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue,
            VisitorWithContextFunc<TContext> visitorFunc,
            in TContext iterationContext,
            PredicateWithContextFunc<TContext>? predicate)
            where TContext : new()
        {
            var index = 0;

            // PropertyPath is [..parentPath, ownPathItem]; the per-item path replaces the collection
            // member's own trailing entry with the indexed item entry, so only the first PropertyPath.Count - 1
            // entries are kept from it.
            var basePath = current.IterationItem.PropertyPath;
            var baseCount = basePath.Count - 1;
            var prefixCount = propertyPathPrefix.Count;

            // Same-typed items (the overwhelmingly common case - a homogeneous List<int>, int[], etc.) can
            // share one ObjectGraphNode instead of allocating a new node (and its backing Children list) per item.
            Dictionary<Type, ObjectGraphNode>? itemGraphNodeCache = null;

            foreach (var arrayItem in instanceAsEnumerable)
            {
                if (arrayItem != null) // just in case
                {
                    var itemName = $"{current.Node.Name}[{index}]";
                    var itemType = arrayItem.GetType();

                    var propertyPath = new PropertyPathItem[prefixCount + baseCount + 1];
                    var pos = 0;
                    for (var i = 0; i < prefixCount; i++)
                    {
                        propertyPath[pos++] = propertyPathPrefix[i];
                    }

                    for (var i = 0; i < baseCount; i++)
                    {
                        propertyPath[pos++] = basePath[i];
                    }

                    propertyPath[pos] = new PropertyPathItem(itemName, index);

                    if (itemType.IsPrimitive)
                    {
                        itemGraphNodeCache ??= new Dictionary<Type, ObjectGraphNode>();
                        if (!itemGraphNodeCache.TryGetValue(itemType, out var itemGraphNode))
                        {
                            itemGraphNode = new ObjectGraphNode(itemType, current.Node);
                            itemGraphNodeCache[itemType] = itemGraphNode;
                        }

                        traversalQueue.Enqueue(
                            (new(
                                new ObjectMemberInfo(
                                    itemName,
                                    MemberType.CollectionItem,
                                    arrayItem,
                                    new Ref<ObjectMemberInfo>(current.IterationItem.RawInfo),
                                    itemType,
                                    propertyPath),
                                objectAccessor),
                                itemGraphNode));
                    }
                    else
                    {
                        Traverse(
                            arrayItem,
                            visitorFunc,
                            in iterationContext,
                            predicate,
                            propertyPath);
                    }
                }

                index++;
            }
        }
    }
}
