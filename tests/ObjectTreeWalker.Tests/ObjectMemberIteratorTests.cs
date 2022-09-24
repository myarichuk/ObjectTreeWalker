using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        [Fact]
        public void Can_iterate_flat_class()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            iterator.Traverse(new FooBar(), ii => propertyValues.Add((int)ii.GetValue()));

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

                iterator.Traverse(new FooBar(), ii => propertyValues.Add((int)ii.GetValue()));
                Assert.Equal(6, propertyValues.Count);
            }
        }

        [Fact]
        public void Can_iterate_complex_class()
        {
            var iterator = new ObjectMemberIterator();
            var propertyValues = new List<int>();

            iterator.Traverse(new ComplexFooBar(), ii =>
            {
                var value = ii.GetValue();

                // all of "primitive" properties are of type int so this is correct
                propertyValues.Add((int)value);
            });

            Assert.Collection(propertyValues,
                item => Assert.Equal(111, item),
                item => Assert.Equal(456, item),
                item => Assert.Equal(111, item),
                item => Assert.Equal(222, item),
                item => Assert.Equal(333, item));
        }
    }
}
