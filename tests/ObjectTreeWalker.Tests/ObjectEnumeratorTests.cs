// ReSharper disable ConvertToAutoProperty
// ReSharper disable TooManyDeclarations
// ReSharper disable ExceptionNotDocumented
#pragma warning disable CS0649
#pragma warning disable CS1591
namespace ObjectTreeWalker.Tests
{
    public class ObjectEnumeratorTests
    {
        public class ObjWithIntPtr
        {
            public IntPtr Ptr { get; set; }
        }

        public class ObjWithMemoryOfT
        {
            public Memory<char> Ptr { get; set; }
        }

        public class ObjWithString
        {
            public string Str { get; set; }
        }

        public class ObjWithDecimal
        {
            public decimal Decimal { get; set; }
        }

        public class ObjWithDynamic
        {
            public dynamic DynamicObj { get; set; }
        }

        public class FlatObj
        {
            public int Foo { get; set; }
            public string? Bar;
        }

        public class DeepObj
        {
            private int _foo;
            public int FooBar => _foo;

            public FlatObj? Obj { get; set; }
        }

        public class DeepObjWithStruct
        {
            private int _foo;
            public int FooBar => _foo;

            public FlatStruct? Obj { get; set; }
        }

        public struct DeepStruct
        {
            private int _foo;
            public int FooBar => _foo;

            public FlatStruct? Obj { get; set; }
        }

        public struct DeepStructWithNonNullable
        {
            private int _foo;
            public int FooBar => _foo;

            public FlatStruct Obj { get; set; }
        }


        public struct FlatStruct
        {
            public int Foo { get; set; }
            public string? Bar;
        }

        [Theory]
        [InlineData(typeof(ObjWithIntPtr))]
        [InlineData(typeof(ObjWithMemoryOfT))]
        [InlineData(typeof(ObjWithString))]
        [InlineData(typeof(ObjWithDynamic))]
        [InlineData(typeof(ObjWithDecimal))]
        public void Special_type_properties_should_not_be_traversable(Type objType)
        {
            var objectGraph = new ObjectEnumerator().Enumerate(objType);
            Assert.NotNull(objectGraph); //sanity check

            var rootGraphNode = objectGraph.Roots.FirstOrDefault();

            Assert.NotNull(rootGraphNode);
            Assert.Empty(rootGraphNode.Children);
        }

        [Fact]
        public void Can_enumerate_flat_object()
        {
            var objectGraph = new ObjectEnumerator().Enumerate(typeof(FlatObj));

            Assert.NotNull(objectGraph);
            Assert.Equal(2, objectGraph.Roots.Count);

            Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(FlatObj.Foo) && ogn.Type == typeof(int));
            Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(FlatObj.Bar) && ogn.Type == typeof(string));

            var stringPropNode = objectGraph.Roots.FirstOrDefault(ogn => ogn.Type == typeof(string));

            // strings should be treated as if they are primitives, their properties should not be traversable
            Assert.NotNull(stringPropNode);
            Assert.Empty(stringPropNode.Children);
        }

        [Fact]
        public void Can_enumerate_deep_object()
        {
            var objectGraph = new ObjectEnumerator().Enumerate(typeof(DeepObj));

            Assert.NotNull(objectGraph);

            Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(DeepObj.FooBar) && ogn.Type == typeof(int) && !ogn.CanSet);
            Assert.Contains(objectGraph.Roots, ogn => ogn.Name == "_foo" && ogn.Type == typeof(int));

            var embeddedObj = objectGraph.Roots.FirstOrDefault(ogn => ogn.Name == nameof(DeepObj.Obj));
            Assert.NotNull(embeddedObj);

            Assert.Contains(embeddedObj!.Children, ogn => ogn.Name == nameof(FlatObj.Foo) && ogn.Type == typeof(int));
            Assert.Contains(embeddedObj.Children, ogn => ogn.Name == nameof(FlatObj.Bar) && ogn.Type == typeof(string));
        }

        [Theory]
        [InlineData(typeof(DeepStruct))]
        [InlineData(typeof(DeepStructWithNonNullable))]
        [InlineData(typeof(DeepObjWithStruct))]
        public void Can_enumerate_embedded_struct(Type type)
        {
            var objectGraph = new ObjectEnumerator().Enumerate(type);

            Assert.NotNull(objectGraph);

            Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(DeepStruct.FooBar) && ogn.Type == typeof(int) && !ogn.CanSet);
            Assert.Contains(objectGraph.Roots, ogn => ogn.Name == "_foo" && ogn.Type == typeof(int));

            var embeddedObj = objectGraph.Roots.FirstOrDefault(ogn => ogn.Name == nameof(DeepStruct.Obj));
            Assert.NotNull(embeddedObj);

            Assert.Contains(embeddedObj!.Children, ogn => ogn.Name == nameof(FlatStruct.Foo) && ogn.Type == typeof(int));
            Assert.Contains(embeddedObj.Children, ogn => ogn.Name == nameof(FlatStruct.Bar) && ogn.Type == typeof(string));
        }
    }
}
