#pragma warning disable CS1591
using System.Collections.Generic;

using System.Runtime.CompilerServices;

namespace ObjectTreeWalker
{
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        private ReferenceEqualityComparer() { }

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

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
