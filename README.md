[![Build & Test](https://github.com/myarichuk/ObjectTreeWalker/actions/workflows/on-pull-request.yml/badge.svg)](https://github.com/myarichuk/ObjectTreeWalker/actions/workflows/on-pull-request.yml)

# The What
ObjectGraphWalker is a simple utility library that allows traversing over C# object properties, Node.js style. Under the hood, the class generates accessors that would speed up the traversal.

## The How
Simply instantiate the class and use the ``ObjectMemberIterator::Traverse()`` method.
```cs
var someObject = new SomeObject();
var iterator = new ObjectMemberIterator();
iterator.Traverse(someObject, prop =>
{
	var propertyValue = prop.GetValue();
	prop.SetValue(/* some other value */);
});

```

## Notes
* The iterator will read public and private properties and fields but will ignore any static members of the object. 
* The iterator will work on both value and reference types
* The iterator will ignore backing fields for "auto properties" and it will ignore any compiler generated fields such as closures
* The iterator is still WIP and should be more thoroughly tested
* Additional configuration for iterator behavior is planned. See [Issues](https://github.com/myarichuk/ObjectTreeWalker/issues) tab for more information.
