# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Fixed
- Traversal no longer double-visits `KeyValuePair<K,V>` members (reports `Key`/`Value` once).
- Multidimensional array items now use rank indices (`M[0,1]`) instead of a flat position.
- Enum members are leaves (no more `value__` expansion), matching `DeepClone`.
- `DeepClone` of compiler-generated iterator state machines now throws `NotSupportedException` instead of producing a field-wise copy.
- `DeepClone` no longer copies framework collection internals (e.g. `ConcurrentDictionary` locks) and preserves extra state declared on intermediate bases (`A : B : List<int>`).
- `DeepClone` keeps the source comparer for collections without a pre-sizing constructor (e.g. `SortedSet<T>`).
- `MemberAccessor.SetValue` on a collection item now throws `InvalidOperationException` instead of silently ignoring the set.

### Added
- `ObjectMemberIterator(skipThrowingMembers: true)` skips members whose predicate or getter throws instead of aborting traversal.
- `ObjectAccessor.ClearCache()` and `ObjectExtensions.ClearCache()` for reclaiming type-cache memory in long-lived hosts; oversized pooled traversal queues are dropped instead of retained.

## [v1.0.10.0] - 2022-09-13
### :bug: Bug Fixes
- [`bbfc4ca`](https://github.com/myarichuk/Library.Template/commit/bbfc4ca34650fca71e86bbaa3c177ca892bccf85) - ensure release is created (add missing parameter) *(commit by [@myarichuk](https://github.com/myarichuk))*

[v1.0.10.0]: https://github.com/myarichuk/Library.Template/compare/v1.0.9.0...v1.0.10.0