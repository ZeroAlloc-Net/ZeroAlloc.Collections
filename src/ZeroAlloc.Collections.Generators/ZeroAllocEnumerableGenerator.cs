using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Collections.Generators;

[Generator]
public sealed class ZeroAllocEnumerableGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "ZeroAlloc.Collections.ZeroAllocEnumerableAttribute";

    private static readonly DiagnosticDescriptor AmbiguousArrayField = new(
        id: "ZAC010",
        title: "Ambiguous backing array field",
        messageFormat: "Type '{0}' has multiple array fields; specify arrayFieldName in [ZeroAllocEnumerable] to avoid ambiguity",
        category: "ZeroAlloc.Collections.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbiguousCountField = new(
        id: "ZAC011",
        title: "Ambiguous count field",
        messageFormat: "Type '{0}' has multiple int fields; specify countFieldName in [ZeroAllocEnumerable] to avoid ambiguity",
        category: "ZeroAlloc.Collections.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FieldNotFound = new(
        id: "ZAC012",
        title: "Field not found",
        messageFormat: "Type '{0}' does not have a field named '{1}'",
        category: "ZeroAlloc.Collections.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Use unfiltered provider so diagnostics can be emitted even when generation is skipped
        var allTargets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeFullName,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => GetModel(ctx));

        // Emit diagnostics for every target (including error cases that return null code gen)
        context.RegisterSourceOutput(allTargets, static (spc, model) =>
        {
            if (model is null) return;
            foreach (var diag in model.Value.Diagnostics)
                spc.ReportDiagnostic(diag);
        });

        // Only generate code for models that resolved successfully
        var codeTargets = allTargets
            .Where(static m => m is not null && !m.Value.HasErrors)
            .Select(static (m, _) => m!.Value);

        context.RegisterSourceOutput(codeTargets, static (spc, model) => Execute(spc, model));
    }

    private static GeneratorModel? GetModel(GeneratorAttributeSyntaxContext ctx)
    {
        var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();
        bool hasErrors = false;

        // Read explicit names from attribute constructor arguments
        string? explicitArrayFieldName = null;
        string? explicitCountFieldName = null;

        var attr = ctx.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFullName);

        if (attr is not null && attr.ConstructorArguments.Length == 2)
        {
            explicitArrayFieldName = attr.ConstructorArguments[0].Value as string;
            explicitCountFieldName = attr.ConstructorArguments[1].Value as string;
        }

        // Collect field candidates
        var arrayFields = new List<IFieldSymbol>();
        var countFields = new List<IFieldSymbol>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IFieldSymbol field && !field.IsStatic)
            {
                if (field.Type is IArrayTypeSymbol)
                    arrayFields.Add(field);
                else if (field.Type.SpecialType == SpecialType.System_Int32)
                    countFields.Add(field);
            }
        }

        // Resolve array field
        IFieldSymbol? arrayField = null;
        if (explicitArrayFieldName is not null)
        {
            arrayField = arrayFields.Find(f => f.Name == explicitArrayFieldName);
            if (arrayField is null)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(FieldNotFound,
                    ctx.TargetNode.GetLocation(), typeSymbol.Name, explicitArrayFieldName));
                hasErrors = true;
            }
        }
        else if (arrayFields.Count == 0)
        {
            return null; // No array field — attribute not applicable to this type
        }
        else
        {
            if (arrayFields.Count > 1)
                diagnosticsBuilder.Add(Diagnostic.Create(AmbiguousArrayField,
                    ctx.TargetNode.GetLocation(), typeSymbol.Name));
            arrayField = arrayFields[0];
        }

        // Resolve count field
        IFieldSymbol? countField = null;
        if (explicitCountFieldName is not null)
        {
            countField = countFields.Find(f => f.Name == explicitCountFieldName);
            if (countField is null)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(FieldNotFound,
                    ctx.TargetNode.GetLocation(), typeSymbol.Name, explicitCountFieldName));
                hasErrors = true;
            }
        }
        else if (countFields.Count == 0)
        {
            return null; // No int field — attribute not applicable to this type
        }
        else
        {
            if (countFields.Count > 1)
                diagnosticsBuilder.Add(Diagnostic.Create(AmbiguousCountField,
                    ctx.TargetNode.GetLocation(), typeSymbol.Name));
            countField = countFields[0];
        }

        // If we have errors (missing named fields), still return a model so diagnostics get emitted
        if (hasErrors)
        {
            return new GeneratorModel(
                Namespace: null,
                TypeName: typeSymbol.Name,
                ArrayFieldName: string.Empty,
                CountFieldName: string.Empty,
                ElementTypeFullName: string.Empty,
                IsStruct: false,
                Accessibility: typeSymbol.DeclaredAccessibility,
                Diagnostics: diagnosticsBuilder.ToImmutable(),
                HasErrors: true);
        }

        var arrayElementType = ((IArrayTypeSymbol)arrayField!.Type)
            .ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : typeSymbol.ContainingNamespace.ToDisplayString();

        return new GeneratorModel(
            Namespace: ns,
            TypeName: typeSymbol.Name,
            ArrayFieldName: arrayField.Name,
            CountFieldName: countField!.Name,
            ElementTypeFullName: arrayElementType,
            IsStruct: typeSymbol.IsValueType,
            Accessibility: typeSymbol.DeclaredAccessibility,
            Diagnostics: diagnosticsBuilder.ToImmutable(),
            HasErrors: false);
    }

    private static void Execute(SourceProductionContext spc, GeneratorModel model)
    {
        var accessibility = model.Accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            _ => "internal"
        };

        var typeKind = model.IsStruct ? "struct" : "class";
        var readonlyMod = model.IsStruct ? "readonly " : "";
        var T = model.ElementTypeFullName;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();

        if (model.Namespace is not null)
        {
            sb.AppendLine($"namespace {model.Namespace}");
            sb.AppendLine("{");
        }

        var indent = model.Namespace is not null ? "    " : "";
        var indent2 = indent + "    ";
        var indent3 = indent2 + "    ";
        var indent4 = indent3 + "    ";

        sb.AppendLine($"{indent}{accessibility} partial {typeKind} {model.TypeName}");
        sb.AppendLine($"{indent}{{");

        // GetEnumerator
        sb.AppendLine($"{indent2}/// <summary>Returns an enumerator that iterates through the collection.</summary>");
        sb.AppendLine($"{indent2}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{indent2}public {readonlyMod}Enumerator GetEnumerator() => new Enumerator({model.ArrayFieldName}, {model.CountFieldName});");
        sb.AppendLine();

        // Nested Enumerator
        sb.AppendLine($"{indent2}/// <summary>Enumerates the elements.</summary>");
        sb.AppendLine($"{indent2}public ref struct Enumerator");
        sb.AppendLine($"{indent2}{{");
        sb.AppendLine($"{indent3}private readonly {T}[]? _items;");
        sb.AppendLine($"{indent3}private readonly int _count;");
        sb.AppendLine($"{indent3}private int _index;");
        sb.AppendLine();
        sb.AppendLine($"{indent3}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{indent3}internal Enumerator({T}[]? items, int count)");
        sb.AppendLine($"{indent3}{{");
        sb.AppendLine($"{indent4}_items = items;");
        sb.AppendLine($"{indent4}_count = count;");
        sb.AppendLine($"{indent4}_index = -1;");
        sb.AppendLine($"{indent3}}}");
        sb.AppendLine();
        sb.AppendLine($"{indent3}/// <summary>Gets the current element.</summary>");
        sb.AppendLine($"{indent3}public readonly ref readonly {T} Current");
        sb.AppendLine($"{indent3}{{");
        sb.AppendLine($"{indent4}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{indent4}get => ref _items![_index];");
        sb.AppendLine($"{indent3}}}");
        sb.AppendLine();
        sb.AppendLine($"{indent3}/// <summary>Advances the enumerator.</summary>");
        sb.AppendLine($"{indent3}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"{indent3}public bool MoveNext()");
        sb.AppendLine($"{indent3}{{");
        sb.AppendLine($"{indent4}int index = _index + 1;");
        sb.AppendLine($"{indent4}if (index < _count)");
        sb.AppendLine($"{indent4}{{");
        sb.AppendLine($"{indent4}    _index = index;");
        sb.AppendLine($"{indent4}    return true;");
        sb.AppendLine($"{indent4}}}");
        sb.AppendLine($"{indent4}return false;");
        sb.AppendLine($"{indent3}}}");
        sb.AppendLine($"{indent2}}}");

        sb.AppendLine($"{indent}}}");

        if (model.Namespace is not null)
        {
            sb.AppendLine("}");
        }

        spc.AddSource($"{model.TypeName}.ZeroAllocEnumerable.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private readonly record struct GeneratorModel(
        string? Namespace,
        string TypeName,
        string ArrayFieldName,
        string CountFieldName,
        string ElementTypeFullName,
        bool IsStruct,
        Accessibility Accessibility,
        ImmutableArray<Diagnostic> Diagnostics,  // ImmutableArray has structural equality — safe for incremental caching
        bool HasErrors);
}
