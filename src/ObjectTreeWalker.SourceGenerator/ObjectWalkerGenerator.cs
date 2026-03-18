#pragma warning disable CS1591
using System.Text;
using Microsoft.CodeAnalysis;

namespace ObjectTreeWalker.SourceGenerator
{
    /// <summary>
    /// Source generator for creating object graph traversal functionality for classes marked with ObjectTreeWalker.ObjectWalkableAttribute.
    /// Implements traversal logic for properties and fields dynamically using Roslyn Source Generators.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public class ObjectWalkerGenerator : AttributeTargetedGeneratorBase
    {
        /// <inheritdoc />
        protected override string AttributeName => "ObjectTreeWalker.ObjectWalkableAttribute";

        /// <inheritdoc />
        protected override string FileSuffix => "Walk";

        /// <inheritdoc />
        protected override string ProcessClass(INamedTypeSymbol classSymbol)
        {
            var className = classSymbol.Name;
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString();

            var builder = new ClassBuilder();
            builder.EmitFileHeader();
            builder.StartNamespace(namespaceName);
            builder.StartClass(className);

            GenerateWalkMethod(builder.StringBuilder, classSymbol, className);

            builder.EndClass();
            builder.EndNamespace(namespaceName);

            return builder.ToString();
        }

        private static void GenerateWalkMethod(StringBuilder sb, INamedTypeSymbol classSymbol, string className)
        {
            sb.AppendLine("        public void Walk(ObjectTreeWalker.IObjectVisitor visitor)");
            sb.AppendLine("        {");
            sb.AppendLine("            var queue = ObjectTreeWalker.CollectionPools.RentBfsQueue();");
            sb.AppendLine("            var visited = ObjectTreeWalker.CollectionPools.RentVisitedDictionary();");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                queue.Enqueue(this);");
            sb.AppendLine("                visited[this] = null!;");
            sb.AppendLine("                while (queue.Count > 0)");
            sb.AppendLine("                {");
            sb.AppendLine("                    var current = queue.Dequeue();");
            sb.AppendLine("                    visitor.Visit(current);");
            sb.AppendLine($"                    if (current is {className} typedCurrent)");
            sb.AppendLine("                    {");

            GenerateFieldIteration(sb, classSymbol);

            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            finally");
            sb.AppendLine("            {");
            sb.AppendLine("                ObjectTreeWalker.CollectionPools.ReturnBfsQueue(queue);");
            sb.AppendLine("                ObjectTreeWalker.CollectionPools.ReturnVisitedDictionary(visited);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
        }

        private static void GenerateFieldIteration(StringBuilder sb, INamedTypeSymbol classSymbol)
        {
            // Use SymbolExtensions to get traversable fields
            foreach (var member in classSymbol.GetTraversableFields())
            {
                if (member.Type.IsValueTypeOrString()) continue;

                sb.AppendLine($"                        if (typedCurrent.{member.Name} != null && !visited.ContainsKey(typedCurrent.{member.Name}))");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            visited[typedCurrent.{member.Name}] = null!;");
                sb.AppendLine($"                            queue.Enqueue(typedCurrent.{member.Name});");
                sb.AppendLine("                        }");
            }

            // Use SymbolExtensions to get traversable properties
            foreach (var member in classSymbol.GetTraversableProperties())
            {
                if (member.Type.IsValueTypeOrString()) continue;

                sb.AppendLine($"                        if (typedCurrent.{member.Name} != null && !visited.ContainsKey(typedCurrent.{member.Name}))");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            visited[typedCurrent.{member.Name}] = null!;");
                sb.AppendLine($"                            queue.Enqueue(typedCurrent.{member.Name});");
                sb.AppendLine("                        }");
            }
        }
    }
}
