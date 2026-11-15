using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace ObjectTreeWalker;

/// <summary>
/// A helper class that replaces reflection access to properties and fields, by member name
/// </summary>
/// <remarks>
/// This class does not support static fields and properties.
/// When a member name is declared more than once in the hierarchy (a member hidden with <c>new</c>),
/// the most derived member wins. Traversal does not go through this class: it binds accessors to the
/// member itself (see <see cref="ObjectGraphNode.Getter"/>), which keeps hidden members distinct.
/// </remarks>
internal sealed class ObjectAccessor
{
    private static readonly ConcurrentDictionary<Type, ObjectAccessor> AccessorCache = new();

    private readonly bool _isUnsupportedType;

    // properties are inserted first, so a property wins over a same-named field (as it always has)
    private readonly Dictionary<string, CompiledMember> _members = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectAccessor"/> class
    /// </summary>
    /// <param name="objectType">type of the object to prepare access of it's properties</param>
    /// <exception cref="ArgumentException">The type <paramref name="objectType"/> is a pointer, a ref struct or contains pointer members</exception>
    public ObjectAccessor(Type objectType)
    {
        if (objectType.IsPointer)
        {
            throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} is a pointer, it is not supported by {nameof(ObjectAccessor)}.");
        }

        if (objectType is { IsValueType: true, IsByRefLike: true })
        {
            throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} is a ref struct, it is not supported by {nameof(ObjectAccessor)}.");
        }

        if (objectType == typeof(string) || typeof(IEnumerable).IsAssignableFrom(objectType))
        {
            _isUnsupportedType = true;
            return;
        }

        // GetProperties/GetFields return the most derived declarations first
        foreach (var propertyInfo in objectType.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            // indexers require arguments to read and cannot be accessed like plain members
            if (propertyInfo.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (propertyInfo.PropertyType.IsPointer)
            {
                throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} contains a pointer property, it is not supported by {nameof(ObjectAccessor)}.");
            }

            if (!_members.ContainsKey(propertyInfo.Name))
            {
                _members.Add(propertyInfo.Name, new CompiledMember(
                    propertyInfo.PropertyType,
                    propertyInfo.GetMethod != null ? MemberAccessorEmitter.CreateGetter(propertyInfo) : null,
                    propertyInfo.SetMethod != null ? MemberAccessorEmitter.CreateSetter(propertyInfo) : null));
            }
        }

        foreach (var fieldInfo in objectType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            if (fieldInfo.FieldType.IsPointer)
            {
                throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} contains a pointer field, it is not supported by {nameof(ObjectAccessor)}.");
            }

            if (!_members.ContainsKey(fieldInfo.Name))
            {
                _members.Add(fieldInfo.Name, new CompiledMember(
                    fieldInfo.FieldType,
                    MemberAccessorEmitter.CreateGetter(fieldInfo),
                    MemberAccessorEmitter.CreateSetter(fieldInfo)));
            }
        }
    }

    /// <summary>
    /// Gets a cached <see cref="ObjectAccessor"/> instance for the given type, creating it if needed.
    /// </summary>
    /// <param name="objectType">type of the object to prepare access of it's properties</param>
    /// <returns>a shared, type-keyed <see cref="ObjectAccessor"/> instance</returns>
    /// <remarks>
    /// Constructing an <see cref="ObjectAccessor"/> reflects over every member of the type and IL-emits
    /// accessor delegates for each one, so this cache must be used instead of <c>new ObjectAccessor(...)</c>
    /// everywhere the same type may be accessed more than once.
    /// </remarks>
    public static ObjectAccessor GetOrCreate(Type objectType) =>
        AccessorCache.GetOrAdd(objectType, static t => new ObjectAccessor(t));

    /// <summary>
    /// Clears the shared <see cref="ObjectAccessor"/> cache.
    /// </summary>
    /// <remarks>
    /// The cache is keyed by type, so this is only needed to reclaim memory in
    /// long-lived hosts that see many dynamic or generic types — never for correctness.
    /// Exposed publicly through <see cref="ObjectMemberIterator.ClearCache"/>.
    /// </remarks>
    public static void ClearCache() => AccessorCache.Clear();

    /// <summary>
    /// Try fetching the field or a property from the object
    /// </summary>
    /// <param name="source">object to work on</param>
    /// <param name="memberName">field or property name</param>
    /// <param name="value">value fetched or a default value</param>
    /// <returns>true if fetching successful, false otherwise</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="memberName"/> is <see langword="null"/></exception>
    public bool TryGetValue(object source, string memberName, out object? value)
    {
        ValidateThrowIfNeeded(source, memberName);

        value = null;
        if (_isUnsupportedType ||
            !_members.TryGetValue(memberName, out var member) ||
            member.Getter == null)
        {
            return false;
        }

        value = member.Getter(source);
        return true;
    }

    /// <summary>
    /// Try setting field or property in the object
    /// </summary>
    /// <param name="source">object to work on</param>
    /// <param name="memberName">field or property name</param>
    /// <param name="value">value to be set</param>
    /// <returns>true if setting successful, false if there is no such writable member</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="memberName"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException">The type of <paramref name="value"/> does not match the member type</exception>
    public bool TrySetValue(object source, string memberName, object? value)
    {
        ValidateThrowIfNeeded(source, memberName);

        if (_isUnsupportedType ||
            !_members.TryGetValue(memberName, out var member) ||
            member.Setter == null)
        {
            return false;
        }

        AssignChecked(member.Setter, member.Type, source, memberName, value);
        return true;
    }

    /// <summary>
    /// Invokes <paramref name="setter"/> after verifying that <paramref name="value"/> is assignable to <paramref name="memberType"/>
    /// </summary>
    /// <param name="setter">compiled member setter</param>
    /// <param name="memberType">declared type of the member</param>
    /// <param name="source">instance to set the member on</param>
    /// <param name="memberName">member name, for error reporting</param>
    /// <param name="value">the new value</param>
    /// <exception cref="InvalidOperationException">The type of <paramref name="value"/> does not match <paramref name="memberType"/></exception>
    internal static void AssignChecked(Action<object, object?> setter, Type memberType, object source, string memberName, object? value)
    {
        // the emitted setter would otherwise fail with an InvalidCastException deep inside dynamic IL
        if (value != null && !memberType.IsInstanceOfType(value))
        {
            throw new InvalidOperationException(
                $"Type mismatch, cannot set member '{memberName}'. Member type = {memberType}, value type = {value.GetType()}");
        }

        setter(source, value);
    }

    private static void ValidateThrowIfNeeded(object source, string memberName)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (memberName == null)
        {
            throw new ArgumentNullException(nameof(memberName));
        }
    }

    private readonly record struct CompiledMember(Type Type, Func<object, object?>? Getter, Action<object, object?>? Setter);
}
