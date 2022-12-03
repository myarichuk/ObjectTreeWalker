using System.Collections.Concurrent;
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
    public class ObjectMemberIterator
    {
        private static readonly ConcurrentDictionary<Type, ObjectAccessor> ObjectAccessorCache = new();
        private static readonly object EmptyContext = new();
        private static readonly ObjectPool<Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)>> TraversalQueuePool =
            new DefaultObjectPoolProvider().Create<Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)>>();

        private static ObjectEnumerator.Settings? _enumeratorSettings;
        private readonly ObjectEnumerator _objectEnumerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectMemberIterator"/> class.
        /// </summary>
        /// <param name="ignoreCompilerGenerated">ignore compiler generated fields (like auto properties)</param>
        public ObjectMemberIterator(bool ignoreCompilerGenerated = true)
        {
            if (_enumeratorSettings == null)
            {
                _enumeratorSettings = new ObjectEnumerator.Settings(IgnoreCompilerGenerated: ignoreCompilerGenerated);
                _objectEnumerator = new(_enumeratorSettings);
                return;
            }

            var newSettings = new ObjectEnumerator.Settings
            {
                IgnoreCompilerGenerated = ignoreCompilerGenerated,
            };

            if (_enumeratorSettings != newSettings)
            {
                _enumeratorSettings = newSettings;
                ObjectAccessorCache.Clear();
                ObjectEnumerator.ClearCache();
            }

            _objectEnumerator = new(_enumeratorSettings);
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

        private static void EnqueueObjectRoots(
            object obj,
            ObjectGraph objectGraph,
            Queue<(MemberAccessor IterationItem, ObjectGraphNode Node)> traversalQueue)
        {
            var rootObjectAccessor = GetCachedObjectAccessor(objectGraph.Type);

            foreach (var root in objectGraph.Roots)
            {
                traversalQueue.Enqueue(
                    (new(
                        new ObjectMemberInfo(
                            root.Name,
                            root.MemberType,
                            obj,
                            root.MemberInfo.GetUnderlyingType()!,
                            new List<string> { root.Name }),
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
        public TContext Traverse<TContext>(object obj, VisitorWithContextFunc<TContext> visitorFunc, PredicateWithContextFunc<TContext>? predicate = null)
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
        public TContext Traverse<TContext>(object obj, VisitorWithContextFunc<TContext> visitorFunc, in TContext iterationContext, PredicateWithContextFunc<TContext>? predicate = null)
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
                EnqueueObjectRoots(obj, objectGraph, traversalQueue);

#if NET6_0_OR_GREATER
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

                    var objectAccessor = GetCachedObjectAccessor(current.Node.Type);

                    // nodeInstance null means no iteration is necessary
                    if (!current.IterationItem.TryGetValue(out var nodeInstance) || nodeInstance == null)
                    {
                        visitorFunc(ref context, current.IterationItem);
                        continue;
                    }

                    /*
                           * Handle the edge-case where our member value is an upcast version of the object ->
                           * in such a case we need to dynamically fetch the type with GetType() as the object enumerator
                           * would enumerate the members of a base object at this point.
                           * In order to conserve the caching of existing object enumerator, we must not allow it to rely on reflection like this
                          */
                    if (current.Node.Type == typeof(object) ||
                        current.Node.Type == typeof(ValueType))
                    {
                        var actualType = nodeInstance?.GetType()!; // we checked for null already
                        if (actualType == typeof(object) ||
                            actualType == typeof(ValueType)) // got nothing to do!
                        {
                            continue;
                        }

                        var actualObjectGraph = _objectEnumerator.Enumerate(actualType);
                        EnqueueObjectRoots(nodeInstance!, actualObjectGraph, traversalQueue);
                        continue;
                    }

                    // we are only interested in iterating over the "data" vertices
                    // otherwise, we would get "foo(obj)" and then all foo's properties
                    if (current.Node.Children.Count == 0)
                    {
                        visitorFunc(ref context, current.IterationItem);
                    }
                    else
                    {
                        foreach (var child in current.Node.Children)
                        {
                            traversalQueue.Enqueue(
                                (new(
                                    new ObjectMemberInfo(
                                        child.Name,
                                        child.MemberType,
                                        nodeInstance,
                                        child.MemberInfo.GetUnderlyingType()!,
                                        current.IterationItem.PropertyPath.Append(child.Name)),
                                    objectAccessor), child));
                        }
                    }
                }
            }
            finally
            {
                TraversalQueuePool.Return(traversalQueue);
            }

            return context;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ObjectAccessor GetCachedObjectAccessor(Type type) =>
            ObjectAccessorCache.GetOrAdd(type, t => new ObjectAccessor(t));
    }
}
