using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ObjectTreeWalker
{
	internal static class ObjectEnumerator
	{
		private static readonly ConcurrentDictionary<Type, ObjectGraph> ObjectGraphCache = new();

		public static ObjectGraph Enumerate(Type type)
		{
			var roots = EnumerateChildMembers(type).Select(memberData => EnumerateMember(memberData.mi, null, memberData.canGet, memberData.canSet));
			return new ObjectGraph(type, roots);
		}

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
