using System.Reflection;
using Sigil;

namespace ObjectTreeWalker;

/// <summary>
/// Emits fast, boxed getter/setter delegates for instance fields and properties.
/// </summary>
// inspired by https://www.codeproject.com/Articles/14560/Fast-Dynamic-Property-Field-Accessors
internal static class MemberAccessorEmitter
{
    /// <summary>
    /// Emits a getter for an instance field or a readable instance property
    /// </summary>
    /// <param name="member">the field or property to read</param>
    /// <returns>a delegate taking the (possibly boxed) instance and returning the (boxed) member value</returns>
    /// <exception cref="ArgumentException"><paramref name="member"/> is neither a field nor a property</exception>
    public static Func<object, object?> CreateGetter(MemberInfo member)
    {
        var declaringType = member.DeclaringType!;
        var emitter = Emit<Func<object, object?>>.NewDynamicMethod(GetMethodName("Get", member));

        LoadInstance(emitter, declaringType);

        Type memberType;
        switch (member)
        {
            case FieldInfo field:
                emitter.LoadField(field);
                memberType = field.FieldType;
                break;
            case PropertyInfo { GetMethod: { } getMethod } property:
                Call(emitter, getMethod, declaringType);
                memberType = property.PropertyType;
                break;
            default:
                throw new ArgumentException($"Cannot emit a getter for {member.MemberType} {member.Name}", nameof(member));
        }

        if (memberType.IsValueType)
        {
            emitter.Box(memberType);
        }

        emitter.Return();
        return emitter.CreateDelegate();
    }

    /// <summary>
    /// Emits a setter for an instance field or a writable instance property.
    /// Setting <see langword="null"/> to a value-type member stores its default value.
    /// </summary>
    /// <param name="member">the field or property to write</param>
    /// <returns>a delegate taking the (possibly boxed) instance and the (boxed) new value</returns>
    /// <exception cref="ArgumentException"><paramref name="member"/> is neither a field nor a property</exception>
    /// <remarks>When the instance is a boxed struct the box is mutated in place.</remarks>
    public static Action<object, object?> CreateSetter(MemberInfo member)
    {
        var declaringType = member.DeclaringType!;
        var memberType = member.GetUnderlyingType()!;
        var emitter = Emit<Action<object, object?>>.NewDynamicMethod(GetMethodName("Set", member));

        LoadInstance(emitter, declaringType);

        if (memberType.IsValueType)
        {
            // null -> default(T): a fresh local is zero-initialized, so loading it yields default(T)
            var defaultValue = emitter.DeclareLocal(memberType);
            var notNull = emitter.DefineLabel();
            var store = emitter.DefineLabel();

            emitter.LoadArgument(1);
            emitter.BranchIfTrue(notNull);
            emitter.LoadLocal(defaultValue);
            emitter.Branch(store);

            emitter.MarkLabel(notNull);
            emitter.LoadArgument(1);
            emitter.UnboxAny(memberType);

            emitter.MarkLabel(store);
        }
        else
        {
            emitter.LoadArgument(1);
            emitter.CastClass(memberType);
        }

        switch (member)
        {
            case FieldInfo field:
                emitter.StoreField(field);
                break;
            case PropertyInfo { SetMethod: { } setMethod }:
                Call(emitter, setMethod, declaringType);
                break;
            default:
                throw new ArgumentException($"Cannot emit a setter for {member.MemberType} {member.Name}", nameof(member));
        }

        emitter.Return();
        return emitter.CreateDelegate();
    }

    private static void LoadInstance<TDelegate>(Emit<TDelegate> emitter, Type declaringType)
    {
        emitter.LoadArgument(0);
        if (declaringType.IsValueType)
        {
            // a pointer into the box: struct members are read from/written to the boxed instance itself
            emitter.Unbox(declaringType);
        }
        else
        {
            emitter.CastClass(declaringType);
        }
    }

    private static void Call<TDelegate>(Emit<TDelegate> emitter, MethodInfo method, Type declaringType)
    {
        if (method.IsVirtual && !declaringType.IsValueType)
        {
            emitter.CallVirtual(method);
        }
        else
        {
            emitter.Call(method);
        }
    }

    private static string GetMethodName(string prefix, MemberInfo memberInfo) =>
        $"{prefix}_{memberInfo.DeclaringType?.FullName}_{memberInfo.Name}";
}
