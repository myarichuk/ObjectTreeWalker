#pragma warning disable CS1591
using System.Collections.Generic;
using Microsoft.Extensions.ObjectPool;

namespace ObjectTreeWalker
{
    internal class DictionaryPooledObjectPolicy : IPooledObjectPolicy<Dictionary<object, object>>
    {
        public Dictionary<object, object> Create() => new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        public bool Return(Dictionary<object, object> obj)
        {
            obj.Clear();
            return true;
        }
    }

    internal class QueuePooledObjectPolicy : IPooledObjectPolicy<Queue<object>>
    {
        public Queue<object> Create() => new Queue<object>();

        public bool Return(Queue<object> obj)
        {
            obj.Clear();
            return true;
        }
    }

    public static class CollectionPools
    {
        private static readonly ObjectPool<Dictionary<object, object>> _visitedDictionaries =
            new DefaultObjectPoolProvider().Create(new DictionaryPooledObjectPolicy());

        private static readonly ObjectPool<Queue<object>> _bfsQueues =
            new DefaultObjectPoolProvider().Create(new QueuePooledObjectPolicy());

        public static Dictionary<object, object> RentVisitedDictionary() => _visitedDictionaries.Get();

        public static void ReturnVisitedDictionary(Dictionary<object, object> dict) => _visitedDictionaries.Return(dict);

        public static Queue<object> RentBfsQueue() => _bfsQueues.Get();

        public static void ReturnBfsQueue(Queue<object> queue) => _bfsQueues.Return(queue);
    }
}
