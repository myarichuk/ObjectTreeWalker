using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ObjectTreeWalker.SourceGenerator
{
    /// <summary>
    /// Base class for source generators that target classes decorated with a specific attribute.
    /// Provides boilerplate code for finding targets, setting up the incremental generation pipeline,
    /// and generating source code files.
    /// </summary>
    public abstract class AttributeTargetedGeneratorBase : IIncrementalGenerator
    {
        /// <summary>
        /// Gets the fully qualified name of the attribute to look for.
        /// </summary>
        protected abstract string AttributeName { get; }

        /// <summary>
        /// Gets the suffix to append to the generated file name.
        /// </summary>
        protected abstract string FileSuffix { get; }

        /// <summary>
        /// Initializes the generator.
        /// </summary>
        /// <param name="context">The incremental generator initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var provider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: GetSemanticTargetForGeneration)
                .Where(static m => m is not null);

            var compilationAndClasses = context.CompilationProvider.Combine(provider.Collect());

            context.RegisterSourceOutput(compilationAndClasses, (spc, source) => Execute(source.Left, source.Right!, spc));
        }

        private ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context, System.Threading.CancellationToken token)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            foreach (var attributeListSyntax in classDeclaration.AttributeLists)
            {
                foreach (var attributeSyntax in attributeListSyntax.Attributes)
                {
                    if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is IMethodSymbol attributeSymbol)
                    {
                        if (attributeSymbol.ContainingType.ToDisplayString() == AttributeName)
                        {
                            return classDeclaration;
                        }
                    }
                }
            }
            return null;
        }

        private void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context)
        {
            if (classes.IsDefaultOrEmpty) return;

            var distinctClasses = classes.Where(c => c != null).Distinct();

            foreach (var classDeclaration in distinctClasses)
            {
                var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var symbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (symbol == null) continue;

                var source = ProcessClass(symbol);
                context.AddSource($"{symbol.Name}_{FileSuffix}.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        /// <summary>
        /// Processes a targeted class and generates the corresponding source code.
        /// </summary>
        /// <param name="classSymbol">The symbol of the class being processed.</param>
        /// <returns>The generated source code for the class.</returns>
        protected abstract string ProcessClass(INamedTypeSymbol classSymbol);
    }
}
