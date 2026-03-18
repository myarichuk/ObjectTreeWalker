using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ObjectTreeWalker.SourceGenerator
{
    /// <summary>
    /// Extension methods for Roslyn symbols, specifically tailored for source generation
    /// of object traversal and cloning functionality.
    /// </summary>
    public static class SymbolExtensions
    {
        /// <summary>
        /// Retrieves all instance fields from a class symbol that are suitable for traversal or cloning.
        /// Ignores static fields, read-only fields (if <paramref name="ignoreReadOnly"/> is true),
        /// pointers, function pointers, and compiler-generated backing fields.
        /// </summary>
        /// <param name="classSymbol">The class symbol to inspect.</param>
        /// <param name="ignoreReadOnly">Whether to ignore read-only fields.</param>
        /// <returns>An enumerable of valid field symbols.</returns>
        public static IEnumerable<IFieldSymbol> GetTraversableFields(this INamedTypeSymbol classSymbol, bool ignoreReadOnly = false)
        {
            foreach (var member in classSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.IsStatic) continue;
                if (ignoreReadOnly && member.IsReadOnly) continue;
                if (member.Type.TypeKind == TypeKind.Pointer || member.Type.TypeKind == TypeKind.FunctionPointer) continue;
                // Avoid using backing fields directly if they are compiler generated as their name is invalid in C#
                if (member.Name.Contains("k__BackingField")) continue;

                yield return member;
            }
        }

        /// <summary>
        /// Retrieves all instance properties from a class symbol that are suitable for traversal or cloning.
        /// Ignores static properties, properties without get (and optionally set) methods,
        /// pointers, function pointers, and compiler-generated properties.
        /// </summary>
        /// <param name="classSymbol">The class symbol to inspect.</param>
        /// <param name="requireSetter">Whether a property must have a setter to be included.</param>
        /// <returns>An enumerable of valid property symbols.</returns>
        public static IEnumerable<IPropertySymbol> GetTraversableProperties(this INamedTypeSymbol classSymbol, bool requireSetter = false)
        {
            foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic) continue;
                if (member.GetMethod == null) continue;
                if (requireSetter && (member.SetMethod == null || member.IsReadOnly)) continue;
                if (member.Type.TypeKind == TypeKind.Pointer || member.Type.TypeKind == TypeKind.FunctionPointer) continue;

                var isCompilerGenerated = member.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "CompilerGeneratedAttribute" ||
                    a.AttributeClass?.Name == "CompilerGenerated");

                if (isCompilerGenerated) continue;

                yield return member;
            }
        }

        /// <summary>
        /// Determines whether a type is a value type or the system string type.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to check.</param>
        /// <returns>True if it is a value type or string, otherwise false.</returns>
        public static bool IsValueTypeOrString(this ITypeSymbol typeSymbol)
        {
            return typeSymbol.IsValueType || typeSymbol.SpecialType == SpecialType.System_String;
        }
    }
}
