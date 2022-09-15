using System.Reflection;

namespace ObjectTreeWalker.Tests
{
	public class Basics
	{
		internal class PublicFooBar
		{
			public int Foo { get; set; } = 123;
		}

		internal class PrivateFooBar
		{
			private int Foo { get; set; } = 123;
		}

		[Fact]
		public void Can_get_public_property()
		{
			var propertyInfo = typeof(PublicFooBar).GetProperty(nameof(PublicFooBar.Foo)) ??
							   throw new InvalidOperationException("This is not supposed to happen!");

			var accessor = new PropertyAccessor<PublicFooBar>(propertyInfo);

			Assert.Equal(123, accessor.GetValue(new PublicFooBar()));
		}

		[Fact]
		public void Can_get_private_property()
		{
			var propertyInfo = typeof(PrivateFooBar).GetProperty("Foo", BindingFlags.NonPublic | BindingFlags.Instance) ??
							   throw new InvalidOperationException("This is not supposed to happen!");

			var accessor = new PropertyAccessor<PrivateFooBar>(propertyInfo);

			Assert.Equal(123, accessor.GetValue(new PrivateFooBar()));
		}


		[Fact]
		public void Can_set_public_property()
		{
			var propertyInfo = typeof(PublicFooBar).GetProperty(nameof(PublicFooBar.Foo)) ??
							   throw new InvalidOperationException("This is not supposed to happen!");

			var accessor = new PropertyAccessor<PublicFooBar>(propertyInfo);
			var obj = new PublicFooBar();

			accessor.SetValue(obj, 345);

			Assert.Equal(345, obj.Foo);
		}

		[Fact]
		public void Can_set_private_property()
		{
			var propertyInfo = typeof(PrivateFooBar).GetProperty("Foo", BindingFlags.NonPublic | BindingFlags.Instance) ??
							   throw new InvalidOperationException("This is not supposed to happen!");

			var accessor = new PropertyAccessor<PrivateFooBar>(propertyInfo);
			var obj = new PrivateFooBar();

			accessor.SetValue(obj, 345);

			Assert.Equal(345, (int)propertyInfo.GetValue(obj)!);
		}
	}
}
