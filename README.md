# ObjectGraphWalker
ObjectGraphWalker is a simple utility library that allows traversing over C# object properties, Node.js style. Under the hood, the class generates accessors that would speed up the traversal.

## How to use
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
