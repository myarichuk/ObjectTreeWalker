using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ObjectTreeWalker
{
    /// <summary>
    /// Extension methods for object operations like deep cloning.
    /// </summary>
    public static class ObjectExtensions
    {
        private static readonly ObjectEnumerator DefaultEnumerator = new ObjectEnumerator(new ObjectEnumerator.Settings { IgnoreCompilerGenerated = false });

        /// <summary>
        /// Creates a deep clone of the object.
        /// </summary>
        /// <typeparam name="T">The type of the object.</typeparam>
        /// <param name="source">The object to clone.</param>
        /// <returns>A deep clone of the object.</returns>
        public static T DeepClone<T>(this T source)
        {
            var visited = new Dictionary<object, object>(new ReferenceEqualityComparer());
            return (T)DeepCloneInternal(source, visited)!;
        }

        private static object? DeepCloneInternal(object? source, Dictionary<object, object> visited)
        {
            if (source is null)
            {
                return null;
            }

            var type = source.GetType();

            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type.IsEnum)
            {
                return source;
            }

            // check for cyclic references
            if (!type.IsValueType && visited.TryGetValue(source, out var existingClone))
            {
                return existingClone;
            }

            if (type.IsArray)
            {
                var array = (Array)source;
                var cloneArray = (Array)array.Clone();

                if (!type.IsValueType)
                {
                    visited[source] = cloneArray;
                }

                if (array.Rank == 1)
                {
                    for (int i = 0; i < array.Length; i++)
                    {
                        var item = array.GetValue(i);
                        if (item != null)
                        {
                            var clonedItem = DeepCloneInternal(item, visited);
                            cloneArray.SetValue(clonedItem, i);
                        }
                    }
                }
                else
                {
                    // multidimensional array handling
                    var indices = new int[array.Rank];
                    CopyMultidimensionalArray(array, cloneArray, indices, 0, visited);
                }

                return cloneArray;
            }

#pragma warning disable SYSLIB0050
            var clone = FormatterServices.GetUninitializedObject(type);
#pragma warning restore SYSLIB0050

            if (!type.IsValueType)
            {
                visited[source] = clone;
            }

            var objectAccessor = new ObjectAccessor(type);
            var objectGraph = DefaultEnumerator.Enumerate(type);

            // if clone is a struct, boxed version is used to set values
            object box = clone;

            foreach (var root in objectGraph.Roots)
            {
                // we only clone fields since we bypass constructors
                if (root.MemberType != MemberType.Field)
                {
                    continue;
                }

                if (objectAccessor.TryGetValue(source, root.Name, out var originalValue))
                {
                    if (originalValue != null)
                    {
                        var clonedValue = DeepCloneInternal(originalValue, visited);
                        objectAccessor.TrySetValue(box, root.Name, clonedValue);
                    }
                }
            }

            return box;
        }

        private static void CopyMultidimensionalArray(Array source, Array dest, int[] indices, int dimension, Dictionary<object, object> visited)
        {
            if (dimension == source.Rank)
            {
                var item = source.GetValue(indices);
                if (item != null)
                {
                    var clonedItem = DeepCloneInternal(item, visited);
                    dest.SetValue(clonedItem, indices);
                }
                return;
            }

            int length = source.GetLength(dimension);
            int lowerBound = source.GetLowerBound(dimension);

            for (int i = 0; i < length; i++)
            {
                indices[dimension] = i + lowerBound;
                CopyMultidimensionalArray(source, dest, indices, dimension + 1, visited);
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
