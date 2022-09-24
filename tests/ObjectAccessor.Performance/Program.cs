using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FastMember;
using ObjectAccessor = ObjectTreeWalker.ObjectAccessor;
// ReSharper disable CheckNamespace
// ReSharper disable ExceptionNotDocumentedOptional
// ReSharper disable ExceptionNotDocumented
#pragma warning disable CS8602
#pragma warning disable CS8604
#pragma warning disable CS8605
#pragma warning disable CS1591


public class Foobar
{
    public int NumberProp { get; set; }
}

[MemoryDiagnoser]
public class Program
{
    private Foobar? _objectInstance;
    private ObjectAccessor? _accessor;
    private TypeAccessor? _typeAccessor;

    [IterationSetup]
    public void Init()
    {
        _objectInstance = new Foobar { NumberProp = 1 };
        _accessor = new ObjectAccessor(typeof(Foobar));
        _typeAccessor = TypeAccessor.Create(typeof(Foobar));
    }

    [Benchmark(Baseline = true)]
    public void CSharpProperty()
    {
        for (int i = 0; i < 9000000; i++)
        {
            _objectInstance.NumberProp += 1;
        }
    }

    [Benchmark]
    public void Reflection()
    {
        var propInfo = typeof(Foobar).GetProperty(nameof(Foobar.NumberProp));
        for (int i = 0; i < 9000000; i++)
        {
            var val = (int)propInfo.GetValue(_objectInstance);
            propInfo.SetValue(_objectInstance, val + 1);
        }
    }

    [Benchmark]
    public void ObjectAccessorProperty()
    {
        for (int i = 0; i < 9000000; i++)
        {
            _accessor.TryGetValue(_objectInstance, nameof(Foobar.NumberProp), out var value);
            _accessor.TrySetValue(_objectInstance, nameof(Foobar.NumberProp), (int)value + 1);
        }
    }

    [Benchmark]
    public void FastMemberProperty()
    {
        for (int i = 0; i < 9000000; i++)
        {
            var value = (int)_typeAccessor[_objectInstance, "NumberProp"];
            _typeAccessor[_objectInstance, "NumberProp"] = (value + 1);
        }
    }


    internal static void Main(string[] args) => BenchmarkRunner.Run<Program>();
}
