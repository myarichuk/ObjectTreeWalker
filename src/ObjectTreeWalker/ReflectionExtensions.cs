using System.Reflection;

namespace ObjectTreeWalker
{
    /// <summary>
    /// Misc helper method that deal with reflection
    /// </summary>
    internal static class ReflectionExtensions
    {
        /// <summary>
        /// Fetch underlying type of the <see cref="MemberInfo"/> class (it is a base class)
        /// </summary>
        /// <param name="member">instance of the base class</param>
        /// <returns>Instance of the concrete underlying type</returns>
        /// <exception cref="ArgumentException">The underlying type of the <see cref="MemberInfo"/>  must be EventInfo, FieldInfo, MethodInfo or PropertyInfo</exception>
        public static Type? GetUnderlyingType(this MemberInfo member) =>
            member.MemberType switch
            {
                MemberTypes.Event => ((EventInfo)member).EventHandlerType,
                MemberTypes.Field => ((FieldInfo)member).FieldType,
                MemberTypes.Method => ((MethodInfo)member).ReturnType,
                MemberTypes.Property => ((PropertyInfo)member).PropertyType,
                _ => throw new ArgumentException("Input MemberInfo must be if type EventInfo, FieldInfo, MethodInfo, or PropertyInfo"),
            };
    }
}
