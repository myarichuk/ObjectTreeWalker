using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;

namespace ObjectTreeWalker
{
    /// <summary>
    /// Signature of the visitor function
    /// </summary>
    /// <param name="memberAccessor">member accessor instance for currently visited member</param>
    public delegate void VisitorFunc(in MemberAccessor memberAccessor);

    /// <summary>
    /// Signature of the visit predicate function
    /// </summary>
    /// <param name="memberAccessor">member accessor instance for currently visited member</param>
    /// <returns>True if we should continue traversing, false otherwise</returns>
    public delegate bool PredicateFunc(in MemberAccessor memberAccessor);

    /// <summary>
    /// A class that allows recursive iteration over object members (BFS traversal)
    /// </summary>
    public class ObjectMemberIterator
    {
        private static readonly ConcurrentDictionary<Type, ObjectAccessor> ObjectAccessorCache = new();

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
        public void Traverse(object obj, VisitorFunc visitorFunc, PredicateFunc? predicate = null)
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

            predicate ??= (in MemberAccessor _) => true;

            var traversalQueue = TraversalQueuePool.Get();
            try
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
                                root.MemberInfo.GetUnderlyingType()!),
                            rootObjectAccessor), root));
                }

#if NET6_0
                while (traversalQueue.TryDequeue(out var current))
                {
#else
                while (traversalQueue.Count > 0)
                {
                    var current = traversalQueue.Dequeue();
#endif
                    var objectAccessor = GetCachedObjectAccessor(current.Node.Type);
                    var nodeInstance = current.IterationItem.GetValue();

                    if (nodeInstance == null)
                    {
                        throw new InvalidOperationException("Invalid (null) item in the iteration queue. This is not supposed to happen and is likely a bug.");
                    }

                    if (!predicate(current.IterationItem))
                    {
                        continue;
                    }

                    // we are only interested in iterating over the "data" vertices
                    // otherwise, we would get "foo(obj)" and then all foo's properties
                    if (current.Node.Children.Count == 0)
                    {
                        visitorFunc(current.IterationItem);
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
                                        child.MemberInfo.GetUnderlyingType()!),
                                    objectAccessor), child));
                        }
                    }
                }
            }
            finally
            {
                TraversalQueuePool.Return(traversalQueue);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ObjectAccessor GetCachedObjectAccessor(Type type) =>
            ObjectAccessorCache.GetOrAdd(type, t => new ObjectAccessor(t));
    }
}
