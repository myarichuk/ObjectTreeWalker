using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ObjectTreeWalker
{
    /// <summary>
    /// Exposes helper function to enumerate types and fetch member graph
    /// </summary>
    internal sealed class ObjectEnumerator
    {
        private const BindingFlags DeclaredInstanceMembers =
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // One type-keyed graph cache per distinct (immutable) settings value: every setting changes the
        // resulting graph. Resolved once per enumerator, so the per-value lookup on the traversal hot path
        // hashes a Type only - never the settings record.
        private static readonly ConcurrentDictionary<Settings, ConcurrentDictionary<Type, ObjectGraph>> GraphCaches = new();

        private readonly ConcurrentDictionary<Type, ObjectGraph> _graphs;
        private readonly Func<Type, ObjectGraph> _graphFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectEnumerator"/> class
        /// </summary>
        /// <param name="settings">settings that might modify how iteration is done</param>
        public ObjectEnumerator(Settings settings)
        {
            EnumeratorSettings = settings ?? throw new ArgumentNullException(nameof(settings));
            _graphs = GraphCaches.GetOrAdd(settings, static _ => new ConcurrentDictionary<Type, ObjectGraph>());
            _graphFactory = CreateGraph;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectEnumerator"/> class with default settings
        /// </summary>
        public ObjectEnumerator()
            : this(new Settings())
        {
        }

        /// <summary>
        /// Gets enumerator settings
        /// </summary>
        public Settings EnumeratorSettings { get; }

        /// <summary>
        /// Clears internal cache shared by all <see cref="ObjectEnumerator"/> instances
        /// </summary>
        /// <remarks>The cache is keyed by both type and settings, so this is only needed to reclaim memory, never for correctness.
        /// Exposed publicly through <see cref="ObjectMemberIterator.ClearCache"/>.</remarks>
        public static void ClearCache()
        {
            foreach (var graphs in GraphCaches.Values)
            {
                graphs.Clear();
            }
        }

        /// <summary>
        /// Enumerate and fetch property/field graph of the type
        /// </summary>
        /// <param name="type">the type to enumerate</param>
        /// <returns>object graph</returns>
        public ObjectGraph Enumerate(Type type) =>
            _graphs.TryGetValue(type, out var graph) ? graph : _graphs.GetOrAdd(type, _graphFactory);

        private static ObjectGraphNode CreateNode(EnumerationItem item, ObjectGraphNode? parent) =>
            new(item.MemberInfo, parent)
            {
                CanGet = item.CanGet,
                CanSet = item.CanSet,
                MemberType = item.MemberType,
            };

        private static bool IsUserLevel(Type? type) =>
            type != null && type != typeof(object) && type != typeof(ValueType) && type != typeof(Enum);

        private static bool IsLeafType(Type type) =>
            type.IsPrimitive ||
            type.IsEnum || // enums are leaves (their value__ field would otherwise be visited)
            type.IsPointer ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            typeof(IEnumerable).IsAssignableFrom(type) || // collections get special treatment by the traversal
            (type.IsGenericType &&
             (type.GetGenericTypeDefinition() == typeof(Memory<>) ||
              type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>)));

        // members of these types cannot be read into an object (pointers, byrefs, ref structs)
        private static bool IsSupportedMemberType(Type memberType) =>
            !memberType.IsPointer &&
            !memberType.IsByRef &&
            !memberType.IsByRefLike;

        private ObjectGraph CreateGraph(Type type) =>
            new(type, EnumerateChildMembers(type).Select(item => EnumerateMember(null, item)));

        private ObjectGraphNode EnumerateMember(ObjectGraphNode? parent, EnumerationItem item)
        {
            // NOTE: Children is populated even though the traversal hot path re-resolves members
            // per runtime type: ObjectEnumeratorTests pins the populated-Children contract.
            var node = CreateNode(item, parent);
            node.Children.AddRange(EnumerateChildMembers(node.Type).Select(child => CreateNode(child, node)));
            return node;
        }

        private IEnumerable<EnumerationItem> EnumerateChildMembers(Type type)
        {
            if (IsLeafType(type))
            {
                yield break;
            }

            // a Nullable<T> is traversed as its underlying T (a boxed T? is a boxed T at runtime)
            var underlyingNullable = Nullable.GetUnderlyingType(type);
            if (underlyingNullable != null)
            {
                foreach (var item in EnumerateChildMembers(underlyingNullable))
                {
                    yield return item;
                }

                yield break;
            }

            // Walk the hierarchy level by level: GetProperties/GetFields on the concrete type do not
            // return *private* members of base classes, which would silently skip base-class state.
            // Order is kept as before: all properties (most derived first), then all fields.
            var seenAccessors = new HashSet<MethodInfo>();
            for (var current = type; IsUserLevel(current); current = current!.BaseType)
            {
                foreach (var property in current!.GetProperties(DeclaredInstanceMembers))
                {
                    // indexers require arguments to read and cannot be traversed like plain members
                    if (property.GetIndexParameters().Length != 0 || !IsSupportedMemberType(property.PropertyType))
                    {
                        continue;
                    }

                    // an override and the virtual it overrides are the same member: report the most derived one.
                    // A member hidden with 'new' has its own base definition, so it is reported separately.
                    var accessor = property.GetMethod ?? property.SetMethod;
                    if (accessor != null && !seenAccessors.Add(accessor.GetBaseDefinition()))
                    {
                        continue;
                    }

                    yield return new EnumerationItem(property, property.GetMethod != null, property.SetMethod != null, MemberType.Property);
                }
            }

            for (var current = type; IsUserLevel(current); current = current!.BaseType)
            {
                // A KeyValuePair<K, V> exposes its entry twice: the Key/Value properties and the
                // key/value backing fields. The properties already report the entry, so traversal
                // skips the fields (DeepClone keeps them: get-only properties cannot rebuild the pair).
                if (EnumeratorSettings.SkipKeyValuePairFields &&
                    current!.IsGenericType && current.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    continue;
                }

                foreach (var field in current!.GetFields(DeclaredInstanceMembers))
                {
                    if (!IsSupportedMemberType(field.FieldType))
                    {
                        continue;
                    }

                    // compiler generated fields are auto-property backing fields, closures' captured state etc.
                    if (EnumeratorSettings.IgnoreCompilerGenerated && field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                    {
                        continue;
                    }

                    yield return new EnumerationItem(field, true, true, MemberType.Field);
                }
            }
        }

        /// <summary>
        /// Iteration Settings
        /// </summary>
        /// <param name="IgnoreCompilerGenerated">whether to ignore compiler generated fields (such as auto-property backing fields)</param>
        /// <param name="SkipKeyValuePairFields">
        /// whether the backing fields of a <see cref="KeyValuePair{TKey, TValue}"/> are skipped (its Key/Value
        /// properties already report the entry; without this each entry is visited twice)
        /// </param>
        /// <remarks>Immutable on purpose: it is part of the shared graph cache key.</remarks>
        public sealed record Settings(bool IgnoreCompilerGenerated = true, bool SkipKeyValuePairFields = false);

        private readonly record struct EnumerationItem(MemberInfo MemberInfo, bool CanGet, bool CanSet, MemberType MemberType);
    }
}
