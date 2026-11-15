using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ObjectTreeWalker.Tests
{
    /// <summary>
    /// Regressions for defects found in review; each test failed (crash, hang, silent data loss or a
    /// shallow "deep" clone) on the code before the fix.
    /// </summary>
    public class RegressionTests
    {
        public enum Color { Red, Green }

        public class Item { public int V { get; set; } }

        public struct Leaf { public int X; }

        public struct Middle { public Leaf B; }

        public class ThreeLevelStructHolder { public Middle A; }

        public class EnumListHolder { public List<Color> Colors { get; set; } = new() { Color.Red, Color.Green }; }

        public class SharedItemHolder
        {
            public SharedItemHolder() => Items.Add(Shared);

            public Item Shared { get; set; } = new();

            public List<Item> Items { get; set; } = new();
        }

        public class ItemListHolder { public List<Item> Items { get; set; } = new() { new() { V = 1 } }; }

        public class SelfContainingList
        {
            public SelfContainingList() => List.Add(List);

            public List<object> List { get; } = new();
        }

        public class SelfContainingDictionary
        {
            public SelfContainingDictionary() => Map["self"] = Map;

            public Dictionary<string, object> Map { get; } = new();
        }

        public class Base
        {
            private List<Item> _baseItems = new() { new() { V = 1 } };

            public List<Item> BaseItemsView() => _baseItems;
        }

        public class Derived : Base { public int Own { get; set; } }

        public class HiddenBase { public int Value { get; set; } = 1; }

        public class HiddenDerived : HiddenBase { public new string Value { get; set; } = "x"; }

        public class GetOnly { public int Value { get; } = 3; }

        public class StructCollections
        {
            public List<Leaf> List { get; set; } = new() { new Leaf() };

            public Leaf[] Array { get; set; } = { new Leaf() };

            public Dictionary<string, Leaf> Map { get; set; } = new() { ["a"] = new Leaf() };

            public Leaf[,] Grid { get; set; } = new Leaf[1, 1];
        }

        public class StructSet { public HashSet<Leaf> Set { get; set; } = new() { new Leaf() }; }

        public class GetOnlyStructProperty
        {
            private Middle _middle;

            public Middle Middle => _middle;
        }

        public class TypeHolder { public Type T = typeof(int); }

        public class KvpHolder { public KeyValuePair<string, List<int>> Pair = new("a", new List<int> { 1 }); }

        public class NonZeroLowerBoundHolder
        {
            public NonZeroLowerBoundHolder()
            {
                Values = Array.CreateInstance(typeof(int), new[] { 2 }, new[] { 5 });
                Values.SetValue(10, 5);
                Values.SetValue(20, 6);
            }

            public Array Values { get; }
        }

        [Fact]
        public void Nested_enum_collection_items_are_reported()
        {
            var values = new List<object?>();
            new ObjectMemberIterator().Traverse(new EnumListHolder(), (in MemberAccessor a) => values.Add(a.GetValue()));
            Assert.Equal(new object[] { Color.Red, Color.Green }, values);
        }

        [Fact]
        public void Shared_instance_inside_a_collection_is_reported_as_alias()
        {
            var names = new List<string>();
            new ObjectMemberIterator().Traverse(new SharedItemHolder(), (in MemberAccessor a) => names.Add(a.Name));
            Assert.Equal(new[] { "Items[0]", "V" }, names.OrderBy(n => n));
        }

        [Fact]
        public void Members_of_collection_items_carry_the_full_property_path()
        {
            var paths = new List<string>();
            new ObjectMemberIterator().Traverse(new ItemListHolder(), (in MemberAccessor a) =>
                paths.Add(string.Join(".", a.PropertyPath.Select(p => p.Name))));
            Assert.Equal(new[] { "Items[0].V" }, paths);
        }

        [Fact(Timeout = 5000)]
        public async Task Collection_containing_itself_terminates()
        {
            var names = await Task.Run(() =>
            {
                var result = new List<string>();
                new ObjectMemberIterator().Traverse(new SelfContainingList(), (in MemberAccessor a) => result.Add(a.Name));
                return result;
            });

            Assert.Equal(new[] { "List[0]" }, names);
        }

        [Fact(Timeout = 5000)]
        public async Task Dictionary_containing_itself_terminates()
        {
            var names = await Task.Run(() =>
            {
                var result = new List<string>();
                new ObjectMemberIterator().Traverse(new SelfContainingDictionary(), (in MemberAccessor a) => result.Add(a.Name));
                return result;
            });

            Assert.Equal(new[] { "Map[0].Key", "Map[0].Value" }, names);
        }

        [Fact]
        public void Private_base_class_fields_are_traversed()
        {
            var names = new List<string>();
            new ObjectMemberIterator().Traverse(new Derived(), (in MemberAccessor a) => names.Add(string.Join(".", a.PropertyPath.Select(p => p.Name))));
            Assert.Contains("_baseItems[0].V", names);
        }

        [Fact]
        public void Members_hidden_with_new_are_traversed_separately()
        {
            var values = new List<object?>();
            new ObjectMemberIterator().Traverse(new HiddenDerived(), (in MemberAccessor a) => values.Add(a.GetValue()));
            Assert.Equal(2, values.Count);
            Assert.Contains("x", values);
            Assert.Contains(1, values);
        }

        [Fact]
        public void Members_hidden_with_new_are_set_separately()
        {
            var obj = new HiddenDerived();
            new ObjectMemberIterator().Traverse(obj, (in MemberAccessor a) => a.SetValue(a.Type == typeof(int) ? 42 : "y"));
            Assert.Equal("y", obj.Value);
            Assert.Equal(42, ((HiddenBase)obj).Value);
        }

        [Fact]
        public void Setting_a_get_only_property_throws()
        {
            Exception? error = null;
            new ObjectMemberIterator().Traverse(new GetOnly(), (in MemberAccessor a) =>
            {
                try
                {
                    a.SetValue(5);
                }
                catch (Exception e)
                {
                    error = e;
                }
            });

            Assert.IsType<InvalidOperationException>(error);
        }

        [Fact]
        public void Setting_a_member_of_a_struct_nested_three_levels_deep_is_persisted()
        {
            var obj = new ThreeLevelStructHolder();
            new ObjectMemberIterator().Traverse(obj, (in MemberAccessor a) => a.SetValue(42));
            Assert.Equal(42, obj.A.B.X);
        }

        [Fact]
        public void Setting_members_of_struct_collection_items_is_written_back()
        {
            var obj = new StructCollections();
            new ObjectMemberIterator().Traverse(obj, (in MemberAccessor a) =>
            {
                // dictionary keys are reported as (read-only) items
                if (a.MemberType != MemberType.CollectionItem)
                {
                    a.SetValue(7);
                }
            });

            Assert.Equal(7, obj.List[0].X);
            Assert.Equal(7, obj.Array[0].X);
            Assert.Equal(7, obj.Map["a"].X);
            Assert.Equal(7, obj.Grid[0, 0].X);
        }

        [Fact]
        public void Setting_members_of_struct_items_in_a_root_list_is_written_back()
        {
            var list = new List<Leaf> { new(), new() };
            new ObjectMemberIterator().Traverse(list, (in MemberAccessor a) => a.SetValue(3));
            Assert.All(list, leaf => Assert.Equal(3, leaf.X));
        }

        [Fact]
        public void Setting_a_member_of_a_struct_item_in_a_set_throws_instead_of_losing_the_change()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ObjectMemberIterator().Traverse(new StructSet(), (in MemberAccessor a) => a.SetValue(1)));
        }

        [Fact]
        public void Setting_a_member_of_a_struct_behind_a_get_only_property_throws_instead_of_losing_the_change()
        {
            var setters = 0;
            new ObjectMemberIterator().Traverse(new GetOnlyStructProperty(), (in MemberAccessor a) =>
            {
                // the backing field is writable; only the copy reached through the get-only property is not
                if (a.PropertyPath[0].Name == nameof(GetOnlyStructProperty.Middle))
                {
                    var accessor = a;
                    Assert.Throws<InvalidOperationException>(() => accessor.SetValue(1));
                    setters++;
                }
            });

            Assert.Equal(1, setters);
        }

        [Fact]
        public void Non_zero_lower_bound_arrays_are_traversed()
        {
            var values = new List<object?>();
            new ObjectMemberIterator().Traverse(new NonZeroLowerBoundHolder(), (in MemberAccessor a) => values.Add(a.GetValue()));
            Assert.Equal(new object[] { 10, 20 }, values);
        }

        [Fact]
        public void DeepClone_clones_private_base_class_fields()
        {
            var source = new Derived();
            var clone = source.DeepClone();
            Assert.NotSame(source.BaseItemsView(), clone.BaseItemsView());
            Assert.NotSame(source.BaseItemsView()[0], clone.BaseItemsView()[0]);
        }

        [Fact]
        public void DeepClone_keeps_reflection_objects_identity()
        {
            Assert.Same(typeof(int), new TypeHolder().DeepClone().T);
        }

        [Fact]
        public void DeepClone_of_KeyValuePair_is_not_affected_by_a_preceding_traversal()
        {
            // the traversal skips KeyValuePair backing fields; it used to share the graph cache entry with DeepClone
            new ObjectMemberIterator(ignoreCompilerGenerated: false).Traverse(new KvpHolder(), (in MemberAccessor _) => { });

            var source = new KvpHolder();
            var clone = source.DeepClone();
            Assert.NotSame(source.Pair.Value, clone.Pair.Value);
            Assert.Equal(source.Pair.Value, clone.Pair.Value);
        }

        [Fact]
        public void DeepClone_of_Stack_is_independent_and_keeps_order()
        {
            var source = new Stack<Item>();
            source.Push(new Item { V = 1 });
            source.Push(new Item { V = 2 });

            var clone = source.DeepClone();
            clone.Push(new Item { V = 3 });

            Assert.Equal(new[] { 2, 1 }, source.Select(i => i.V));
            Assert.Equal(new[] { 3, 2, 1 }, clone.Select(i => i.V));
            Assert.NotSame(source.Peek(), clone.ElementAt(1));
        }

        [Fact]
        public void DeepClone_of_ConcurrentBag_does_not_share_storage()
        {
            var source = new ConcurrentBag<Item> { new() { V = 1 } };
            var clone = source.DeepClone();
            clone.Add(new Item { V = 2 });

            Assert.Single(source);
            Assert.Equal(2, clone.Count);
            Assert.NotSame(source.Single(), clone.Single(i => i.V == 1));
        }

        [Fact]
        public void DeepClone_of_concurrent_queue_and_stack_keeps_order()
        {
            var queue = new ConcurrentQueue<Item>(new[] { new Item { V = 1 }, new Item { V = 2 } });
            var stack = new ConcurrentStack<Item>(new[] { new Item { V = 1 }, new Item { V = 2 } });

            var queueClone = queue.DeepClone();
            var stackClone = stack.DeepClone();

            Assert.Equal(queue.Select(i => i.V), queueClone.Select(i => i.V));
            Assert.Equal(stack.Select(i => i.V), stackClone.Select(i => i.V));
            Assert.NotSame(queue.First(), queueClone.First());
            Assert.NotSame(stack.First(), stackClone.First());
        }

        [Fact]
        public void DeepClone_of_collections_without_a_usable_constructor_falls_back_to_a_field_clone()
        {
            var readOnlyList = new List<Item> { new() { V = 1 } }.AsReadOnly();
            var readOnlyMap = new ReadOnlyDictionary<string, Item>(new Dictionary<string, Item> { ["a"] = new() { V = 2 } });
            var immutable = ImmutableArray.Create(new Item { V = 3 });

            var readOnlyListClone = readOnlyList.DeepClone();
            var readOnlyMapClone = readOnlyMap.DeepClone();
            var immutableClone = immutable.DeepClone();

            Assert.Equal(1, readOnlyListClone[0].V);
            Assert.NotSame(readOnlyList[0], readOnlyListClone[0]);
            Assert.Equal(2, readOnlyMapClone["a"].V);
            Assert.NotSame(readOnlyMap["a"], readOnlyMapClone["a"]);
            Assert.Equal(3, immutableClone[0].V);
            Assert.NotSame(immutable[0], immutableClone[0]);
        }
    }
}
