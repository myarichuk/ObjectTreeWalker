using System.Collections.Concurrent;

namespace ObjectTreeWalker
{
	/// <summary>
	/// A class that allows recursive iteration over object members (BFS traversal)
	/// </summary>
	public class ObjectMemberIterator
	{
		private static readonly ConcurrentDictionary<Type, ObjectAccessor> ObjectAccessorCache = new();

		/// <summary>
		/// Initializes a new instance of the <see cref="ObjectMemberIterator"/> class
		/// </summary>
		public ObjectMemberIterator()
		{
		}
	}
}
