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
        /// <param name="predicate">An optional predicate to ignore some object members when traversing (return false for certain iteration item to skip it)</param>
        public void Traverse(object obj, Action<MemberAccessor> visitor, Func<MemberAccessor, bool>? predicate = null)
        {
            var objectGraph = _objectEnumerator.Enumerate(obj.GetType());
            predicate ??= _ => true;

            var traversalQueue = TraversalQueuePool.Get();
            try
            {
                var rootObjectAccessor = GetCachedObjectAccessor(objectGraph.Type);

                foreach (var root in objectGraph.Roots)
                {
                    traversalQueue.Enqueue(
                        (new MemberAccessor(root.Name, obj, rootObjectAccessor), root));
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

                    if (!predicate(current.IterationItem))
                    {
                        continue;
                    }

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
                                (new MemberAccessor(child.Name, nodeInstance!, objectAccessor), child));
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

        /// <summary>
        /// A member accessor that allows to get and set member values during iteration
        /// </summary>
        public class MemberAccessor
        {
            private readonly ObjectAccessor _objectAccessor;
            private readonly object _object;
            private readonly string _memberName;

            /// <summary>
            /// Initializes a new instance of the <see cref="MemberAccessor"/> class.
            /// </summary>
            /// <param name="memberName">Member (field/property) name</param>
            /// <param name="obj">parent object instance</param>
            /// <param name="objectAccessor">internal object accessor that allows accessing members</param>
            /// <exception cref="ArgumentNullException">Gets thrown if any of constructor parameters is null</exception>
            internal MemberAccessor(string memberName, object obj, ObjectAccessor objectAccessor)
            {
                _memberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
                _object = obj ?? throw new ArgumentNullException(nameof(obj));
                _objectAccessor = objectAccessor ?? throw new ArgumentNullException(nameof(objectAccessor));
            }

            /// <summary>
            /// Gets member name
            /// </summary>
            public string Name => _memberName;

            /// <summary>
            /// Accesses and fetches member value
            /// </summary>
            /// <returns>Member value</returns>
            public object? GetValue() =>
                !_objectAccessor.TryGetValue(_object, _memberName, out var value) ?
                    null : value;

            /// <summary>
            /// Accesses and sets member value
            /// </summary>
            /// <param name="newValue">New member value</param>
            public void SetValue(object newValue) =>
                _objectAccessor.TrySetValue(_object, _memberName, newValue);
        }
    }
}
