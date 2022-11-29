# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [v0.9.4.0] - 2022-11-29
### :bug: Bug Fixes
- [`140dbd7`](https://github.com/myarichuk/ObjectTreeWalker/commit/140dbd794885da0bc7f9cbd4715f3929f7647553) - make sure strings are properly supported *(commit by [@myarichuk](https://github.com/myarichuk))*

### :wrench: Chores
- [`0f2844d`](https://github.com/myarichuk/ObjectTreeWalker/commit/0f2844dc6b69e93da9a3e09fb392aaa478d408e3) - adjust global.json to allow major version upgrades *(commit by [@myarichuk](https://github.com/myarichuk))*
- [`daf177e`](https://github.com/myarichuk/ObjectTreeWalker/commit/daf177e6c798e4c1604bd958428f15a5f169bc08) - merge commit conflict *(commit by [@myarichuk](https://github.com/myarichuk))*


## [v0.3.0.0] - 2022-09-17
### :sparkles: New Features
- [`0f6e7dd`](https://github.com/myarichuk/ObjectTreeWalker/commit/0f6e7dd1f735231cfab83b16a57e64770e238747) - inner implementation - object enumerator to compute and cache property/field graph per type *(commit by [@myarichuk](https://github.com/myarichuk))*
- [`219854d`](https://github.com/myarichuk/ObjectTreeWalker/commit/219854d6524da067f975171381b22fe9d690826f) - implement basic object member iterator functionality, probably will need more testing *(commit by [@myarichuk](https://github.com/myarichuk))*

### :bug: Bug Fixes
- [`48f9c6c`](https://github.com/myarichuk/ObjectTreeWalker/commit/48f9c6c7a91d9ac02b619a37125ddfbe15f5ca66) - ensure returning false when getter/setter is missing from property and trying to get/set value *(commit by [@myarichuk](https://github.com/myarichuk))*
- [`ced48d3`](https://github.com/myarichuk/ObjectTreeWalker/commit/ced48d3263d135598068039a179dfa65ed200096) - add object graph cache to ObjectEnumerator *(commit by [@myarichuk](https://github.com/myarichuk))*
- [`ee10271`](https://github.com/myarichuk/ObjectTreeWalker/commit/ee102713bc5624c1ce0c5d327c36be8599c81a88) - ensure backing properties will not get iterated on and also no trying to "parse" children of primitive fields/properties *(commit by [@myarichuk](https://github.com/myarichuk))*

### :white_check_mark: Tests
- [`5db4961`](https://github.com/myarichuk/ObjectTreeWalker/commit/5db4961700afbacbf51a6c77b50b866059408293) - basic tests for ObjectMemberIterator *(commit by [@myarichuk](https://github.com/myarichuk))*

### :wrench: Chores
- [`b5a918c`](https://github.com/myarichuk/ObjectTreeWalker/commit/b5a918c05681ec587fce8d86f089ee6eb4d856f4) - ensure perf test won't be packaged with "dotnet pack" *(commit by [@myarichuk](https://github.com/myarichuk))*


## [v0.2.1.0] - 2022-09-17
### :white_check_mark: Tests
- [`6b07a38`](https://github.com/myarichuk/ObjectTreeWalker/commit/6b07a38b23228f6517b3547e5450477aa7268d07) - ensure ObjectAccessor tests work on both struct and class objects *(commit by [@myarichuk](https://github.com/myarichuk))*


## [v2.1.0.0] - 2022-09-14
### :sparkles: New Features
- [`0e85a6c`](https://github.com/myarichuk/Library.Template/commit/0e85a6c62eee9c66c38785e062ce8337169c982e) - automatically fill release notes when generating nuspec *(commit by [@myarichuk](https://github.com/myarichuk))*


## [v2.0.1.0] - 2022-09-13
### :wrench: Chores
- [`9262149`](https://github.com/myarichuk/Library.Template/commit/9262149e016f9497ea5a6372e8c79aacd95cf488) - update readme to show how to start working with the template *(commit by [@myarichuk](https://github.com/myarichuk))*


## [v2.0.0.0] - 2022-09-13
### :wrench: Chores
- [`a11ffeb`](https://github.com/myarichuk/Library.Template/commit/a11ffeb94f3f69328dc26b4ab326957c8274eef6) - minor adjustments to PR merge flow *(commit by [@myarichuk](https://github.com/myarichuk))*

### :boom: BREAKING CHANGES
- due to [`fe4696c`](https://github.com/myarichuk/Library.Template/commit/fe4696c697b5f8816131fe7da24af1bf06a0235b) - adjust gitversion config *(commit by [@myarichuk](https://github.com/myarichuk))*:

  adjust gitversion config

- due to [`fc62abd`](https://github.com/myarichuk/Library.Template/commit/fc62abd3cc8cfef87fa337476fd577a30815c32f) - adjust gitversion config *(commit by [@myarichuk](https://github.com/myarichuk))*:

  adjust gitversion config


## [v1.0.23.0] - 2022-09-13
### :bug: Bug Fixes
- [`baf9251`](https://github.com/myarichuk/Library.Template/commit/baf92514fb00d64d4d4f7cfba46e9ebbc5c8be6f) - try to ensure CHANGELOG.md gets updated on release -> attempt 2 *(commit by [@myarichuk](https://github.com/myarichuk))*


## [v1.0.10.0] - 2022-09-13
### :bug: Bug Fixes
- [`bbfc4ca`](https://github.com/myarichuk/Library.Template/commit/bbfc4ca34650fca71e86bbaa3c177ca892bccf85) - ensure release is created (add missing parameter) *(commit by [@myarichuk](https://github.com/myarichuk))*


[v1.0.10.0]: https://github.com/myarichuk/Library.Template/compare/v1.0.9.0...v1.0.10.0
[v1.0.23.0]: https://github.com/myarichuk/Library.Template/compare/v1.0.22.0...v1.0.23.0
[v2.0.0.0]: https://github.com/myarichuk/Library.Template/compare/v1.0.23.0...v2.0.0.0
[v2.0.1.0]: https://github.com/myarichuk/Library.Template/compare/v2.0.0.0...v2.0.1.0
[v2.1.0.0]: https://github.com/myarichuk/Library.Template/compare/v2.0.1.0...v2.1.0.0
[v0.2.1.0]: https://github.com/myarichuk/ObjectTreeWalker/compare/v0.2.0.0...v0.2.1.0
[v0.3.0.0]: https://github.com/myarichuk/ObjectTreeWalker/compare/v0.2.1.0...v0.3.0.0
[v0.9.4.0]: https://github.com/myarichuk/ObjectTreeWalker/compare/v0.9.3.0...v0.9.4.0