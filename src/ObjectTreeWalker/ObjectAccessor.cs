using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sigil;
// ReSharper disable ComplexConditionExpression

namespace ObjectTreeWalker;

/// <summary>
/// A helper class that replaces reflection access to properties and fields
/// </summary>
/// <remarks>This class does not support static fields and properties </remarks>
// inspired by https://www.codeproject.com/Articles/14560/Fast-Dynamic-Property-Field-Accessors
internal class ObjectAccessor
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> GetDefaultCache = new();
    private readonly Type _objectType;
    private readonly bool _isStringAccessor;
    private readonly Dictionary<string, Func<object, object>> _getPropertyMethods = new();
    private readonly Dictionary<string, Action<object, object>> _setPropertyMethods = new();

    private readonly Dictionary<string, Func<object, object>> _getFieldMethods = new();
    private readonly Dictionary<string, Action<object, object>> _setFieldMethods = new();

    private readonly Dictionary<string, PropertyInfo> _propertyCache = new();
    private readonly Dictionary<string, FieldInfo> _fieldCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectAccessor"/> class
    /// </summary>
    /// <param name="objectType">type of the object to prepare access of it's properties</param>
    /// <exception cref="ArgumentException">The type <paramref name="objectType"/> is a ref struct, it is not supported by <see cref="ObjectAccessor"/></exception>
    public ObjectAccessor(Type objectType)
    {
        if (objectType.IsPointer)
        {
            throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} is a pointer, it is not supported by {nameof(ObjectAccessor)}.");
        }

#if NET6_0_OR_GREATER
        if (objectType.IsValueType && objectType.IsByRefLike)
        {
            throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} is a ref struct, it is not supported by {nameof(ObjectAccessor)}.");
        }
#endif

        _objectType = objectType;

        if (objectType == typeof(string))
        {
            _isStringAccessor = true;
            return;
        }

        foreach (var propertyInfo in objectType.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            if (propertyInfo.PropertyType.IsPointer)
            {
                throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} contains a pointer property, it is not supported by {nameof(ObjectAccessor)}.");
            }

            AddPropertyToCacheIfNeeded(propertyInfo);
            if (propertyInfo.GetMethod != null)
            {
                _getPropertyMethods.Add(propertyInfo.Name, CreateGetPropertyFunc(propertyInfo));
            }

            if (propertyInfo.SetMethod != null)
            {
                _setPropertyMethods.Add(propertyInfo.Name, CreateSetPropertyFunc(propertyInfo));
            }
        }

        foreach (var fieldInfo in objectType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            if (fieldInfo.FieldType.IsPointer)
            {
                throw new ArgumentException($"The type {objectType.AssemblyQualifiedName} contains a pointer field, it is not supported by {nameof(ObjectAccessor)}.");
            }

            AddFieldToCacheIfNeeded(fieldInfo);
            _getFieldMethods.Add(fieldInfo.Name, CreateGetFieldFunc(fieldInfo));
            _setFieldMethods.Add(fieldInfo.Name, CreateSetFieldFunc(fieldInfo));
        }
    }

    /// <summary>
    /// Try fetching the field or a property from the object
    /// </summary>
    /// <param name="source">object to work on</param>
    /// <param name="memberName">field or property name</param>
    /// <param name="value">value fetched or a default value</param>
    /// <returns>true if fetching successful, false otherwise</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="memberName"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException">Internally cached delegate is null but it shouldn't be. This is not supposed to happen and should be reported.</exception>
    public bool TryGetValue(object source, string memberName, out object? value)
    {
        ValidateThrowIfNeeded(source, memberName);

        value = default;

        // strings have no valid members to access!
        if (_isStringAccessor)
        {
            return false;
        }

        if (_getPropertyMethods.TryGetValue(memberName, out var getterPropertyFunc))
        {
            return TryExecuteGetter(source, out value, getterPropertyFunc);
        }

        return _getFieldMethods.TryGetValue(memberName, out var getterFieldFunc) &&
               TryExecuteGetter(source, out value, getterFieldFunc);
    }

    /// <summary>
    /// Try setting field or property in the object
    /// </summary>
    /// <param name="source">object to work on</param>
    /// <param name="memberName">field or property name</param>
    /// <param name="value">value to be set</param>
    /// <returns>true if setting successful, false otherwise</returns>
    /// <exception cref="InvalidOperationException">Failed to find property in the property cache..</exception>
    public bool TrySetValue(object source, string memberName, object? value)
    {
        ValidateThrowIfNeeded(source, memberName);

        // strings have no valid members to access!
        if (_isStringAccessor)
        {
            return false;
        }

        if (_setPropertyMethods.TryGetValue(memberName, out var setterPropertyFunc))
        {
            if (!_propertyCache.TryGetValue(memberName, out var propertyInfo))
            {
                throw new InvalidOperationException($"Failed to find property {memberName} in property cache. This is not supposed to happen and is likely a bug.");
            }

            // safety precaution
            if (value != null && !propertyInfo.PropertyType.IsInstanceOfType(value))
            {
                throw new InvalidOperationException($"Type mismatch, cannot set member. Property type = {propertyInfo.PropertyType} but value type = {value.GetType()}");
            }

            ExecuteSetter(source, value, setterPropertyFunc);
            return true;
        }

        if (_setFieldMethods.TryGetValue(memberName, out var setterFieldFunc))
        {
            if (!_fieldCache.TryGetValue(memberName, out var fieldInfo))
            {
                throw new InvalidOperationException($"Failed to find field {memberName} in the field cache. This is not supposed to happen and is likely a bug.");
            }

            // safety precaution
            if (value != null && !fieldInfo.FieldType.IsInstanceOfType(value))
            {
                throw new InvalidOperationException($"Type mismatch, cannot set member. Field type = {fieldInfo.FieldType}, value type = {value.GetType()}");
            }

            ExecuteSetter(source, value, setterFieldFunc);
            return true;
        }

        return false;
    }

    private static bool TryExecuteGetter(object source, out object? value, Func<object, object> getterFieldFunc)
    {
        if (getterFieldFunc == null)
        {
            throw new InvalidOperationException(
                "Internally cached delegate is null but it shouldn't be. This is not supposed to happen and should be reported.");
        }

        // ReSharper disable once EventExceptionNotDocumented
        value = getterFieldFunc(source);
        return true;
    }

    private static void ExecuteSetter(object objectInstance, object? newValue, Action<object, object> setter)
    {
        if (setter == null)
        {
            throw new InvalidOperationException(
                "Internally cached delegate is null but it shouldn't be. This is not supposed to happen and should be reported.");
        }

        // ReSharper disable once EventExceptionNotDocumented
        try
        {
            setter(objectInstance, newValue!);
        }
        catch (InvalidCastException e)
        {
            throw new InvalidOperationException("Object accessor failed to execute a setter, perhaps the type of the object specified doesn't match the type of the instance?", e);
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T? GetDefault<T>() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetMethodName(string prefix, MemberInfo memberInfo)
    {
        var className = memberInfo.ReflectedType?.AssemblyQualifiedName ?? string.Empty;
        return $"{prefix}{className}{memberInfo.Name}";
    }

    private Func<object, object> CreateGetFieldFunc(FieldInfo fieldInfo)
    {
        var emitter = Emit<Func<object, object>>.NewDynamicMethod(GetMethodName("Get", fieldInfo));

        emitter.LoadArgument(0); // this
        if (_objectType.IsValueType)
        {
            emitter.Unbox(_objectType);
        }
        else
        {
            emitter.CastClass(_objectType);
        }

        emitter.LoadField(fieldInfo);

        if (fieldInfo.FieldType.IsValueType)
        {
            emitter.Box(fieldInfo.FieldType);
        }

        emitter.Return();
        return emitter.CreateDelegate();
    }

    private Action<object, object> CreateSetFieldFunc(FieldInfo fieldInfo)
    {
        var emitter = Emit<Action<object, object>>.NewDynamicMethod(GetMethodName("Set", fieldInfo));
        var afterLabel = emitter.DefineLabel("after");

        if (fieldInfo.FieldType.IsValueType)
        {
            emitter.LoadArgument(1);
            emitter.BranchIfTrue("after"); // if null it will not branch!

            var getDefaultConcrete =
                GetDefaultCache.GetOrAdd(
                    fieldInfo.FieldType,
                    t => typeof(ObjectAccessor)
                        .GetMethod(
                            nameof(GetDefault),
                            BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(t));

            emitter.Call(getDefaultConcrete);
            emitter.Box(fieldInfo.FieldType);
            emitter.StoreArgument(1);
        }

        emitter.MarkLabel(afterLabel);

        emitter.LoadArgument(0); // this
        if (_objectType.IsValueType)
        {
            emitter.Unbox(_objectType);
        }
        else
        {
            emitter.CastClass(_objectType);
        }

        emitter.LoadArgument(1); // value to save

        if (fieldInfo.FieldType.IsValueType)
        {
            emitter.UnboxAny(fieldInfo.FieldType);
        }
        else
        {
            emitter.CastClass(fieldInfo.FieldType);
        }

        emitter.StoreField(fieldInfo);

        emitter.Return();
        return emitter.CreateDelegate();
    }

    private Func<object, object> CreateGetPropertyFunc(PropertyInfo propertyInfo)
    {
        var emitter = Emit<Func<object, object>>.NewDynamicMethod(GetMethodName("Get", propertyInfo));

        emitter.LoadArgument(0); // this
        if (_objectType.IsValueType)
        {
            emitter.Unbox(_objectType);
        }
        else
        {
            emitter.CastClass(_objectType);
        }

        emitter.Call(propertyInfo.GetMethod);

        if (propertyInfo.PropertyType.IsValueType)
        {
            emitter.Box(propertyInfo.PropertyType);
        }

        emitter.Return();

        return emitter.CreateDelegate();
    }

    private Action<object, object> CreateSetPropertyFunc(PropertyInfo propertyInfo)
    {
        var emitter = Emit<Action<object, object>>.NewDynamicMethod(GetMethodName("Set", propertyInfo));
        var afterLabel = emitter.DefineLabel("after");

        if (propertyInfo.PropertyType.IsValueType)
        {
            emitter.LoadArgument(1);
            emitter.BranchIfTrue("after");

            var getDefaultConcrete =
                GetDefaultCache.GetOrAdd(
                    propertyInfo.PropertyType,
                    t => typeof(ObjectAccessor)
                        .GetMethod(
                            nameof(GetDefault),
                            BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(t));

            emitter.Call(getDefaultConcrete);
            emitter.Box(propertyInfo.PropertyType);
            emitter.StoreArgument(1);
        }

        emitter.MarkLabel(afterLabel);
        emitter.LoadArgument(0); // this
        if (_objectType.IsValueType)
        {
            emitter.Unbox(_objectType);
        }
        else
        {
            emitter.CastClass(_objectType);
        }

        emitter.LoadArgument(1); // property value

        if (propertyInfo.PropertyType.IsValueType)
        {
            emitter.UnboxAny(propertyInfo.PropertyType);
        }
        else
        {
            emitter.CastClass(propertyInfo.PropertyType);
        }

        emitter.Call(propertyInfo.SetMethod);
        emitter.Return();

        return emitter.CreateDelegate();
    }

    private void AddPropertyToCacheIfNeeded(PropertyInfo propertyInfo)
    {
#if NET6_0_OR_GREATER
        _propertyCache.TryAdd(propertyInfo.Name, propertyInfo);
#else
        if (!_propertyCache.ContainsKey(propertyInfo.Name))
        {
            _propertyCache.Add(propertyInfo.Name, propertyInfo);
        }
#endif
    }

    private void AddFieldToCacheIfNeeded(FieldInfo fieldInfo)
    {
#if NET6_0_OR_GREATER
        _fieldCache.TryAdd(fieldInfo.Name, fieldInfo);
#else
        if (!_fieldCache.ContainsKey(fieldInfo.Name))
        {
            _fieldCache.Add(fieldInfo.Name, fieldInfo);
        }
#endif
    }
}
