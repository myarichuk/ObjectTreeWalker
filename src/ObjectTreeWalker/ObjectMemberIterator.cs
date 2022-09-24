using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;

namespace ObjectTreeWalker
{
    /// <summary>
    /// A class that allows recursive iteration over object members (BFS traversal)
    /// </summary>
    public class ObjectMemberIterator
    {
        private static readonly ConcurrentDictionary<Type, ObjectAccessor> ObjectAccessorCache = new();

        private static readonly ObjectPool<Queue<(IterationInfo IterationItem, ObjectGraphNode Node)>> TraversalQueuePool =
            new DefaultObjectPoolProvider().Create<Queue<(IterationInfo IterationItem, ObjectGraphNode Node)>>();

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
                _enumeratorSettings = new ObjectEnumerator.Settings
                {
                    IgnoreCompilerGenerated = ignoreCompilerGenerated,
                };
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
        /// <param name="visitor">a lambda that encapsulates an action to apply to each member property or field</param>
        public void Traverse(object obj, Action<IterationInfo> visitor)
        {
            var objectGraph = _objectEnumerator.Enumerate(obj.GetType());

            var traversalQueue = TraversalQueuePool.Get();
            try
            {
                var rootObjectAccessor = GetCachedObjectAccessor(objectGraph.Type);

                foreach (var root in objectGraph.Roots)
                {
                    traversalQueue.Enqueue(
                        (new IterationInfo(root.Name, obj, rootObjectAccessor), root));
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

                    // we are only interested in iterating over the "data" vertices
                    // otherwise, we would get "foo(obj)" and then all foo's properties
                    if (current.Node.Children.Count == 0)
                    {
                        visitor(current.IterationItem);
                    }
                    else
                    {
                        foreach (var child in current.Node.Children)
                        {
                            traversalQueue.Enqueue(
                                (new IterationInfo(child.Name, nodeInstance!, objectAccessor), child));
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

        public class IterationInfo
        {
            private readonly ObjectAccessor _objectAccessor;
            private readonly object _object;
            private readonly string _memberName;

            internal IterationInfo(string memberName, object obj, ObjectAccessor objectAccessor)
            {
                _memberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
                _object = obj ?? throw new ArgumentNullException(nameof(obj));
                _objectAccessor = objectAccessor ?? throw new ArgumentNullException(nameof(objectAccessor));
            }

            public object? GetValue() =>
                !_objectAccessor.TryGetValue(_object, _memberName, out var value) ?
                    null : value;

            public void SetValue(object newValue) =>
                _objectAccessor.TrySetValue(_object, _memberName, newValue);
        }
    }
}
