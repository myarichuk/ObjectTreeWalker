namespace ObjectTreeWalker.Tests
{
	public class ObjectEnumeratorTests
	{
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

		[Fact]
		public void Can_enumerate_flat_object()
		{
			var objectGraph = ObjectEnumerator.Enumerate(typeof(FlatObj));

			Assert.NotNull(objectGraph);
			Assert.Equal(2, objectGraph.Roots.Count);

			Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(FlatObj.Foo) && ogn.Type == typeof(int));
			Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(FlatObj.Bar) && ogn.Type == typeof(string));
		}

		[Fact]
		public void Can_enumerate_deep_object()
		{
			var objectGraph = ObjectEnumerator.Enumerate(typeof(DeepObj));

			Assert.NotNull(objectGraph);

			Assert.Contains(objectGraph.Roots, ogn => ogn.Name == nameof(DeepObj.FooBar) && ogn.Type == typeof(int) && !ogn.CanSet);
			Assert.Contains(objectGraph.Roots, ogn => ogn.Name == "_foo" && ogn.Type == typeof(int));

			var embeddedObj = objectGraph.Roots.FirstOrDefault(ogn => ogn.Name == nameof(DeepObj.Obj));
			Assert.NotNull(embeddedObj);

			Assert.Contains(embeddedObj!.Children, ogn => ogn.Name == nameof(FlatObj.Foo) && ogn.Type == typeof(int));
			Assert.Contains(embeddedObj.Children, ogn => ogn.Name == nameof(FlatObj.Bar) && ogn.Type == typeof(string));
		}
	}
}
