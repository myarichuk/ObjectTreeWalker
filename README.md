[![Build & Test](https://github.com/myarichuk/ObjectTreeWalker/actions/workflows/on-pull-request.yml/badge.svg)](https://github.com/myarichuk/ObjectTreeWalker/actions/workflows/on-pull-request.yml)

# ObjectGraphWalker
ObjectGraphWalker is a powerful utility library that enables seamless traversal over C# object properties and fields, Node.js style. Designed with performance and flexibility in mind, it leverages dynamic code generation and caching to provide efficient traversal capabilities.

## Features
- **Fast Traversal**: Utilizes dynamic code generation to create accessors that speed up traversal.
- **Flexible Filtering**: Allows custom predicates to include or exclude specific members during traversal.
- **Support for Various Types**: Works with both value and reference types, including generics and embedded structs.
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
    prop.SetValue(/* some other value */);
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
    prop.SetValue(/* some other value */);

    //filtering for selective iteration is a simple lambda
}, (in MemberAccessor accessor) => accessor.Name != "Foo1" && accessor.MemberType != MemberType.Property);
```

## Notes
- The iterator will read public and private properties and fields but will ignore any static members of the object.
- The iterator will ignore backing fields for "auto properties" and any compiler-generated fields such as closures (unless specified in the constructor).
- While the iterator is well-tested, there may be some bugs. If you encounter any issues, please open an issue with a description.

## Contributing
Any contributions are welcome :)

## License
[MIT License](LICENSE)
