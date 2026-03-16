#pragma warning disable CS1591
using System;
using System.Collections.Generic;
using Xunit;

namespace ObjectTreeWalker.Tests
{
    [ObjectTreeWalker.DeepCloneable]
    [ObjectTreeWalker.ObjectWalkable]
    public partial class Person
    {
        public string? Name { get; set; }
        public Person? BestFriend { get; set; }
    }

    public class TestVisitor : IObjectVisitor
    {
        public List<object> Visited { get; } = new();

        public void Visit(object obj)
        {
            Visited.Add(obj);
        }
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
        public void DeepClone_PreservesReferences()
        {
            var p1 = new Person { Name = "p1" };
            var p2 = new Person { Name = "p2", BestFriend = p1 };
            p1.BestFriend = p2; // Cycle

            var clone = p1.DeepClone();

            Assert.NotNull(clone);
            Assert.NotSame(p1, clone);
            Assert.Equal(p1.Name, clone.Name);

            Assert.NotNull(clone.BestFriend);
            Assert.NotSame(p2, clone.BestFriend);
            Assert.Equal(p2.Name, clone.BestFriend.Name);

            Assert.Same(clone, clone.BestFriend.BestFriend); // Cycle preserved
        }

        [Fact]
        public void GeneratorEmittedMethod()
        {
            var methodInfo = typeof(Person).GetMethod("DeepClone");
            Assert.NotNull(methodInfo);
            var walkMethodInfo = typeof(Person).GetMethod("Walk");
            Assert.NotNull(walkMethodInfo);
        }

        [Fact]
        public void Walk_BfsTraversal()
        {
            var p1 = new Person { Name = "p1" };
            var p2 = new Person { Name = "p2", BestFriend = p1 };
            p1.BestFriend = p2;

            var visitor = new TestVisitor();
            p1.Walk(visitor);

            Assert.Equal(2, visitor.Visited.Count);
            Assert.Same(p1, visitor.Visited[0]);
            Assert.Same(p2, visitor.Visited[1]);
        }
    }
}
