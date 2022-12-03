// ReSharper disable TooManyDeclarations
// ReSharper disable ExceptionNotDocumented
// ReSharper disable ExceptionNotDocumented

using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

#pragma warning disable CS8605
#pragma warning disable CS1591

namespace ObjectTreeWalker.Tests
{
    public class ObjectMemberIteratorTests
    {
        public class ObjectWithNullable
        {
            public ObjectWithEmbeddedObject? Value { get; set; }
        }

        public class SomeEmbeddedObject
        {
            public string? AnotherStringProperty { get; set; }

            public int AnotherNumProperty { get; set; }

            public decimal DecimalProperty { get; set; }

            public bool BoolProperty { get; set; }
        }

        public struct SomeEmbeddedStruct
        {
            public string? AnotherStringProperty { get; set; }

            public int AnotherNumProperty { get; set; }

            public decimal DecimalProperty { get; set; }

            public bool BoolProperty { get; set; }
        }

        public class ObjectWithEmbeddedObject
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }

            public SomeEmbeddedObject? Embedded { get; set; }
        }

        public class ObjectWithEmbeddedStruct
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }

            public SomeEmbeddedStruct? Embedded { get; set; }
        }

        public struct StructWithEmbeddedObject
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }

            public SomeEmbeddedObject? Embedded { get; set; }
        }

        public struct StructWithEmbeddedStruct
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }

            public SomeEmbeddedStruct? Embedded { get; set; }
        }

        public class JustAFoobarObj
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }
        }

        public class ObjectWithUpcastProperty
        {
            public object Value { get; set; }
        }

        public struct JustAFoobarStruct
        {
            public int NumProperty { get; set; }

            public string? StringProperty { get; set; }
        }

        // recreation of a class in Fasterflect library (https://github.com/buunguyen/fasterflect)
        // essentially, this is an edge case scenario that needs to be supported
        internal class ValueTypeHolder
        {
            /// <summary>
            /// Creates a wrapper for <paramref name="value"/> value type.
            /// </summary>
            /// <param name="value">The value type to be wrapped.
            /// Must be a derivative of <code>ValueType</code>.</param>
            public ValueTypeHolder(object value)
            {
                Value = (ValueType) value;
            }

            /// <summary>
            /// The actual struct wrapped by this instance.
            /// </summary>
            public ValueType Value { get; set; }
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

        public interface IObjectInterface
        {
            string Foo { get; set; }
            ObjWithString Bar { get; set; }
        }

        public class ComplexObjWithString : IObjectInterface
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
        public void Can_iterate_properties_through_interfaces()
        {
            var iterator = new ObjectMemberIterator();
            var result = string.Empty;

            var objectAsInterface = (IObjectInterface)new ComplexObjWithString();

            iterator.Traverse(objectAsInterface,
                (in MemberAccessor accessor) =>
                    result += (string)accessor.GetValue()!);

            Assert.Equal("BAC", result);
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
        public void Can_iterate_wrapped_struct()
        {
            var emptyInstance = (JustAFoobarStruct)FormatterServices.GetUninitializedObject(typeof(JustAFoobarStruct));
            var wrappedInstance = new ValueTypeHolder(emptyInstance);

            var iterator = new ObjectMemberIterator();

            iterator.Traverse(wrappedInstance, (in MemberAccessor accessor) =>
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

            var objectAsDynamic = (dynamic)wrappedInstance.Value;

            Assert.Equal(5, objectAsDynamic.NumProperty);
            Assert.Equal("abc", objectAsDynamic.StringProperty);
        }

        [Fact]
        public void Can_iterate_wrapped_obj()
        {
            var emptyInstance = (JustAFoobarObj)FormatterServices.GetUninitializedObject(typeof(JustAFoobarObj));
            var wrappedInstance = new ObjectWithUpcastProperty{ Value = emptyInstance };

            var iterator = new ObjectMemberIterator();

            iterator.Traverse(wrappedInstance, (in MemberAccessor accessor) =>
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

            var objectAsDynamic = (dynamic)wrappedInstance.Value;

            Assert.Equal(5, objectAsDynamic.NumProperty);
            Assert.Equal("abc", objectAsDynamic.StringProperty);
        }

        private static object CreateEmptyInstance(Type type) =>
            RuntimeHelpers.GetUninitializedObject(type);

        [Theory]
        [InlineData(typeof(ObjectWithEmbeddedObject))]
        [InlineData(typeof(StructWithEmbeddedObject))]
        [InlineData(typeof(StructWithEmbeddedStruct))]
        [InlineData(typeof(ObjectWithEmbeddedStruct))]
        public void Can_iterate_embedded_property(Type type)
        {
            var instance = FormatterServices.GetUninitializedObject(type);
            var iterator = new ObjectMemberIterator();

            iterator.Traverse(instance, (in MemberAccessor accessor) =>
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

                if (accessor.Name.Contains("Embedded"))
                {
                    var embeddedObject = (dynamic)FormatterServices.GetUninitializedObject(accessor.Type);
                    embeddedObject.AnotherNumProperty = 123;
                    accessor.SetValue(embeddedObject);
                }
            });

            var objectAsDynamic = (dynamic)instance;

            Assert.Equal(5, objectAsDynamic.NumProperty);
            Assert.Equal("abc", objectAsDynamic.StringProperty);
            Assert.Equal(123, objectAsDynamic.Embedded?.AnotherNumProperty ?? 0);
        }

        [Fact]
        public void Can_iterate_nullable_property()
        {
            var instance = new ObjectWithNullable { Value = new() };
            var iterator = new ObjectMemberIterator();

            iterator.Traverse(instance, (in MemberAccessor accessor) =>
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

            var objectAsDynamic = (dynamic)instance.Value;

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
            }, (in MemberAccessor accessor) =>
            {
                return accessor.MemberType != MemberType.Property;
            });

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
