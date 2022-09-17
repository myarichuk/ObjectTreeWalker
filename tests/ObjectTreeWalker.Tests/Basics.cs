using System.Reflection;

namespace ObjectTreeWalker.Tests
{
	public class Basics
	{
		internal class PublicFooBar
		{
			public int Foo { get; set; } = 123;

			public int Test123 = 555;

			public PrivateFooBar Bar { get; set; } = new();
		}

		public class Foo
		{

		}

		internal class PrivateFooBar
		{
			// ReSharper disable once UnusedMember.Local
			private int Foo { get; set; } = 123;

			private int Test123 = 555;

			public int Tester { get; set; } = 345;

			public Foo Bar { get; set; } = new();
		}

		[Fact]
		public void Can_get_public_value_type_property()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));

			Assert.True(accessor.TryGetValue(new PublicFooBar(), "Foo", out var value));
			Assert.Equal(123, value);
		}

		[Fact]
		public void Can_get_public_value_type_field()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));

			Assert.True(accessor.TryGetValue(new PublicFooBar(), "Test123", out var value));
			Assert.Equal(555, value);
		}


		[Fact]
		public void Can_get_public_reference_type_property()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));

			Assert.True(accessor.TryGetValue(new PublicFooBar(), "Bar", out var value));
			Assert.IsType<PrivateFooBar>(value);

			Assert.Equal(345, ((PrivateFooBar)value).Tester);
		}

		[Fact]
		public void Can_get_private_value_type_property()
		{
			var accessor = new ObjectAccessor(typeof(PrivateFooBar));

			Assert.True(accessor.TryGetValue(new PrivateFooBar(), "Foo", out var value));
			Assert.Equal(123, value);
		}


		[Fact]
		public void Can_get_private_value_type_field()
		{
			var accessor = new ObjectAccessor(typeof(PrivateFooBar));

			Assert.True(accessor.TryGetValue(new PrivateFooBar(), "Test123", out var value));
			Assert.Equal(555, value);
		}

		[Fact]
		public void Can_set_public_value_type_property()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));
			var obj = new PublicFooBar();

			accessor.TrySetValue(obj, "Foo", 345);

			Assert.Equal(345, obj.Foo);
		}

		[Fact]
		public void Can_set_public_ref_type_property()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));
			var obj = new PublicFooBar();

			Assert.NotNull(obj.Bar); //sanity check
			accessor.TrySetValue(obj, "Bar", null);
			Assert.Null(obj.Bar);
		}

		[Fact]
		public void Can_handle_null_on_value_type_property()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));
			var obj = new PublicFooBar();

			accessor.TrySetValue(obj, "Foo", null);

			Assert.Equal(0, obj.Foo);
		}

		[Fact]
		public void Can_handle_null_on_value_type_field()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));
			var obj = new PublicFooBar();

			accessor.TrySetValue(obj, "Test123", null);

			Assert.Equal(0, obj.Test123);
		}

		[Fact]
		public void Can_set_value_type_field()
		{
			var accessor = new ObjectAccessor(typeof(PublicFooBar));
			var obj = new PublicFooBar();

			accessor.TrySetValue(obj, "Test123", 345);

			Assert.Equal(345, obj.Test123);
		}

		[Fact]
		public void Can_set_private_value_type_property()
		{
			var propertyInfo = typeof(PrivateFooBar).GetProperty("Foo", BindingFlags.NonPublic | BindingFlags.Instance) ??
							   throw new InvalidOperationException("This is not supposed to happen!");

			var accessor = new ObjectAccessor(typeof(PrivateFooBar));
			var obj = new PrivateFooBar();

			accessor.TrySetValue(obj, "Foo", 345);

			Assert.Equal(345, (int)propertyInfo.GetValue(obj)!);
		}

		[Fact]
		public void Can_set_private_ref_type_property()
		{
			var propertyInfo = typeof(PrivateFooBar).GetProperty("Bar", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public) ??
							   throw new InvalidOperationException("This is not supposed to happen!");

			var accessor = new ObjectAccessor(typeof(PrivateFooBar));
			var obj = new PrivateFooBar();

			Assert.NotNull(propertyInfo.GetValue(obj));

			accessor.TrySetValue(obj, "Bar", null);

			Assert.Null(propertyInfo.GetValue(obj));
		}
	}
}
