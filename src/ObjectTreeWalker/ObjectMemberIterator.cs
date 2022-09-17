using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace ObjectTreeWalker
{
	/// <summary>
	/// A class that allows recursive iteration over object members (BFS traversal)
	/// </summary>
	public class ObjectMemberIterator
	{
		private static readonly ConcurrentDictionary<Type, ObjectAccessor> ObjectAccessorCache = new();
		private static readonly ObjectPool<Queue<KeyValuePair<string, object>>> TraversalQueuePool =
			new DefaultObjectPoolProvider().Create<Queue<KeyValuePair<string, object>>>();

		/// <summary>
		/// Initializes a new instance of the <see cref="ObjectMemberIterator"/> class
		/// </summary>
		public ObjectMemberIterator()
		{
		}
	}
}
