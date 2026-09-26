using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

// ReSharper disable ComplexConditionExpression
// ReSharper disable MethodTooLong
// ReSharper disable CognitiveComplexity
namespace ObjectTreeWalker
{
    /// <summary>
    /// Exposes helper function to enumerate types and fetch member graph
    /// </summary>
    internal class ObjectEnumerator
    {
        /// <summary>
        /// Iteration Settings
        /// </summary>
        public record Settings(bool IgnoreCompilerGenerated = true, bool SkipKeyValuePairFields = false)
        {
            /// <summary>
            /// Gets or sets a value indicating whether to ignore compiler generated fields or not
            /// </summary>
            public bool IgnoreCompilerGenerated { get; set; } = IgnoreCompilerGenerated;

            /// <summary>
            /// Gets or sets a value indicating whether the backing fields of a
            /// <see cref="KeyValuePair{TKey, TValue}"/> are skipped (its Key/Value
            /// properties already report the entry; without this each entry is visited twice).
            /// </summary>
            public bool SkipKeyValuePairFields { get; set; } = SkipKeyValuePairFields;
        }

        private static readonly ConcurrentDictionary<(Type Type, bool IgnoreCompilerGenerated), ObjectGraph> ObjectGraphCache = new();
        private readonly Settings _settings;

        /// <summary>
        /// Gets enumerator settings
        /// </summary>
        public Settings EnumeratorSettings => _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectEnumerator"/> class
        /// </summary>
        /// <param name="settings">settings that might modify how iteration is done</param>
        public ObjectEnumerator(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Clears internal cache shared by all <see cref="ObjectEnumerator"/> instances
        /// </summary>
        /// <remarks>The cache is keyed by both type and settings, so this is only needed to reclaim memory, never for correctness.
        /// See also <see cref="ObjectAccessor.ClearCache"/> and <see cref="ObjectExtensions.ClearCache"/>.</remarks>
        public static void ClearCache() => ObjectGraphCache.Clear();

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectEnumerator"/> class
        /// </summary>
        public ObjectEnumerator()
        {
            _settings = new()
            {
                IgnoreCompilerGenerated = true,
            };
        }

        /// <summary>
        /// Enumerate and fetch property/field graph of the type
        /// </summary>
        /// <param name="type">the type to enumerate</param>
        /// <returns>object graph</returns>
        /// <exception cref="OverflowException">The object graph cache contains too many elements.</exception>
        public ObjectGraph Enumerate(Type type) =>
            ObjectGraphCache.GetOrAdd((type, _settings.IgnoreCompilerGenerated), key =>
            {
                var roots =
                    EnumerateChildMembers(key.Type)
                        .Select(memberData =>
                            EnumerateMember(
                                null,
                                new EnumerationItem(
                                    memberData.MemberInfo,
                                    memberData.CanGet,
                                    memberData.CanSet,
                                    memberData.MemberType)));

                return new ObjectGraph(key.Type, roots);
            });

        private ObjectGraphNode EnumerateMember(ObjectGraphNode? parent, EnumerationItem enumerationItem)
        {
            // NOTE: Children is populated even though the traversal hot path re-resolves members
            // per runtime type: ObjectEnumeratorTests pins the populated-Children contract,
            // so the "stop building it" optimization stays declined until that contract changes.
            var ogn = new ObjectGraphNode(enumerationItem.MemberInfo, parent)
            {
                CanGet = enumerationItem.CanGet,
                CanSet = enumerationItem.CanSet,
                MemberType = enumerationItem.MemberType,
            };

            var children = EnumerateChildMembers(enumerationItem.MemberInfo.GetUnderlyingType()!)
                .Select(memberData =>
                    new ObjectGraphNode(memberData.MemberInfo, ogn)
                    {
                        CanGet = memberData.CanGet,
                        CanSet = memberData.CanSet,
                        MemberType = memberData.MemberType,
                    });

            ogn.Children.AddRange(children);
            return ogn;
        }

        private IEnumerable<EnumerationItem> EnumerateChildMembers(Type type)
        {
            if (type.IsPrimitive ||
                type.IsEnum || // enums are leaves (their value__ field would otherwise be visited)
                type == typeof(string) ||
                type == typeof(decimal) ||
                typeof(IEnumerable).IsAssignableFrom(type) || // don't enumerate collections, they get special treatment
                (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Memory<>)))
            {
                yield break;
            }

            // nullable primitive is a special case
            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                type.IsPrimitive)
            {
                var valueProp = type.GetProperty(nameof(Nullable<bool>.Value), BindingFlags.Instance | BindingFlags.Public);

                // just in case
                if (valueProp == null)
                {
                    throw new InvalidOperationException("Failed to fetch 'Value' property of a Nullable<T> struct. This is not supposed to happen and is likely a bug.");
                }

                yield return new EnumerationItem(valueProp, valueProp.GetMethod != null, valueProp.SetMethod != null, MemberType.Property);
                yield break;
            }

            // nullable struct is a special case
            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                !type.IsPrimitive)
            {
                var structType = type.GenericTypeArguments[0];

                foreach (var item in EnumerateChildMembers(structType))
                {
                    yield return item;
                }

                yield break;
            }

            foreach (var property in type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
            {
                // indexers require arguments to read and cannot be traversed like plain members
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                yield return new EnumerationItem(property, property.GetMethod != null, property.SetMethod != null, MemberType.Property);
            }

            // A KeyValuePair<K, V> exposes its entry twice: the Key/Value properties and the
            // key/value backing fields. The properties already report the entry, so traversal
            // skips the fields (DeepClone keeps them: get-only properties cannot rebuild the pair).
            var skipKeyValuePairFields = _settings.SkipKeyValuePairFields &&
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

            foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
            {
                if (skipKeyValuePairFields)
                {
                    continue;
                }

                // ignore backing property, if the attribute is not true then it is a backing property
                if (_settings.IgnoreCompilerGenerated && field.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                {
                    continue;
                }

                yield return new EnumerationItem(field, true, true, MemberType.Field);
            }
        }

        private readonly record struct EnumerationItem
            (MemberInfo MemberInfo, bool CanGet, bool CanSet, MemberType MemberType)
        {
            public readonly MemberInfo MemberInfo = MemberInfo;
            public readonly bool CanGet = CanGet;
            public readonly bool CanSet = CanSet;
            public readonly MemberType MemberType = MemberType;
        }
    }
}
