#pragma warning disable CS1591
using System.Text;
using Microsoft.CodeAnalysis;

namespace ObjectTreeWalker.SourceGenerator
{
    /// <summary>
    /// Source generator for creating deep clone functionality for classes marked with ObjectTreeWalker.DeepCloneableAttribute.
    /// Implements deep copying logic for properties and fields dynamically using Roslyn Source Generators.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public class DeepCloneGenerator : AttributeTargetedGeneratorBase
    {
        /// <inheritdoc />
        protected override string AttributeName => "ObjectTreeWalker.DeepCloneableAttribute";

        /// <inheritdoc />
        protected override string FileSuffix => "DeepClone";

        /// <inheritdoc />
        protected override string ProcessClass(INamedTypeSymbol classSymbol)
        {
            var className = classSymbol.Name;
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString();

            var builder = new ClassBuilder();
            builder.EmitFileHeader();
            builder.StartNamespace(namespaceName);
            builder.StartClass(className);

            GenerateDeepCloneMethod(builder.StringBuilder, classSymbol, className);

            builder.EndClass();
            builder.EndNamespace(namespaceName);

            return builder.ToString();
        }

        private static void GenerateDeepCloneMethod(StringBuilder sb, INamedTypeSymbol classSymbol, string className)
        {
            sb.AppendLine($"        public {className} DeepClone(ObjectTreeWalker.CloningContext? ctx = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            var isRoot = ctx == null;");
            sb.AppendLine("            ctx ??= new ObjectTreeWalker.CloningContext();");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                if (ctx.TryGetClone(this, out var existingClone)) return ({className})existingClone!;");
            sb.AppendLine($"                var clone = new {className}();");
            sb.AppendLine("                ctx.RecordClone(this, clone);");

            GenerateFieldCloning(sb, classSymbol);

            sb.AppendLine("                return clone;");
            sb.AppendLine("            }");
            sb.AppendLine("            finally");
            sb.AppendLine("            {");
            sb.AppendLine("                if (isRoot) ctx.Dispose();");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
        }

        private static void GenerateFieldCloning(StringBuilder sb, INamedTypeSymbol classSymbol)
        {
            // Use the SymbolExtensions to get traversable fields
            foreach (var member in classSymbol.GetTraversableFields(ignoreReadOnly: true))
            {
                if (member.Type.IsValueTypeOrString())
                {
                    sb.AppendLine($"                clone.{member.Name} = this.{member.Name};");
                }
                else
                {
                    sb.AppendLine($"                clone.{member.Name} = this.{member.Name}?.DeepClone(ctx);");
                }
            }

            // Use the SymbolExtensions to get traversable properties
            foreach (var member in classSymbol.GetTraversableProperties(requireSetter: true))
            {
                if (member.Type.IsValueTypeOrString())
                {
                    sb.AppendLine($"                clone.{member.Name} = this.{member.Name};");
                }
                else
                {
                    sb.AppendLine($"                clone.{member.Name} = this.{member.Name}?.DeepClone(ctx);");
                }
            }
        }
    }
}
