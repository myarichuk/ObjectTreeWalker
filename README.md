[![Build & Test](https://github.com/myarichuk/ObjectTreeWalker/actions/workflows/on-pull-request.yml/badge.svg)](https://github.com/myarichuk/ObjectTreeWalker/actions/workflows/on-pull-request.yml)

# ObjectGraphWalker
ObjectGraphWalker is a powerful utility library that enables seamless traversal over C# object properties and fields, Node.js style. Designed with performance and flexibility in mind, it leverages dynamic code generation and caching to provide efficient traversal capabilities.

## Features
- **Fast Traversal**: Utilizes dynamic code generation to create accessors that speed up traversal.
- **Flexible Filtering**: Allows custom predicates to include or exclude specific members during traversal.
- **Support for Various Types**: Works with both value and reference types, including generics and embedded structs.
- **Deep Cloning**: Includes a high-performance deep clone extension method for complex object graphs.
- **Well-Tested**: Includes comprehensive tests to ensure reliability and correctness.

## Installation
Simply install the [NuGet Package](https://www.nuget.org/packages/ObjectTreeWalker/)

## Usage Examples

### Simple Example
A basic example that demonstrates how to traverse an object and access its properties:

```cs
var someObject = new SomeObject();
var iterator = new ObjectMemberIterator();
iterator.Traverse(someObject, (in MemberAccessor accessor) =>
{
    var propertyValue = accessor.GetValue();
    accessor.SetValue(/* some other value */);
});
```

### Advanced Example
An advanced example that shows how to use predicates to filter members and control the traversal behavior:

```cs
var someObject = new SomeObject();
var iterator = new ObjectMemberIterator();
iterator.Traverse(someObject, (in MemberAccessor accessor) =>
{
    var propertyValue = accessor.GetValue();
    accessor.SetValue(/* some other value */);

    //filtering for selective iteration is a simple lambda
}, (in MemberAccessor accessor) => accessor.Name != "Foo1" && accessor.MemberType != MemberType.Property);
```

### Deep Cloning
You can easily create deep clones of any object using the provided extension method:

```cs
var someObject = new SomeObject();
var clone = someObject.DeepClone();
```

## Performance
`DeepClone()` is benchmarked against [AnyClone](https://github.com/replaysMike/AnyClone) and [DeepCloner](https://github.com/force-net/DeepCloner) using [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) (`dotnet run -c Release --project bench/DeepCloneBenchmark`). *Graph100* is a list of 100 complex objects (each with a nested object, a 16-item `List<int>` and a 2-entry `Dictionary<string, SimpleObject>`), every 10th one shared:

| Scenario | ObjectTreeWalker | DeepCloner | AnyClone |
|---|---:|---:|---:|
| Simple object | **13.9 ns** / 32 B | 99.9 ns / 168 B | 1,318.8 ns / 2,624 B |
| Complex object | **278 ns** / 448 B | 1,084 ns / 1,496 B | 11,277 ns / 17,000 B |
| Graph100 | **26.2 µs** / 44.8 KB | 95.0 µs / 127.0 KB | 1,352.6 µs / 1,943.5 KB |

Traversal (`dotnet run -c Release --project bench/IteratorBenchmark`): a 2-member object in 89 ns / 24 B, the complex object in 1.04 µs / 936 B.

(Intel Xeon @ 2.10GHz, 4 cores, .NET 10.0, 2 launches x 15 iterations - actual numbers will vary by machine.)

### How
- **No reflection on the hot path, no dependencies.** Every per-type cloner and member accessor is hand-written IL (`System.Reflection.Emit`), built once per type.
- **Dispatch resolved at JIT time.** Per-type cloners live in generic static caches (`Cloner<T>`), and the emitted IL calls the cloner of each field's static type directly; a runtime type check routes subclasses and interface-typed fields through dynamic dispatch. Whether a type needs cloning at all is a `static readonly bool` the JIT folds into a constant, so strings and reference-free structs cost nothing.
- **No `MemberwiseClone`, no `Array.Clone`.** On .NET 10 they measured 4-5x slower than allocating uninitialized and copying typed fields (or a span `memmove`) for typical objects; they are used only for wide or explicit-layout types.
- **Typed collection paths.** `T[]`, `List<T>`, `Dictionary<K,V>` and `HashSet<T>` are cloned with typed code (no boxing, no non-generic enumerators). When keys need no cloning, a dictionary's hash structure is copied verbatim and only values are replaced in place - no re-hashing. This relies on the BCL's private layout, so it is enabled only after verifying that layout, and falls back to the public API otherwise.
- **A purpose-built identity map** (open addressing, single probe per object, O(used) reset, pooled per thread) tracks shared references and cycles.

## Notes
- The iterator reads public and private properties and fields, including private members declared on base classes, but ignores static members. A member hidden with `new` is reported separately from the member it hides; an overridden property is reported once.
- The iterator will ignore backing fields for "auto properties" and any compiler-generated fields such as closures (unless specified in the constructor). Pointer, by-ref and `ref struct` members are skipped.
- Traversal contract: collection *members* are expanded, never visited; collection items are reported as `MemberType.CollectionItem` and are read-only (`SetValue` on one throws `InvalidOperationException`); dictionary keys/values are flagged `IsPartOfDictionary`; an already-expanded instance (object *or* collection) reached again is reported once as a leaf and never re-expanded, so cyclic graphs - including a collection containing itself - terminate; struct `TContext` accumulates across collection recursion; a root `string` or other member-less leaf visits zero members.
- `PropertyPath` is the full route to a member, through collection items too (`Items[0]`, `V` for `Items[0].V`).
- `SetValue` persists changes made to members of structs: the modified copy is written back through every enclosing struct and into the enclosing list, array or dictionary value. When that is impossible it throws `InvalidOperationException` instead of silently losing the change (a struct in a `HashSet<T>` or a dictionary key, a struct reached through a get-only property, a get-only property itself).
- `KeyValuePair<K,V>` members report `Key`/`Value` once; multidimensional array items use rank indices (`M[0,1]`); enum members are leaves.
- By default a throwing member getter aborts the whole traversal; pass `skipThrowingMembers: true` to the `ObjectMemberIterator` constructor to skip such members instead.
- `DeepClone()` supports arrays, dictionaries, `IList`/`ICollection<T>` collections, concurrent collections and plain objects, including private fields of base classes. Collections without a usable constructor (`ReadOnlyCollection<T>`, `ImmutableArray<T>`, ...) are cloned field by field. Reflection objects (`Type`, `MemberInfo`, ...) are shared, never copied. Compiler-generated iterator state machines (e.g. `yield` generators) throw `NotSupportedException` — materialize them into a `List<T>` or array first. Collection comparers are preserved when the type offers a matching constructor.
- Long-lived hosts that see many dynamic or generic types can reclaim type-cache memory via `ObjectMemberIterator.ClearCache()` and `ObjectExtensions.ClearCache()`. The compiled per-type cloners themselves live in generic static caches for the process lifetime (like `EqualityComparer<T>.Default`).
- While the iterator is well-tested, there may be some bugs. If you encounter any issues, please open an issue with a description.

## Contributing
Any contributions are welcome :)

## License
[MIT License](LICENSE)
