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

Note, it is also possible to use ``predicate`` parameter in the ``ObjectMemberIterator::Traverse()`` method to exclude some members from the iteration. The following example iterates over all fields and properties *if* their name is not Foo1
```cs
var someObject = new SomeObject();
var iterator = new ObjectMemberIterator();
iterator.Traverse(someObject, prop =>
{
    var propertyValue = prop.GetValue();
    prop.SetValue(/* some other value */);
}, iterationItem => iterationItem.Name != "Foo1");

```

## Notes
* The iterator will read public and private properties and fields but will ignore any static members of the object.
* The iterator will work on both value and reference types
* The iterator will ignore backing fields for "auto properties" and it will ignore any compiler generated fields such as closures (unless specified in the constructor)
* The iterator is fairly well tested by there may be some bugs. If you do see something weird, I'd appreciate an opened issue with a description :)
