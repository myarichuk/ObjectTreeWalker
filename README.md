# ObjectTreeWalker2 — Zero-alloc source-generated deep cloner

This is a v2 rewrite with Roslyn source generators for deep cloning objects with zero allocations.

## Quick Example

```csharp
[DeepCloneable]
public partial class Person
{
    public string? Name { get; set; }
}

// ...

var person = new Person { Name = "test" };
var clone = person.DeepClone();
```
