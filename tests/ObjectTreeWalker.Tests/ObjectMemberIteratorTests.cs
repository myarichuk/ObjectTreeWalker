// ReSharper disable TooManyDeclarations
// ReSharper disable ExceptionNotDocumented
// ReSharper disable ExceptionNotDocumented
#pragma warning disable CS8605
#pragma warning disable CS1591

namespace ObjectTreeWalker.Tests
{
    public class ObjectMemberIteratorTests
    {
        public class FooBar
        {
            public int Foo1 { get; set; } = 111;
            public int Foo2 { get; set; } = 222;
            public int Foo3 { get; set; } = 333;
        }

        public class ComplexFooBar
        {
            public int Foo1 { get; set; } = 111;
            public FooBar Obj { get; set; } = new FooBar();
            public int Foo4 { get; set; } = 456;
        }

        public class FooBarFields
        {
            public int Foo1 = 111;
            public int Foo2 = 222;
            public int Foo3 = 333;
        }

        public class ComplexFooBarFields
        {
            public int Foo1 = 111;
            public FooBarFields Obj = new FooBarFields();
            public int Foo4 = 456;
        }

        [Fact]
        public void Can_iterate_flat_class()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();


            iterator.Traverse(new FooBar(), (in MemberAccessor accessor) => propertyValues.Add((int)accessor.GetValue()));

            Assert.Collection(propertyValues,
                item => Assert.Equal(111, item),
                item => Assert.Equal(222, item),
                item => Assert.Equal(333, item));
        }

        [Fact]
        public void Can_iterate_flat_class_with_backing_fields()
        {
            for (int i = 0; i < 2; i++)
            {
                var iterator = new ObjectMemberIterator(false);
                var propertyValues = new List<int>();

                iterator.Traverse(new FooBar(), (in MemberAccessor accessor) => propertyValues.Add((int)accessor.GetValue()));
                Assert.Equal(6, propertyValues.Count);
            }
        }

        [Fact]
        public void Can_iterate_complex_class()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            iterator.Traverse(new ComplexFooBar(), (in MemberAccessor accessor) =>
            {
                var value = accessor.GetValue();

                // all of "primitive" properties are of type int so this is correct
                propertyValues.Add((int)value!);
            });

            Assert.Collection(propertyValues,
                item => Assert.Equal(111, item),
                item => Assert.Equal(456, item),
                item => Assert.Equal(111, item),
                item => Assert.Equal(222, item),
                item => Assert.Equal(333, item));
        }

        public class IterationContext
        {
            public int ValueSum { get; set; }
        }

        [Fact]
        public void Can_maintain_class_context_when_iterating()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            var aggregationResult =
                iterator.Traverse(new ComplexFooBar(),
                    (ref IterationContext ctx, in MemberAccessor accessor) =>
                    {
                        var value = accessor.GetValue();
                        ctx.ValueSum += (int)value!;
                        propertyValues.Add((int)value!);
                    });

            Assert.Equal(propertyValues.Sum(x => x), aggregationResult.ValueSum);
        }

        public struct IterationContextStruct
        {
            public int ValueSum { get; set; }
        }

        [Fact]
        public void Can_maintain_struct_context_when_iterating()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            var aggregationResult =
                iterator.Traverse(new ComplexFooBar(),
                    (ref IterationContextStruct ctx, in MemberAccessor accessor) =>
                    {
                        var value = accessor.GetValue();
                        ctx.ValueSum += (int)value!;
                        propertyValues.Add((int)value!);
                    });

            Assert.Equal(propertyValues.Sum(x => x), aggregationResult.ValueSum);
        }

        [Fact]
        public void Can_see_member_types_when_iterating()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            iterator.Traverse(new ComplexFooBar(),
                (in MemberAccessor accessor) =>
                    Assert.Equal(typeof(int), accessor.Type));
        }

        [Fact]
        public void Can_see_property_path_when_iterating()
        {
            var iterator = new ObjectMemberIterator();
            var propertyPaths = new List<IEnumerable<string>>();

            iterator.Traverse(new ComplexFooBar(),
                (in MemberAccessor accessor) => 
                    propertyPaths.Add(accessor.PropertyPath));

            Assert.Collection(propertyPaths, 
                propertyPath => 
                    Assert.Equal("Foo1", string.Join(",",propertyPath)),
                propertyPath => 
                    Assert.Equal("Foo4", string.Join(",",propertyPath)),
                propertyPath => 
                    Assert.Equal("Obj,Foo1", string.Join(",",propertyPath)),
                propertyPath => 
                    Assert.Equal("Obj,Foo2", string.Join(",",propertyPath)),
                propertyPath => 
                    Assert.Equal("Obj,Foo3", string.Join(",",propertyPath))
            );
        }

        [Fact]
        public void Can_skip_some_members()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            iterator.Traverse(new ComplexFooBar(), (in MemberAccessor accessor) =>
            {
                var value = accessor.GetValue();

                // all of "primitive" properties are of type int so this is correct
                propertyValues.Add((int)value!);
            }, (in MemberAccessor accessor) => accessor.Name != "Foo1");

            Assert.Collection(propertyValues,
                item => Assert.Equal(456, item),
                item => Assert.Equal(222, item),
                item => Assert.Equal(333, item));
        }

        [Fact]
        public void Can_skip_member_types()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();


            iterator.Traverse(new ComplexFooBar(), (in MemberAccessor accessor) =>
            {
                var value = accessor.GetValue();

                // all of "primitive" properties are of type int so this is correct
                propertyValues.Add((int)value!);
            }, (in MemberAccessor accessor) => accessor.MemberType != MemberType.Property);

            Assert.Empty(propertyValues);

            propertyValues.Clear();

            iterator.Traverse(new ComplexFooBarFields(), (in MemberAccessor accessor) =>
            {
                var value = accessor.GetValue();

                // all of "primitive" properties are of type int so this is correct
                propertyValues.Add((int)value!);
            }, (in MemberAccessor accessor) => accessor.MemberType != MemberType.Property);

            Assert.Collection(propertyValues,
                item => Assert.Equal(111, item),
                item => Assert.Equal(456, item),
                item => Assert.Equal(111, item),
                item => Assert.Equal(222, item),
                item => Assert.Equal(333, item));
        }
    }
}
