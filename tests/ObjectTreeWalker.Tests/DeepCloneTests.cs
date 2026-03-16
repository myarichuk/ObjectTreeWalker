using System;
using System.Collections.Generic;
using ObjectTreeWalker;
using Xunit;

namespace ObjectTreeWalker.Tests
{
    public class DeepCloneTests
    {
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
            public string Value { get; set; }
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
    }
}
