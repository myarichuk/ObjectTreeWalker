using System;
using System.Collections.Generic;
using ObjectTreeWalker;
using Xunit;

namespace ObjectTreeWalker.Tests
{
    public class DeepCloneTests
    {
        public enum TestEnum
        {
            Value1,
            Value2
        }

        public class SimpleClass
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        public class ComplexClass
        {
            public SimpleClass? Inner { get; set; }
            public decimal Price { get; set; }
            public DateTime Date { get; set; }
        }

        public struct SimpleStruct
        {
            public int Value { get; set; }
            public string Text { get; set; }
        }

        public class ClassWithArray
        {
            public int[]? Numbers { get; set; }
            public SimpleClass[]? Items { get; set; }
        }

        public class ClassWithCircularRef
        {
            public ClassWithCircularRef? Self { get; set; }
            public List<ClassWithCircularRef>? SelfList { get; set; }
            public string Value { get; set; }
        }

        public class ComplexObjectWithCollections
        {
            public int Id { get; set; }
            public List<int>? Numbers { get; set; }
            public Dictionary<string, SimpleClass>? Items { get; set; }
        }

        [Fact]
        public void DeepClone_Decimal_ReturnsValue()
        {
            decimal val = 10.5m;
            var clone = val.DeepClone();
            Assert.Equal(10.5m, clone);
        }

        [Fact]
        public void DeepClone_DateTime_ReturnsValue()
        {
            DateTime val = new DateTime(2023, 10, 27);
            var clone = val.DeepClone();
            Assert.Equal(val, clone);
        }

        [Fact]
        public void DeepClone_Enum_ReturnsValue()
        {
            TestEnum val = TestEnum.Value2;
            var clone = val.DeepClone();
            Assert.Equal(TestEnum.Value2, clone);
        }

        [Fact]
        public void DeepClone_PrimitiveOptimization_ReturnsSameInstance()
        {
            // For strings, it should return the exact same reference
            string str = "test string";
            object clonedStr = ((object)str).DeepClone();
            Assert.Same(str, clonedStr);

            // For boxed primitives, it should return the exact same reference because of the optimization
            object boxedInt = 42;
            object clonedInt = boxedInt.DeepClone();
            Assert.Same(boxedInt, clonedInt);

            object boxedDecimal = 10.5m;
            object clonedDecimal = boxedDecimal.DeepClone();
            Assert.Same(boxedDecimal, clonedDecimal);

            object boxedDateTime = new DateTime(2023, 1, 1);
            object clonedDateTime = boxedDateTime.DeepClone();
            Assert.Same(boxedDateTime, clonedDateTime);

            object boxedEnum = TestEnum.Value1;
            object clonedEnum = boxedEnum.DeepClone();
            Assert.Same(boxedEnum, clonedEnum);
        }

        [Fact]
        public void DeepClone_Null_ReturnsDefault()
        {
            SimpleClass? obj = null;
            var clone = obj.DeepClone();
            Assert.Null(clone);
        }

        [Fact]
        public void DeepClone_Primitive_ReturnsValue()
        {
            int val = 42;
            var clone = val.DeepClone();
            Assert.Equal(42, clone);
        }

        [Fact]
        public void DeepClone_String_ReturnsValue()
        {
            string val = "hello";
            var clone = val.DeepClone();
            Assert.Equal("hello", clone);
        }

        [Fact]
        public void DeepClone_SimpleClass_ReturnsClone()
        {
            var original = new SimpleClass { Id = 1, Name = "Test" };
            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(original.Id, clone.Id);
            Assert.Equal(original.Name, clone.Name);
        }

        [Fact]
        public void DeepClone_ComplexClass_ReturnsClone()
        {
            var original = new ComplexClass
            {
                Price = 99.99m,
                Date = new DateTime(2023, 1, 1),
                Inner = new SimpleClass { Id = 2, Name = "Inner" }
            };

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(original.Price, clone.Price);
            Assert.Equal(original.Date, clone.Date);

            Assert.NotSame(original.Inner, clone.Inner);
            Assert.Equal(original.Inner.Id, clone.Inner.Id);
            Assert.Equal(original.Inner.Name, clone.Inner.Name);
        }

        [Fact]
        public void DeepClone_Struct_ReturnsClone()
        {
            var original = new SimpleStruct { Value = 10, Text = "Struct" };
            var clone = original.DeepClone();

            Assert.Equal(original.Value, clone.Value);
            Assert.Equal(original.Text, clone.Text);
        }

        [Fact]
        public void DeepClone_Array_ReturnsClone()
        {
            var original = new ClassWithArray
            {
                Numbers = new[] { 1, 2, 3 },
                Items = new[] { new SimpleClass { Id = 1, Name = "A" }, new SimpleClass { Id = 2, Name = "B" } }
            };

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.NotSame(original.Numbers, clone.Numbers);
            Assert.Equal(original.Numbers, clone.Numbers);

            Assert.NotSame(original.Items, clone.Items);
            Assert.Equal(original.Items.Length, clone.Items.Length);

            Assert.NotSame(original.Items[0], clone.Items[0]);
            Assert.Equal(original.Items[0].Id, clone.Items[0].Id);
            Assert.Equal(original.Items[0].Name, clone.Items[0].Name);
        }

        [Fact]
        public void DeepClone_CircularReference_ReturnsCloneWithoutStackOverflow()
        {
            var original = new ClassWithCircularRef { Value = "Test" };
            original.Self = original;

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Same(clone, clone.Self);
            Assert.Equal("Test", clone.Value);
        }

        [Fact]
        public void DeepClone_MultidimensionalArray_ReturnsClone()
        {
            int[,] array = new int[2, 2] { { 1, 2 }, { 3, 4 } };
            var clone = array.DeepClone();

            Assert.NotSame(array, clone);
            Assert.Equal(1, clone[0, 0]);
            Assert.Equal(2, clone[0, 1]);
            Assert.Equal(3, clone[1, 0]);
            Assert.Equal(4, clone[1, 1]);
        }


        [Fact]
        public void DeepClone_ListOfPrimitives_ReturnsClone()
        {
            var original = new List<int> { 1, 2, 3, 4, 5 };
            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(original, clone);
        }

        [Fact]
        public void DeepClone_ListOfObjects_ReturnsClone()
        {
            var original = new List<SimpleClass>
            {
                new SimpleClass { Id = 1, Name = "A" },
                new SimpleClass { Id = 2, Name = "B" },
            };

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(original.Count, clone.Count);
            Assert.NotSame(original[0], clone[0]);
            Assert.Equal(original[0].Id, clone[0].Id);
            Assert.Equal(original[0].Name, clone[0].Name);
        }

        [Fact]
        public void DeepClone_Dictionary_ReturnsClone()
        {
            var original = new Dictionary<string, SimpleClass>
            {
                { "key1", new SimpleClass { Id = 1, Name = "A" } },
                { "key2", new SimpleClass { Id = 2, Name = "B" } },
            };

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(original.Count, clone.Count);
            Assert.NotSame(original["key1"], clone["key1"]);
            Assert.Equal(original["key1"].Id, clone["key1"].Id);
            Assert.Equal(original["key2"].Name, clone["key2"].Name);
        }

        [Fact]
        public void DeepClone_HashSet_ReturnsClone()
        {
            var original = new HashSet<int> { 1, 2, 3 };
            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(original, clone);
        }

        [Fact]
        public void DeepClone_ClassWithListField_ReturnsClone()
        {
            var original = new ComplexObjectWithCollections
            {
                Id = 1,
                Numbers = new List<int> { 1, 2, 3 },
                Items = new Dictionary<string, SimpleClass>
                {
                    { "a", new SimpleClass { Id = 10, Name = "Ten" } },
                },
            };

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.NotSame(original.Numbers, clone.Numbers);
            Assert.Equal(original.Numbers, clone.Numbers);

            Assert.NotSame(original.Items, clone.Items);
            Assert.NotSame(original.Items["a"], clone.Items["a"]);
            Assert.Equal(original.Items["a"].Id, clone.Items["a"].Id);
        }

        [Fact]
        public void DeepClone_CircularReferenceThroughList_ReturnsCloneWithoutStackOverflow()
        {
            var original = new ClassWithCircularRef { Value = "Test" };
            var list = new List<ClassWithCircularRef> { original };
            original.SelfList = list;

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.NotSame(original.SelfList, clone.SelfList);
            Assert.Same(clone, clone.SelfList![0]);
        }
    }
}
