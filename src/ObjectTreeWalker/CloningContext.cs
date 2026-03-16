#pragma warning disable CS1591
using System.Collections.Generic;

namespace ObjectTreeWalker
{
    public class CloningContext
    {
        private readonly Dictionary<object, object> _visited = new(ReferenceEqualityComparer.Instance);

        public bool TryGetClone(object original, out object? clone)
        {
            return _visited.TryGetValue(original, out clone);
        }

        public void RecordClone(object original, object clone)
        {
            _visited[original] = clone;
        }
    }
}
