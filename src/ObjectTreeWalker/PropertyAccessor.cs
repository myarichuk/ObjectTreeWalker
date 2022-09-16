using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sigil;

namespace ObjectTreeWalker;

/// <summary>
/// A helper class that replaces reflection access to properties and fields
/// </summary>
// inspired by https://www.codeproject.com/Articles/14560/Fast-Dynamic-Property-Field-Accessors
internal class PropertyAccessor
{
	private static readonly ConcurrentDictionary<Type, MethodInfo> GetDefaultCache = new();

	private readonly Type _objectType;
	private readonly Dictionary<string, Func<object, object>> _getPropertyMethods = new();
	private readonly Dictionary<string, Action<object, object>> _setPropertyMethods = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="PropertyAccessor"/> class
	/// </summary>
	/// <param name="objectType">type of the object to prepare access of it's properties</param>
	public PropertyAccessor(Type objectType)
	{
		_objectType = objectType;
		foreach (var propertyInfo in objectType.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
		{
			_getPropertyMethods.Add(propertyInfo.Name, CreateGetPropertyFunc(propertyInfo));
			_setPropertyMethods.Add(propertyInfo.Name, CreateSetPropertyFunc(propertyInfo));
		}
	}

	public bool TryGetValue(object source, string propertyName, out object? value)
	{
		if (propertyName == null)
		{
			throw new ArgumentNullException(nameof(propertyName));
		}

		value = default;
		if (!_getPropertyMethods.TryGetValue(propertyName, out var getterFunc))
		{
			return false;
		}

		value = getterFunc(source);
		return true;
	}

	public bool TrySetValue(object source, string propertyName, object? value)
	{
		if (propertyName == null)
		{
			throw new ArgumentNullException(nameof(propertyName));
		}

		if (!_setPropertyMethods.TryGetValue(propertyName, out var setterFunc))
		{
			return false;
		}

		setterFunc(source, value);

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static T? GetDefault<T>() => default;

	private Func<object, object> CreateGetPropertyFunc(PropertyInfo propertyInfo)
	{
		var className = propertyInfo.ReflectedType?.AssemblyQualifiedName ?? string.Empty;
		var emitter = Emit<Func<object, object>>.NewDynamicMethod($"Get{className}{propertyInfo.Name}");

		emitter.LoadArgument(0); // this
		emitter.CastClass(_objectType);
		emitter.CallVirtual(propertyInfo.GetMethod);

		if (propertyInfo.PropertyType.IsValueType)
		{
			emitter.Box(propertyInfo.PropertyType);
		}

		emitter.Return();

		return emitter.CreateDelegate();
	}

	private Action<object, object> CreateSetPropertyFunc(PropertyInfo propertyInfo)
	{
		var className = propertyInfo.ReflectedType?.AssemblyQualifiedName ?? string.Empty;
		var emitter = Emit<Action<object, object>>.NewDynamicMethod($"Set{className}{propertyInfo.Name}");
		var afterLabel = emitter.DefineLabel("after");

		if (propertyInfo.PropertyType.IsValueType)
		{
			emitter.LoadArgument(1);
			emitter.BranchIfTrue("after");

			var getDefaultConcrete =
				GetDefaultCache.GetOrAdd(
					propertyInfo.PropertyType,
					t => typeof(PropertyAccessor)
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
		emitter.CastClass(_objectType);

		emitter.LoadArgument(1); // property value

		if (propertyInfo.PropertyType.IsValueType)
		{
			emitter.UnboxAny(propertyInfo.PropertyType);
		}
		else
		{
			emitter.CastClass(propertyInfo.PropertyType);
		}

		emitter.CallVirtual(propertyInfo.SetMethod);
		emitter.Return();

		return emitter.CreateDelegate();
	}
}
