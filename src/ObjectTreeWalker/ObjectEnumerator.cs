using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        public record Settings(bool IgnoreCompilerGenerated = true)
        {
            /// <summary>
            /// Gets or sets a value indicating whether to ignore compiler generated fields or not
            /// </summary>
            public bool IgnoreCompilerGenerated { get; set; } = IgnoreCompilerGenerated;
        }

        private static readonly ConcurrentDictionary<Type, ObjectGraph> ObjectGraphCache = new();
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
            ObjectGraphCache.GetOrAdd(type, t =>
            {
                var roots = EnumerateChildMembers(t).Select(memberData =>
                    EnumerateMember(null, new EnumerationItem(memberData.MemberInfo, memberData.CanGet, memberData.CanSet, memberData.MemberType)));
                return new ObjectGraph(t, roots);
            });

        private ObjectGraphNode EnumerateMember(ObjectGraphNode? parent, EnumerationItem enumerationItem)
        {
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
                        MemberType = enumerationItem.MemberType,
                    });

            ogn.Children.AddRange(children);
            return ogn;
        }

        private IEnumerable<EnumerationItem> EnumerateChildMembers(Type type)
        {
            if (type.IsPrimitive || typeof(string).IsAssignableFrom(type))
            {
                yield break;
            }

            foreach (var property in type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
            {
                yield return new EnumerationItem(property, property.GetMethod != null, property.SetMethod != null, MemberType.Property);
            }

            foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
            {
                // ignore backing property, if the attribute is not true then it is a backing property
                if (_settings.IgnoreCompilerGenerated && field.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                {
                    continue;
                }

                yield return new EnumerationItem(field, true, true, MemberType.Field);
            }
        }

        private readonly record struct EnumerationItem
        {
            public readonly MemberInfo MemberInfo;
            public readonly bool CanGet;
            public readonly bool CanSet;
            public readonly MemberType MemberType;

            public EnumerationItem(MemberInfo memberInfo, bool canGet, bool canSet, MemberType memberType)
            {
                MemberInfo = memberInfo;
                CanGet = canGet;
                CanSet = canSet;
                MemberType = memberType;
            }
        }
    }
}
