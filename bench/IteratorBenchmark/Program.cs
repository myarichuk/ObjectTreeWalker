using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ObjectTreeWalker;

namespace IteratorBenchmark
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
    public class IteratorBenchmarks
    {
        private SimpleObject _simpleObject = new SimpleObject();
        private ComplexObject _complexObject = new ComplexObject();
        private ObjectMemberIterator _iterator = new ObjectMemberIterator();

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

            _iterator = new ObjectMemberIterator();
        }

        [Benchmark]
        public void Traverse_SimpleObject()
        {
            _iterator.Traverse(_simpleObject, static (in MemberAccessor accessor) =>
            {
                // Just visit
            });
        }

        [Benchmark]
        public void Traverse_ComplexObject()
        {
            _iterator.Traverse(_complexObject, static (in MemberAccessor accessor) =>
            {
                // Just visit
            });
        }
    }

#pragma warning disable CS1591
    public class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<IteratorBenchmarks>();
        }
    }
}
