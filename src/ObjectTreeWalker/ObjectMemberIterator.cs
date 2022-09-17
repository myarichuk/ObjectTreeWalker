using System.Collections.Concurrent;
using System.Reflection;
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
		private static readonly ObjectPool<Queue<(IterationInfo iterationItem, ObjectGraphNode node)>> TraversalQueuePool =
			new DefaultObjectPoolProvider().Create<Queue<(IterationInfo iterationItem, ObjectGraphNode node)>>();

		public void Traverse(object obj, Action<IterationInfo> visitor)
		{
			var objectGraph = ObjectEnumerator.Enumerate(obj.GetType());

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
					var objectAccessor = GetCachedObjectAccessor(current.node.Type);
					var nodeInstance = current.iterationItem.GetValue();

					// we are only interested in iterating over the "data" vertices
					// otherwise, we would get "foo(obj)" and then all foo's properties
					if (current.node.Children.Count == 0)
					{
						visitor(current.iterationItem);
					}
					else
					{
						foreach (var child in current.node.Children)
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
