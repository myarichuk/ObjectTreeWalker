#pragma warning disable CS1591
using System;
using Xunit;

namespace ObjectTreeWalker.Tests
{
    [ObjectTreeWalker.DeepCloneable]
    public partial class Person
    {
        public string? Name { get; set; }
    }

    public class GeneratorTests
    {
        [Fact]
        public void SimplePocoRoundtrip()
        {
            var original = new Person { Name = "test" };
            var clone = original.DeepClone();

            Assert.NotNull(clone);
            Assert.NotSame(original, clone);
            Assert.Equal(original.Name, clone.Name);
        }

        [Fact]
        public void GeneratorEmittedMethod()
        {
            var methodInfo = typeof(Person).GetMethod("DeepClone");
            Assert.NotNull(methodInfo);
        }
    }
}
