using AnyClone;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Force.DeepCloner;
using ObjectTreeWalker;

namespace DeepCloneBenchmark
{
#pragma warning disable CS1591
    public class SimpleObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ComplexObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public SimpleObject Simple { get; set; } = new SimpleObject();
        public List<int> Numbers { get; set; } = new List<int>();
        public Dictionary<string, SimpleObject> Dict { get; set; } = new Dictionary<string, SimpleObject>();
    }

    [MemoryDiagnoser]
    public class DeepCloneBenchmarks
    {
        private SimpleObject _simpleObject = new SimpleObject();
        private ComplexObject _complexObject = new ComplexObject();

        [GlobalSetup]
        public void Setup()
        {
            _simpleObject = new SimpleObject { Id = 1, Name = "Simple" };

            _complexObject = new ComplexObject
            {
                Id = 1,
                Name = "Complex",
                Simple = new SimpleObject { Id = 2, Name = "InnerSimple" },
                Numbers = new List<int> { 1, 2, 3, 4, 5 },
                Dict = new Dictionary<string, SimpleObject>
                {
                    { "key1", new SimpleObject { Id = 3, Name = "DictSimple1" } },
                    { "key2", new SimpleObject { Id = 4, Name = "DictSimple2" } }
                }
            };
        }

        [Benchmark]
        public SimpleObject ObjectTreeWalker_SimpleObject()
        {
            return ObjectTreeWalker.ObjectExtensions.DeepClone(_simpleObject);
        }

        [Benchmark]
        public SimpleObject AnyClone_SimpleObject()
        {
            return _simpleObject.Clone();
        }

        [Benchmark]
        public SimpleObject DeepCloner_SimpleObject()
        {
            return Force.DeepCloner.DeepClonerExtensions.DeepClone(_simpleObject);
        }

        [Benchmark]
        public ComplexObject ObjectTreeWalker_ComplexObject()
        {
            return ObjectTreeWalker.ObjectExtensions.DeepClone(_complexObject);
        }

        [Benchmark]
        public ComplexObject AnyClone_ComplexObject()
        {
            return _complexObject.Clone();
        }

        [Benchmark]
        public ComplexObject DeepCloner_ComplexObject()
        {
            return Force.DeepCloner.DeepClonerExtensions.DeepClone(_complexObject);
        }
    }

#pragma warning disable CS1591
    public class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<DeepCloneBenchmarks>();
        }
    }
}
