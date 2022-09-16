using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FastMember;
using ObjectTreeWalker;

public class Foobar
{
	public int NumberProp { get; set; }
}

[MemoryDiagnoser]
public class Program
{
	private Foobar? _objectInstance;
	private PropertyAccessor? _accessor;
	private TypeAccessor? _typeAccessor;

	[IterationSetup]
	public void Init()
	{
		_objectInstance = new Foobar { NumberProp = 1 };
		_accessor = new PropertyAccessor(typeof(Foobar));
		_typeAccessor = TypeAccessor.Create(typeof(Foobar));
	}

	[Benchmark(Baseline = true)]
	public void CSharp()
	{
		for (int i = 0; i < 9000000; i++)
		{
			_objectInstance.NumberProp += 1;
		}
	}

	[Benchmark]
	public void PropertyAccessor()
	{
		for (int i = 0; i < 9000000; i++)
		{
			_accessor.TryGetValue(_objectInstance, nameof(Foobar.NumberProp), out var value);
			_accessor.TrySetValue(_objectInstance, nameof(Foobar.NumberProp), (int)value + 1);
		}
	}

	[Benchmark]
	public void FastMember()
	{
		for (int i = 0; i < 9000000; i++)
		{
			var value = (int)_typeAccessor[_objectInstance, "NumberProp"];
			_typeAccessor[_objectInstance, "NumberProp"] = (value + 1);
		}
	}


	internal static void Main(string[] args) => BenchmarkRunner.Run<Program>();
}
