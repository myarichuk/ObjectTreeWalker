// ReSharper disable TooManyDeclarations
// ReSharper disable ExceptionNotDocumented
// ReSharper disable ExceptionNotDocumented

using System.Runtime.Serialization;

#pragma warning disable CS8605
#pragma warning disable CS1591

namespace ObjectTreeWalker.Tests
{
    public class ObjectMemberIteratorTests
    {
        public class JustAFoobarObj
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }
        }

        public struct JustAFoobarStruct
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }
        }

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

        public class ComplexObjWithString
        {
            public string Foo { get; set; } = "B";

            public ObjWithString Bar { get; set; } = new();
        }

        public class ObjWithString
        {
            public string? StringProperty { get; } = "A";
            public string StringProperty2 { get; } = "C";
        }

        [Fact]
        public void Can_manipulate_objects_with_strings()
        {
            var iterator = new ObjectMemberIterator();
            var result = string.Empty;

            iterator.Traverse(new ComplexObjWithString(),
                (in MemberAccessor accessor) =>
                    result += (string)accessor.GetValue()!);

            Assert.Equal("BAC", result);
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

        [Theory]
        [InlineData(typeof(JustAFoobarObj))]
        [InlineData(typeof(JustAFoobarStruct))]
        public void Can_iterate_uninitialized_object(Type typeOfObject)
        {
            var emptyInstance = FormatterServices.GetUninitializedObject(typeOfObject);
            var iterator = new ObjectMemberIterator();

            iterator.Traverse(emptyInstance, (in MemberAccessor accessor) =>
            {
                if (accessor.Name.Contains("Num"))
                {
                    //existing value should be null
                    var value = (int)accessor.GetValue();
                    Assert.Equal(0, value);
                    accessor.SetValue(5);
                }

                if (accessor.Name.Contains("String"))
                {
                    var value = (string)accessor.GetValue()!;
                    Assert.Null(value);
                    accessor.SetValue("abc");
                }
            });

            var objectAsDynamic = (dynamic)emptyInstance;

            Assert.Equal(5, objectAsDynamic.NumProperty);
            Assert.Equal("abc", objectAsDynamic.StringProperty);
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
