using ObjectTreeWalker;

namespace ObjectTreeWalker.Tests
{
    public class TraversalCorrectnessTests
    {
        public class Node
        {
            public string Value { get; set; } = string.Empty;

            public Node? Next { get; set; }
        }

        public class Item
        {
            public int X { get; set; }
        }

        public class ItemHolder
        {
            public List<Item>? Items { get; set; }
        }

        public class StringListHolder
        {
            public List<string?>? Items { get; set; }
        }

        public class MapHolder
        {
            public Dictionary<string, int>? Map { get; set; }
        }

        public class WithIndexer
        {
            public string Name { get; set; } = string.Empty;

            public string this[int index]
            {
                get => "indexed";
                set { }
            }
        }

        public class Pair
        {
            public Node? A { get; set; }

            public Node? B { get; set; }
        }

        public struct CountingContext
        {
            public int Count { get; set; }
        }

        [Fact]
        public void Circular_reference_terminates()
        {
            var root = new Node { Value = "x" };
            root.Next = root;

            var visited = new List<string>();
            new ObjectMemberIterator().Traverse(root, (in MemberAccessor accessor) => visited.Add(accessor.Name));

            Assert.Equal(new[] { "Value", "Next" }, visited);
        }

        [Fact]
        public void Two_node_cycle_terminates()
        {
            var a = new Node { Value = "a" };
            var b = new Node { Value = "b" };
            a.Next = b;
            b.Next = a;

            var visited = new List<string>();
            new ObjectMemberIterator().Traverse(a, (in MemberAccessor accessor) => visited.Add(accessor.Name));

            // BFS order: a.Value (leaf), a.Next->b (expanded, no visit),
            // b.Value (leaf), b.Next->a (alias leaf, not re-expanded)
            Assert.Equal(new[] { "Value", "Value", "Next" }, visited);
        }

        [Fact]
        public void Throwing_visitor_does_not_poison_next_traversal()
        {
            var root = new Node { Value = "x" };
            root.Next = root;

            var iterator = new ObjectMemberIterator();
            var visits = 0;
            Assert.Throws<InvalidOperationException>(() =>
                iterator.Traverse(root, (in MemberAccessor accessor) =>
                {
                    visits++;
                    throw new InvalidOperationException("boom");
                }));
            Assert.Equal(1, visits);

            var cleanVisits = new List<string>();
            iterator.Traverse(new Node { Value = "clean" }, (in MemberAccessor accessor) => cleanVisits.Add(accessor.Name));

            Assert.Equal(new[] { "Value", "Next" }, cleanVisits);
        }

        [Fact]
        public void Struct_context_counts_reference_type_collection_items()
        {
            var holder = new ItemHolder { Items = new List<Item> { new() { X = 1 }, new() { X = 2 } } };

            var context = new ObjectMemberIterator().Traverse<CountingContext>(
                holder,
                (ref CountingContext ctx, in MemberAccessor accessor) => ctx.Count++);

            Assert.Equal(2, context.Count);
        }

        [Fact]
        public void Root_list_is_traversed()
        {
            var names = new List<string>();
            new ObjectMemberIterator().Traverse(new List<int> { 1, 2, 3 }, (in MemberAccessor accessor) => names.Add(accessor.Name));

            Assert.Equal(new[] { "[0]", "[1]", "[2]" }, names);
        }

        [Fact]
        public void String_list_items_are_visited()
        {
            var seen = new List<string>();
            new ObjectMemberIterator().Traverse(
                new StringListHolder { Items = new List<string?> { "a", "b" } },
                (in MemberAccessor accessor) => seen.Add(accessor.Name + "=" + accessor.GetValue()));

            Assert.Equal(new[] { "Items[0]=a", "Items[1]=b" }, seen);
        }

        [Fact]
        public void Null_list_items_are_visited()
        {
            var seen = new List<string>();
            new ObjectMemberIterator().Traverse(
                new StringListHolder { Items = new List<string?> { "a", null } },
                (in MemberAccessor accessor) => seen.Add(accessor.Name + "=" + (accessor.GetValue()?.ToString() ?? "NULL")));

            Assert.Equal(new[] { "Items[0]=a", "Items[1]=NULL" }, seen);
        }

        [Fact]
        public void Dictionary_entries_are_visited_as_key_and_value()
        {
            var names = new List<string>();
            var allMarked = true;
            new ObjectMemberIterator().Traverse(
                new MapHolder { Map = new Dictionary<string, int> { ["k"] = 5 } },
                (in MemberAccessor accessor) =>
                {
                    names.Add(accessor.Name);
                    allMarked &= accessor.PropertyPath.Last().IsPartOfDictionary;
                });

            Assert.Equal(new[] { "Map[0].Key", "Map[0].Value" }, names);
            Assert.True(allMarked);
        }

        [Fact]
        public void Indexer_does_not_break_traversal()
        {
            var names = new List<string>();
            new ObjectMemberIterator().Traverse(new WithIndexer { Name = "x" }, (in MemberAccessor accessor) => names.Add(accessor.Name));

            Assert.Equal(new[] { "Name" }, names);
        }

        [Fact]
        public void Shared_reference_is_expanded_once()
        {
            var shared = new Node { Value = "s" };
            var pair = new Pair { A = shared, B = shared };

            var names = new List<string>();
            new ObjectMemberIterator().Traverse(pair, (in MemberAccessor accessor) => names.Add(accessor.Name));

            // BFS order: A expands (no visit), B aliases the shared node (reported as a
            // leaf), then the shared Value leaf and the null Next member are reported
            Assert.Equal(new[] { "B", "Value", "Next" }, names);
        }

        public class KvpHolder
        {
            public KeyValuePair<string, int> Entry { get; set; } = new("k", 1);
        }

        public enum Color
        {
            Red,
            Green,
        }

        public class EnumHolder
        {
            public Color Color { get; set; } = Color.Red;

            public string Name { get; set; } = "x";
        }

        public class MatrixHolder
        {
            public int[,]? M { get; set; }
        }

        public class ListHolder
        {
            public List<int>? Items { get; set; }
        }

        public class ThrowingGetterHolder
        {
            public string Good { get; set; } = "ok";

            public string Bad => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void KeyValuePair_member_reports_Key_and_Value_only()
        {
            var names = new List<string>();
            new ObjectMemberIterator().Traverse(new KvpHolder(), (in MemberAccessor accessor) => names.Add(accessor.Name));

            Assert.Equal(new[] { "Key", "Value" }, names);
        }

        [Fact]
        public void Enum_member_is_a_leaf()
        {
            var names = new List<string>();
            new ObjectMemberIterator().Traverse(new EnumHolder(), (in MemberAccessor accessor) => names.Add(accessor.Name));

            Assert.Equal(new[] { "Color", "Name" }, names);
        }

        [Fact]
        public void Multidimensional_array_items_use_rank_indices()
        {
            var holder = new MatrixHolder { M = new int[,] { { 1, 2 }, { 3, 4 } } };

            var seen = new List<(string Name, object? Value)>();
            new ObjectMemberIterator().Traverse(holder, (in MemberAccessor accessor) => seen.Add((accessor.Name, accessor.GetValue())));

            Assert.Equal(
                new[] { ("M[0,0]", (object?)1), ("M[0,1]", (object?)2), ("M[1,0]", (object?)3), ("M[1,1]", (object?)4) },
                seen);

            // the reported names map back to the same elements through the real rank indices
            foreach (var (name, value) in seen)
            {
                var inner = name.Substring("M[".Length).TrimEnd(']');
                var indices = inner.Split(',').Select(int.Parse).ToArray();
                Assert.Equal(value, holder.M!.GetValue(indices));
            }
        }

        [Fact]
        public void SetValue_on_collection_item_throws()
        {
            var holder = new ListHolder { Items = new List<int> { 1 } };

            var itemAccessors = new List<MemberAccessor>();
            new ObjectMemberIterator().Traverse(
                holder,
                (in MemberAccessor accessor) =>
                {
                    if (accessor.MemberType == MemberType.CollectionItem)
                    {
                        itemAccessors.Add(accessor);
                    }
                });

            Assert.Single(itemAccessors);
            Assert.Throws<InvalidOperationException>(() => itemAccessors[0].SetValue(99));
        }

        [Fact]
        public void Throwing_getter_aborts_by_default()
        {
            var seen = new List<string>();
            Assert.Throws<InvalidOperationException>(() =>
                new ObjectMemberIterator().Traverse(
                    new ThrowingGetterHolder(),
                    (in MemberAccessor accessor) => seen.Add(accessor.Name)));

            Assert.Equal(new[] { "Good" }, seen);
        }

        [Fact]
        public void Throwing_getter_is_skipped_when_opted_in()
        {
            var seen = new List<string>();
            new ObjectMemberIterator(skipThrowingMembers: true).Traverse(
                new ThrowingGetterHolder(),
                (in MemberAccessor accessor) => seen.Add(accessor.Name));

            Assert.Equal(new[] { "Good" }, seen);
        }
    }
}
