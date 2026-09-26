using ObjectTreeWalker;

namespace ObjectTreeWalker.Tests
{
    public class DeepCloneCorrectnessTests
    {
        public class WithGetOnlyCollection
        {
            public List<string> Names { get; } = new();
        }

        public struct StructWithReadonlyList
        {
            public readonly List<int> Numbers;

            public StructWithReadonlyList(List<int> numbers)
            {
                Numbers = numbers;
            }
        }

        public class IntListWithTag : List<int>
        {
            public string Tag { get; set; } = string.Empty;
        }

        public class Box
        {
            public string Content { get; set; } = string.Empty;
        }

        [Fact]
        public void Get_only_collection_property_is_cloned_independently()
        {
            var original = new WithGetOnlyCollection();
            original.Names.Add("a");

            var clone = original.DeepClone();
            clone.Names.Add("b");

            Assert.NotSame(original, clone);
            Assert.NotSame(original.Names, clone.Names);
            Assert.Equal(new[] { "a" }, original.Names);
            Assert.Equal(new[] { "a", "b" }, clone.Names);
        }

        [Fact]
        public void Struct_with_readonly_field_is_cloned_independently()
        {
            var original = new StructWithReadonlyList(new List<int> { 1, 2 });

            var clone = original.DeepClone();
            clone.Numbers.Add(3);

            Assert.Equal(new[] { 1, 2 }, original.Numbers);
            Assert.Equal(new[] { 1, 2, 3 }, clone.Numbers);
        }

        [Fact]
        public void Dictionary_comparer_is_preserved()
        {
            var original = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };

            var clone = original.DeepClone();

            Assert.True(clone.ContainsKey("a"));
            Assert.Equal(1, clone["a"]);
        }

        [Fact]
        public void HashSet_comparer_is_preserved()
        {
            var original = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };

            var clone = original.DeepClone();

            Assert.True(clone.Contains("a"));
        }

        [Fact]
        public void Collection_subclass_extra_state_is_preserved()
        {
            var original = new IntListWithTag { Tag = "T" };
            original.Add(1);
            original.Add(2);

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal("T", clone.Tag);
            Assert.Equal(original, clone);
        }

        public class MidList : List<int>
        {
            public string Tag { get; set; } = string.Empty;
        }

        public class LeafList : MidList
        {
        }

        public class KvpBox
        {
#pragma warning disable CS0649
            public KeyValuePair<string, List<int>> Pair;
#pragma warning restore CS0649
        }

        [Fact]
        public void Collection_subclass_inherited_extra_state_is_preserved()
        {
            var original = new LeafList { Tag = "T" };
            original.Add(1);

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal("T", clone.Tag);
            Assert.Equal(original, clone);
        }

        [Fact]
        public void KeyValuePair_field_is_deep_cloned()
        {
            var original = new KvpBox { Pair = new KeyValuePair<string, List<int>>("k", new List<int> { 1 }) };

            var clone = original.DeepClone();
            clone.Pair.Value.Add(2);

            Assert.Equal(new[] { 1 }, original.Pair.Value);
            Assert.Equal(new[] { 1, 2 }, clone.Pair.Value);
        }

        private static IEnumerable<int> GenerateNumbers()
        {
            yield return 1;
            yield return 2;
        }

        [Fact]
        public void Iterator_enumerable_clone_throws_NotSupportedException()
        {
            IEnumerable<int> query = GenerateNumbers();

            Assert.Throws<NotSupportedException>(() => query.DeepClone());
        }

        [Fact]
        public void ConcurrentDictionary_entries_are_cloned()
        {
            var original = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            original["a"] = 1;

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.Equal(1, clone["a"]);
            clone["b"] = 2;
            Assert.False(original.ContainsKey("b"));
        }

        [Fact]
        public void SortedSet_comparer_is_preserved()
        {
            var original = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };

            var clone = original.DeepClone();

            Assert.True(clone.Contains("a"));
        }

        [Fact]
        public void SortedDictionary_comparer_is_preserved()
        {
            var original = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };

            var clone = original.DeepClone();

            Assert.True(clone.ContainsKey("a"));
            Assert.Equal(1, clone["a"]);
        }

        [Fact]
        public void ClearCache_keeps_traversal_and_clone_working()
        {
            var original = new IntListWithTag { Tag = "T" };
            original.Add(1);

            ObjectEnumerator.ClearCache();
            ObjectAccessor.ClearCache();
            ObjectExtensions.ClearCache();

            var clone = original.DeepClone();
            Assert.Equal("T", clone.Tag);
            Assert.Equal(original, clone);

            var names = new List<string>();
            new ObjectMemberIterator().Traverse(clone, (in MemberAccessor accessor) => names.Add(accessor.Name));

            // collection members are expanded, never visited: only the item shows up
            Assert.Equal(new[] { "[0]" }, names);
        }

        [Fact]
        public void Rank1_array_with_nonzero_lower_bound_and_reference_elements_is_cloned()
        {
            var original = Array.CreateInstance(typeof(Box), new[] { 2 }, new[] { 3 });
            original.SetValue(new Box { Content = "p" }, 3);
            original.SetValue(new Box { Content = "q" }, 4);

            var clone = (Array)(object)((Array)original).DeepClone();

            Assert.Equal(3, clone.GetLowerBound(0));
            Assert.Equal("p", ((Box)clone.GetValue(3)!).Content);
            Assert.Equal("q", ((Box)clone.GetValue(4)!).Content);
            Assert.NotSame(original.GetValue(3), clone.GetValue(3));
        }
    }
}
