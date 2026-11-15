# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Fixed
- Traversal of a collection that (directly or through its items) contains itself no longer loops forever with an ever-growing queue: collections are tracked like any other visited instance.
- Collection items of a nested type (e.g. an enum or class declared inside another class) reported as leaves no longer throw `ArgumentException`.
- The shared type-graph cache is now keyed by all enumerator settings; a traversal can no longer make a later `DeepClone` of a `KeyValuePair<K,V>` shallow.
- Private fields declared on base classes are now traversed and deep cloned (they were silently skipped, so clones shared that state with the source).
- `DeepClone` of `Stack<T>`, `Queue<T>`, `ConcurrentBag<T>` and other collections reaching the field-wise path now clones their storage; it used to be a shallow `MemberwiseClone` (a cloned `ConcurrentBag<T>` shared its items with the source).
- `DeepClone` of collections without a usable constructor (`ReadOnlyCollection<T>`, `ReadOnlyDictionary<K,V>`, `ImmutableArray<T>`, ...) clones them field by field instead of throwing.
- `DeepClone` no longer copies reflection objects (`Type`, `MemberInfo`, ...) - a copied `RuntimeType` is a distinct, broken `Type`.
- Members hidden with `new` (with a different type) no longer crash the accessor with a duplicate key, and are read/written independently of the member they hide.
- `MemberAccessor.SetValue` persists changes to members of structs at any nesting depth, and to members of struct items in lists, arrays and dictionary values; it throws when the change cannot be persisted, and on get-only properties, instead of silently doing nothing.
- `PropertyPath` of members inside collection items now includes the path to the item (it used to restart at the item).
- Single-dimension arrays with a non-zero lower bound are traversed correctly.
- Pointer, by-ref and `ref struct` members are skipped instead of making the whole type unsupported.

### Changed
- Member reads/writes during traversal call compiled delegates bound to the member, instead of a name-keyed lookup on every access.
- Dropped the `Microsoft.Extensions.ObjectPool` and `System.Memory` dependencies; package versions are pinned instead of floating (`*`).
- `MemberAccessor.SetValue` accepts `null` (`object?`); null sets a value-type member to its default.

- Traversal no longer double-visits `KeyValuePair<K,V>` members (reports `Key`/`Value` once).
- Multidimensional array items now use rank indices (`M[0,1]`) instead of a flat position.
- Enum members are leaves (no more `value__` expansion), matching `DeepClone`.
- `DeepClone` of compiler-generated iterator state machines now throws `NotSupportedException` instead of producing a field-wise copy.
- `DeepClone` no longer copies framework collection internals (e.g. `ConcurrentDictionary` locks) and preserves extra state declared on intermediate bases (`A : B : List<int>`).
- `DeepClone` keeps the source comparer for collections without a pre-sizing constructor (e.g. `SortedSet<T>`).
- `MemberAccessor.SetValue` on a collection item now throws `InvalidOperationException` instead of silently ignoring the set.

### Added
- `ObjectMemberIterator(skipThrowingMembers: true)` skips members whose predicate or getter throws instead of aborting traversal.
- `ObjectMemberIterator.ClearCache()` and `ObjectExtensions.ClearCache()` for reclaiming type-cache memory in long-lived hosts; oversized pooled traversal queues are dropped instead of retained.

## [v1.0.10.0] - 2022-09-13
### :bug: Bug Fixes
- [`bbfc4ca`](https://github.com/myarichuk/Library.Template/commit/bbfc4ca34650fca71e86bbaa3c177ca892bccf85) - ensure release is created (add missing parameter) *(commit by [@myarichuk](https://github.com/myarichuk))*

[v1.0.10.0]: https://github.com/myarichuk/Library.Template/compare/v1.0.9.0...v1.0.10.0