using System.Reflection;
using System.Reflection.Emit;

namespace ObjectTreeWalker;

/// <summary>
/// Emits fast, boxed getter/setter delegates for instance fields and properties, in raw IL.
/// </summary>
/// <remarks>
/// The dynamic methods are hosted in this assembly's module with visibility checks skipped, so they can
/// read and write private members (and readonly fields) of any type, exactly like the runtime's own
/// serializers do.
/// </remarks>
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
        var method = CreateMethod("Get", member, typeof(object), typeof(object));
        var il = method.GetILGenerator();

        EmitLoadInstance(il, declaringType);

        Type memberType;
        switch (member)
        {
            case FieldInfo field:
                il.Emit(OpCodes.Ldfld, field);
                memberType = field.FieldType;
                break;
            case PropertyInfo { GetMethod: { } getMethod } property:
                EmitCall(il, getMethod, declaringType);
                memberType = property.PropertyType;
                break;
            default:
                throw new ArgumentException($"Cannot emit a getter for {member.MemberType} {member.Name}", nameof(member));
        }

        if (memberType.IsValueType)
        {
            il.Emit(OpCodes.Box, memberType);
        }

        il.Emit(OpCodes.Ret);
        return (Func<object, object?>)method.CreateDelegate(typeof(Func<object, object?>));
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
        var method = CreateMethod("Set", member, typeof(void), typeof(object), typeof(object));
        var il = method.GetILGenerator();

        EmitLoadInstance(il, declaringType);

        if (memberType.IsValueType)
        {
            // null -> default(T): locals are zero-initialized, so loading a fresh local yields default(T)
            var defaultValue = il.DeclareLocal(memberType);
            var notNull = il.DefineLabel();
            var store = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brtrue_S, notNull);
            il.Emit(OpCodes.Ldloc, defaultValue);
            il.Emit(OpCodes.Br_S, store);

            il.MarkLabel(notNull);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, memberType);

            il.MarkLabel(store);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, memberType);
        }

        switch (member)
        {
            case FieldInfo field:
                il.Emit(OpCodes.Stfld, field);
                break;
            case PropertyInfo { SetMethod: { } setMethod }:
                EmitCall(il, setMethod, declaringType);
                break;
            default:
                throw new ArgumentException($"Cannot emit a setter for {member.MemberType} {member.Name}", nameof(member));
        }

        il.Emit(OpCodes.Ret);
        return (Action<object, object?>)method.CreateDelegate(typeof(Action<object, object?>));
    }

    /// <summary>
    /// Creates a dynamic method hosted in this module, with visibility checks skipped
    /// </summary>
    /// <param name="prefix">method name prefix (shows up in stack traces)</param>
    /// <param name="member">member the method is for</param>
    /// <param name="returnType">return type</param>
    /// <param name="parameterTypes">parameter types</param>
    /// <returns>the dynamic method</returns>
    internal static DynamicMethod CreateMethod(string prefix, MemberInfo member, Type returnType, params Type[] parameterTypes) =>
        new(
            $"{prefix}_{(member as Type ?? member.DeclaringType)?.FullName}_{member.Name}",
            returnType,
            parameterTypes,
            typeof(MemberAccessorEmitter).Module,
            skipVisibility: true);

    private static void EmitLoadInstance(ILGenerator il, Type declaringType)
    {
        il.Emit(OpCodes.Ldarg_0);

        // a pointer into the box: struct members are read from/written to the boxed instance itself
        il.Emit(declaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, declaringType);
    }

    private static void EmitCall(ILGenerator il, MethodInfo method, Type declaringType) =>
        il.Emit(method.IsVirtual && !declaringType.IsValueType ? OpCodes.Callvirt : OpCodes.Call, method);
}
