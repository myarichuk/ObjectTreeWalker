#pragma warning disable CS1591
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ObjectTreeWalker
{
    public static class CollectionPools
    {
        private static readonly ConcurrentBag<Dictionary<object, object>> _visitedDictionaries = new();
        private static readonly ConcurrentBag<Queue<object>> _bfsQueues = new();

        public static Dictionary<object, object> RentVisitedDictionary()
        {
            if (_visitedDictionaries.TryTake(out var dict))
            {
                dict.Clear();
                return dict;
            }
            return new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        }

        public static void ReturnVisitedDictionary(Dictionary<object, object> dict)
        {
            _visitedDictionaries.Add(dict);
        }

        public static Queue<object> RentBfsQueue()
        {
            if (_bfsQueues.TryTake(out var queue))
            {
                queue.Clear();
                return queue;
            }
            return new Queue<object>();
        }

        public static void ReturnBfsQueue(Queue<object> queue)
        {
            _bfsQueues.Add(queue);
        }
    }
}
