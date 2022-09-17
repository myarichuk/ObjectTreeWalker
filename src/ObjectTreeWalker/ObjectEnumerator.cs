using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ObjectTreeWalker
{
	/// <summary>
	/// Exposes helper function to enumerate types and fetch member graph
	/// </summary>
	internal static class ObjectEnumerator
	{
		private static readonly ConcurrentDictionary<Type, ObjectGraph> ObjectGraphCache = new();

		/// <summary>
		/// Enumerate and fetch property/field graph of the type
		/// </summary>
		/// <param name="type">the type to enumerate</param>
		/// <returns>object graph</returns>
		/// <exception cref="OverflowException">The object graph cache contains too many elements.</exception>
		public static ObjectGraph Enumerate(Type type) =>
			ObjectGraphCache.GetOrAdd(type, t =>
			{
				var roots = EnumerateChildMembers(t).Select(memberData =>
					EnumerateMember(memberData.mi, null, memberData.canGet, memberData.canSet));
				return new ObjectGraph(t, roots);
			});

		private static ObjectGraphNode EnumerateMember(MemberInfo member, ObjectGraphNode? parent, bool canGet, bool canSet)
		{
			var ogn = new ObjectGraphNode(member, parent)
			{
				CanGet = canGet,
				CanSet = canSet,
			};

			var children = EnumerateChildMembers(member.GetUnderlyingType()!)
				.Select(memberData =>
					new ObjectGraphNode(memberData.mi, ogn)
					{
						CanGet = memberData.canGet,
						CanSet = memberData.canSet,
					});

			ogn.Children.AddRange(children);
			return ogn;
		}

		private static IEnumerable<(MemberInfo mi, bool canGet, bool canSet)> EnumerateChildMembers(IReflect type)
		{
			foreach (var property in type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
			{
				yield return (property, property.GetMethod != null, property.SetMethod != null);
			}

			foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
			{
				if (field.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
				{
					yield return (field, true, true);
				}
			}
		}
	}
}
