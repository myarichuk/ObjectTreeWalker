namespace ObjectTreeWalker.Tests
{
    /// <summary>
    /// Targets the typed clone engine: exact-type fast paths must never be taken for a value
    /// whose runtime type differs, and must preserve identity/cycles like the dynamic path.
    /// </summary>
    public class CloneEngineTests
    {
        public class Animal { public string Name = "a"; public List<int> Tags = new() { 1 }; }

        public class Dog : Animal { public List<int> Bones = new() { 7 }; }

        public class Zoo
        {
            public Animal Star = new Dog();
            public object[] Covariant = new string[] { "x", "y" };
            public object Boxed = new Dog();
            public IList<int> Interface = new List<int> { 3 };
        }

        public class Node
        {
            public List<Node> Children = new();
            public Node? Parent;
            public Dictionary<string, Node> ByName = new();
            public HashSet<Node> Set = new();
            public Node[] Array = System.Array.Empty<Node>();
        }

        public struct ReadonlyRefStruct
        {
            public readonly List<int> Items;

            public ReadonlyRefStruct(List<int> items) => Items = items;
        }

        public class ReadonlyHolder
        {
            public readonly List<int> Items = new() { 1 };
            public ReadonlyRefStruct Struct = new(new List<int> { 2 });
            public ReadonlyRefStruct? Nullable = new ReadonlyRefStruct(new List<int> { 3 });
            public ReadonlyRefStruct? NullNullable;
        }

        public class Leaf { public int Id; public string Name = "leaf"; }

        [Fact]
        public void Runtime_type_wins_over_static_type()
        {
            var zoo = new Zoo();
            var clone = zoo.DeepClone();

            var star = Assert.IsType<Dog>(clone.Star);
            Assert.NotSame(zoo.Star, star);
            Assert.NotSame(((Dog)zoo.Star).Bones, star.Bones);
            Assert.Equal(new[] { 7 }, star.Bones);

            Assert.IsType<string[]>(clone.Covariant);
            Assert.NotSame(zoo.Covariant, clone.Covariant);
            Assert.Equal(zoo.Covariant, clone.Covariant);

            Assert.IsType<Dog>(clone.Boxed);
            Assert.NotSame(zoo.Boxed, clone.Boxed);

            Assert.IsType<List<int>>(clone.Interface);
            Assert.NotSame(zoo.Interface, clone.Interface);
        }

        [Fact]
        public void Root_of_a_derived_type_through_a_base_typed_variable_is_fully_cloned()
        {
            Animal source = new Dog();
            var clone = source.DeepClone();
            var dog = Assert.IsType<Dog>(clone);
            Assert.NotSame(((Dog)source).Bones, dog.Bones);
        }

        [Fact]
        public void Cycles_and_aliases_through_typed_collections_are_preserved()
        {
            var root = new Node();
            var child = new Node { Parent = root };
            root.Children.Add(child);
            root.Children.Add(child);       // alias inside the same list
            root.ByName["self"] = root;     // cycle through a dictionary
            root.ByName["child"] = child;
            root.Set.Add(child);
            root.Array = new[] { root, child };
            child.Children = root.Children; // a shared list

            var clone = root.DeepClone();

            Assert.NotSame(root, clone);
            var clonedChild = clone.Children[0];
            Assert.NotSame(child, clonedChild);
            Assert.Same(clonedChild, clone.Children[1]);
            Assert.Same(clone, clonedChild.Parent);
            Assert.Same(clone, clone.ByName["self"]);
            Assert.Same(clonedChild, clone.ByName["child"]);
            Assert.Same(clonedChild, Assert.Single(clone.Set));
            Assert.Same(clone, clone.Array[0]);
            Assert.Same(clonedChild, clone.Array[1]);
            Assert.Same(clone.Children, clonedChild.Children);
        }

        [Fact]
        public void Readonly_fields_and_nullable_structs_with_references_are_cloned()
        {
            var source = new ReadonlyHolder();
            var clone = source.DeepClone();

            Assert.NotSame(source.Items, clone.Items);
            Assert.Equal(source.Items, clone.Items);
            Assert.NotSame(source.Struct.Items, clone.Struct.Items);
            Assert.Equal(source.Struct.Items, clone.Struct.Items);
            Assert.NotSame(source.Nullable!.Value.Items, clone.Nullable!.Value.Items);
            Assert.Equal(source.Nullable.Value.Items, clone.Nullable.Value.Items);
            Assert.Null(clone.NullNullable);
        }

        [Fact]
        public void Typed_collection_copies_keep_the_comparer()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
            var refMap = new Dictionary<string, Leaf>(StringComparer.OrdinalIgnoreCase) { ["A"] = new() };

            var mapClone = map.DeepClone();
            var setClone = set.DeepClone();
            var refMapClone = refMap.DeepClone();

            Assert.Same(StringComparer.OrdinalIgnoreCase, mapClone.Comparer);
            Assert.Equal(1, mapClone["a"]);
            Assert.Contains("a", setClone);
            Assert.NotSame(refMap["A"], refMapClone["a"]);
        }

        [Fact]
        public void Reference_free_collections_are_independent_copies()
        {
            var list = new List<int> { 1, 2, 3 };
            var array = new[] { 1, 2, 3 };
            var map = new Dictionary<int, string> { [1] = "a" };

            var listClone = list.DeepClone();
            var arrayClone = array.DeepClone();
            var mapClone = map.DeepClone();
            listClone.Add(4);
            arrayClone[0] = 42;
            mapClone[2] = "b";

            Assert.Equal(3, list.Count);
            Assert.Equal(1, array[0]);
            Assert.Single(map);
        }

        [Fact]
        public void Root_shallow_shortcut_still_returns_a_distinct_copy()
        {
            var source = new Leaf { Id = 5 };
            var clone = source.DeepClone();
            Assert.NotSame(source, clone);
            Assert.Equal(5, clone.Id);
            Assert.Same(source.Name, clone.Name); // strings are immutable: shared
        }

        [Fact]
        public void Clone_is_reentrant_and_thread_safe()
        {
            var roots = Enumerable.Range(0, 64).Select(i => new Node { Children = { new Node() } }).ToArray();
            var clones = new Node[roots.Length];
            Parallel.For(0, roots.Length, i => clones[i] = roots[i].DeepClone());

            for (var i = 0; i < roots.Length; i++)
            {
                Assert.NotSame(roots[i], clones[i]);
                Assert.NotSame(roots[i].Children[0], clones[i].Children[0]);
            }
        }
    
        // the fast path guards itself and silently falls back if the BCL layout differs: this test is what
        // notices it going away. It is expected to fail only against the netstandard2.1 build (no fast path).
        [Fact]
        public void Dictionary_value_patcher_is_active_on_this_runtime()
        {
            Assert.NotNull(DictionaryValuePatcher<string, Leaf>.Patch);
            Assert.NotNull(DictionaryValuePatcher<int, List<int>>.Patch);
        }

        [Fact]
        public void Dictionary_with_removed_entries_clones_every_live_value()
        {
            var source = new Dictionary<string, Leaf>();
            for (var i = 0; i < 50; i++)
            {
                source["k" + i] = new Leaf { Id = i };
            }

            // leave holes (a free list) in the source entry array, then reuse some of them
            for (var i = 0; i < 50; i += 3)
            {
                source.Remove("k" + i);
            }

            source["late"] = new Leaf { Id = 1000 };

            var clone = source.DeepClone();

            Assert.Equal(source.Count, clone.Count);
            foreach (var pair in source)
            {
                Assert.True(clone.TryGetValue(pair.Key, out var cloned));
                Assert.NotSame(pair.Value, cloned);
                Assert.Equal(pair.Value.Id, cloned!.Id);
            }

            clone["new"] = new Leaf();
            clone.Remove("late");
            Assert.False(source.ContainsKey("new"));
            Assert.True(source.ContainsKey("late"));
        }
    
        public class Wide
        {
            public int F0 = 0;
            public int F1 = 1;
            public int F2 = 2;
            public int F3 = 3;
            public int F4 = 4;
            public int F5 = 5;
            public int F6 = 6;
            public int F7 = 7;
            public int F8 = 8;
            public int F9 = 9;
            public int F10 = 10;
            public int F11 = 11;
            public int F12 = 12;
            public int F13 = 13;
            public int F14 = 14;
            public int F15 = 15;
            public int F16 = 16;
            public int F17 = 17;
            public int F18 = 18;
            public int F19 = 19;
            public int F20 = 20;
            public int F21 = 21;
            public int F22 = 22;
            public int F23 = 23;
            public int F24 = 24;
            public int F25 = 25;
            public int F26 = 26;
            public int F27 = 27;
            public int F28 = 28;
            public int F29 = 29;
            public int F30 = 30;
            public int F31 = 31;
            public int F32 = 32;
            public int F33 = 33;
            public int F34 = 34;
            public int F35 = 35;
            public int F36 = 36;
            public int F37 = 37;
            public int F38 = 38;
            public int F39 = 39;
            public List<int> Items = new() { 1 };
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        public class Union
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public long Whole = 0x0102030405060708;

            [System.Runtime.InteropServices.FieldOffset(0)]
            public int Low;

            [System.Runtime.InteropServices.FieldOffset(8)]
            public string Name = "u";
        }

        public class FinalizerCounter
        {
            public static int Finalized;

            public List<int> Items = new() { 1 };

            ~FinalizerCounter() => Interlocked.Increment(ref Finalized);
        }

        [Fact]
        public void Wide_types_use_the_bulk_copy_and_are_complete()
        {
            var source = new Wide();
            var clone = source.DeepClone();
            Assert.Equal(39, clone.F39);
            Assert.Equal(0, clone.F0);
            Assert.NotSame(source.Items, clone.Items);
        }

        [Fact]
        public void Explicit_layout_types_keep_every_byte()
        {
            var source = new Union();
            var clone = source.DeepClone();
            Assert.Equal(source.Whole, clone.Whole);
            Assert.Equal(source.Low, clone.Low);
            Assert.Equal("u", clone.Name);
        }

        [Fact]
        public void Fieldwise_copies_of_finalizable_types_are_still_valid_objects()
        {
            var source = new FinalizerCounter();
            var clone = source.DeepClone();
            Assert.NotSame(source.Items, clone.Items);
            Assert.Equal(source.Items, clone.Items);
        }
    
        // the fast path guards itself and silently falls back if the BCL layout differs: this test is what
        // notices it going away. It is expected to fail only against the netstandard2.1 build (no fast path).
        [Fact]
        public void Raw_hash_collection_copier_is_active_on_this_runtime()
        {
            Assert.NotNull(RawHashCollection<Dictionary<string, Leaf>>.Copy);
            Assert.NotNull(RawHashCollection<Dictionary<int, int>>.Copy);
            Assert.NotNull(RawHashCollection<HashSet<string>>.Copy);
        }

        [Fact]
        public void Raw_dictionary_copy_does_not_share_cached_views_with_the_source()
        {
            var source = new Dictionary<string, Leaf> { ["a"] = new() };
            _ = source.Keys.Count;   // materialize the cached views on the source
            _ = source.Values.Count;

            var clone = source.DeepClone();
            clone["b"] = new Leaf();

            Assert.Equal(new[] { "a", "b" }, clone.Keys.OrderBy(k => k));
            Assert.Equal(2, clone.Values.Count);
            Assert.Equal(new[] { "a" }, source.Keys);
        }

        [Fact]
        public void Raw_copies_keep_a_working_free_list()
        {
            var map = new Dictionary<int, int>();
            var set = new HashSet<string>();
            for (var i = 0; i < 100; i++)
            {
                map[i] = i;
                set.Add("s" + i);
            }

            for (var i = 0; i < 100; i += 2)
            {
                map.Remove(i);
                set.Remove("s" + i);
            }

            var mapClone = map.DeepClone();
            var setClone = set.DeepClone();

            // reuse the freed entries in the clones only
            for (var i = 0; i < 100; i += 2)
            {
                mapClone[i] = -i;
                setClone.Add("n" + i);
            }

            Assert.Equal(50, map.Count);
            Assert.Equal(50, set.Count);
            Assert.Equal(100, mapClone.Count);
            Assert.Equal(100, setClone.Count);
            for (var i = 0; i < 100; i++)
            {
                Assert.Equal(i % 2 == 0 ? -i : i, mapClone[i]);
                Assert.Equal(i % 2 != 0, map.ContainsKey(i));
                Assert.Equal(i % 2 != 0, setClone.Contains("s" + i));
            }
        }
    }
}
