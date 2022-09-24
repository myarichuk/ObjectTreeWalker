using System.Reflection;
// ReSharper disable InconsistentNaming
// ReSharper disable ExceptionNotDocumented
// ReSharper disable ExceptionNotDocumentedOptional
// ReSharper disable ComplexConditionExpression
#pragma warning disable CS8604
#pragma warning disable CS0414
#pragma warning disable CS1591

namespace ObjectTreeWalker.Tests
{
    public class ObjectAccessorTests
    {
        internal class PublicFooBar
        {
            public int Foo { get; set; } = 123;

            public int Test123 = 555;

            public PrivateFooBar Bar { get; set; } = new();
        }

        internal class PublicFooBarNoSet
        {
            public int Foo { get; } = 123;

            public int Test123 = 555;

            public PrivateFooBar Bar { get; set; } = new();
        }

        internal class PublicFooBarNoGet
        {
            public int Foo
            {
                set => _foo = value;
            }

            public int Test123 = 555;
            private int _foo;

            public PrivateFooBar Bar { get; set; } = new();
        }

        internal struct PublicFooBarStruct
        {
            public int Foo { get; set; } = 123;

            public int Test123 = 555;

            public PublicFooBarStruct()
            {
            }

            public PrivateFooBar Bar { get; set; } = new();
        }

        public class FooABC
        {
        }

        public ref struct Xyz
        {
        }

        internal class PrivateFooBar
        {
            // ReSharper disable once UnusedMember.Local
            private int Foo { get; set; } = 123;

            private int Test123 = 555;

            public int Tester { get; set; } = 345;

            public FooABC Bar { get; set; } = new();
        }

        internal struct PrivateFooBarStruct
        {
            // ReSharper disable once UnusedMember.Local
            private int Foo { get; set; } = 123;

            private int Test123 = 555;

            public PrivateFooBarStruct()
            {
            }

            public int Tester { get; set; } = 345;

            public FooABC Bar { get; set; } = new();
        }

        [Fact]
        public void Should_throw_on_ref_struct() =>
            Assert.Throws<ArgumentException>(() => new ObjectAccessor(typeof(Xyz)));

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_get_public_value_type_property(Type type)
        {
            var accessor = new ObjectAccessor(type);

            Assert.True(accessor.TryGetValue(Activator.CreateInstance(type), "Foo", out var value));
            Assert.Equal(123, value);
        }

        [Fact]
        public void Should_fail_get_public_value_no_get()
        {
            var accessor = new ObjectAccessor(typeof(PublicFooBarNoGet));
            Assert.False(accessor.TryGetValue(new PublicFooBarNoGet(), "Foo", out var value));
        }

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_get_public_value_type_field(Type type)
        {
            var accessor = new ObjectAccessor(type);

            Assert.True(accessor.TryGetValue(Activator.CreateInstance(type), "Test123", out var value));
            Assert.Equal(555, value);
        }


        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_get_public_reference_type_property(Type type)
        {
            var accessor = new ObjectAccessor(type);

            Assert.True(accessor.TryGetValue(Activator.CreateInstance(type), "Bar", out var value));
            Assert.IsType<PrivateFooBar>(value);

            Assert.Equal(345, ((PrivateFooBar)value).Tester);
        }

        [Theory]
        [InlineData(typeof(PrivateFooBar))]
        [InlineData(typeof(PrivateFooBarStruct))]
        public void Can_get_private_value_type_property(Type type)
        {
            var accessor = new ObjectAccessor(type);

            Assert.True(accessor.TryGetValue(Activator.CreateInstance(type), "Foo", out var value));
            Assert.Equal(123, value);
        }


        [Fact]
        public void Can_get_private_value_type_field()
        {
            var accessor = new ObjectAccessor(typeof(PrivateFooBar));

            Assert.True(accessor.TryGetValue(new PrivateFooBar(), "Test123", out var value));
            Assert.Equal(555, value);
        }

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_set_public_value_type_property(Type type)
        {
            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            accessor.TrySetValue(obj!, "Foo", 345);

            Assert.Equal(345, ((dynamic)obj!).Foo);
        }

        [Fact]
        public void Should_fail_setting_property_without_set()
        {
            var accessor = new ObjectAccessor(typeof(PublicFooBarNoSet));
            var obj = new PublicFooBarNoSet();

            Assert.False(accessor.TrySetValue(obj, "Foo", 345));
        }

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_set_public_ref_type_property(Type type)
        {
            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            Assert.NotNull(((dynamic)obj!).Bar); //sanity check
            accessor.TrySetValue(obj, "Bar", null);
            Assert.Null(((dynamic)obj).Bar);
        }

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_handle_null_on_value_type_property(Type type)
        {
            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            accessor.TrySetValue(obj!, "Foo", null);

            Assert.Equal(0, ((dynamic)obj!).Foo);
        }

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_handle_null_on_value_type_field(Type type)
        {
            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            accessor.TrySetValue(obj!, "Test123", null);

            Assert.Equal(0, ((dynamic)obj!).Test123);
        }

        [Theory]
        [InlineData(typeof(PublicFooBar))]
        [InlineData(typeof(PublicFooBarStruct))]
        public void Can_set_value_type_field(Type type)
        {
            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            accessor.TrySetValue(obj!, "Test123", 345);

            Assert.Equal(345, ((dynamic)obj!).Test123);
        }

        [Theory]
        [InlineData(typeof(PrivateFooBar))]
        [InlineData(typeof(PrivateFooBarStruct))]
        public void Can_set_private_value_type_property(Type type)
        {
            var propertyInfo = type.GetProperty("Foo", BindingFlags.NonPublic | BindingFlags.Instance) ??
                               throw new InvalidOperationException("This is not supposed to happen!");

            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            accessor.TrySetValue(obj!, "Foo", 345);

            Assert.Equal(345, (int)propertyInfo.GetValue(obj)!);
        }

        [Theory]
        [InlineData(typeof(PrivateFooBar))]
        [InlineData(typeof(PrivateFooBarStruct))]
        public void Can_set_private_ref_type_property(Type type)
        {
            var propertyInfo = type.GetProperty("Bar", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public) ??
                               throw new InvalidOperationException("This is not supposed to happen!");

            var accessor = new ObjectAccessor(type);
            var obj = Activator.CreateInstance(type);

            Assert.NotNull(propertyInfo.GetValue(obj));

            accessor.TrySetValue(obj!, "Bar", null);

            Assert.Null(propertyInfo.GetValue(obj));
        }
    }
}
