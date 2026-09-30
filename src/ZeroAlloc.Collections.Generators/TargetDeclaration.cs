using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Collections.Generators;

/// <summary>
/// Where and how a generator reopens the attributed type: its namespace, a partial declaration
/// of every containing type, outermost first, and the type's own partial declaration. The same
/// approach as ZeroAlloc.Mapping's <c>HostDeclarations</c>. Plain strings, so a model holding it
/// compares by value and the incremental pipeline can cache it.
/// </summary>
/// <param name="Namespace">The namespace, or null in the global namespace.</param>
/// <param name="ContainingTypeHeaders">A partial declaration per containing type, outermost first.</param>
/// <param name="Header">The type's own partial declaration, as in <c>public partial struct Items&lt;T&gt;</c>.</param>
/// <param name="Name">The type's name as a constructor is written: escaped, without type parameters.</param>
/// <param name="FullName">The type's display name, for diagnostics.</param>
/// <param name="NonPartialContainingType">The outermost containing type that is not partial, or null (ZAC013).</param>
/// <param name="Location">Where to report ZAC013; set only when <paramref name="NonPartialContainingType"/> is.</param>
internal readonly record struct TargetDeclaration(
    string? Namespace,
    EquatableArray<string> ContainingTypeHeaders,
    string Header,
    string Name,
    string FullName,
    string? NonPartialContainingType,
    LocationInfo? Location)
{
    /// <summary>
    /// Reported for a nested type whose containing type is not partial: the generated code has to
    /// reopen every containing type, which only a partial type allows, so nothing is generated.
    /// </summary>
    public static readonly DiagnosticDescriptor ContainingTypeNotPartial = new(
        id: "ZAC013",
        title: "Nested type inside a containing type that is not partial",
        messageFormat: "'{0}' is not generated because its containing type '{1}' is not partial",
        category: "ZeroAlloc.Collections.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static TargetDeclaration From(INamedTypeSymbol type, SyntaxNode node)
    {
        var headers = ImmutableArray.CreateBuilder<string>();
        INamedTypeSymbol? nonPartial = null;
        for (var t = type.ContainingType; t is not null; t = t.ContainingType)
        {
            headers.Insert(0, Declaration(t, accessibility: null));
            if (!IsPartial(t)) nonPartial = t;
        }

        return new TargetDeclaration(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            new EquatableArray<string>(headers.ToImmutable()),
            Declaration(type, AccessibilityKeyword(type.DeclaredAccessibility)),
            Identifier(type.Name),
            type.ToDisplayString(),
            nonPartial?.ToDisplayString(),
            nonPartial is null ? null : LocationInfo.From(node.GetLocation()));
    }

    /// <summary>Reports ZAC013 when it applies, and returns whether code may be generated.</summary>
    public bool CanGenerate(SourceProductionContext spc)
    {
        if (NonPartialContainingType is null) return true;
        spc.ReportDiagnostic(Diagnostic.Create(
            ContainingTypeNotPartial, Location?.ToLocation(), FullName, NonPartialContainingType));
        return false;
    }

    /// <summary>
    /// Opens the namespace and the containing types, and returns the indent the type's own
    /// declaration is written at. A type at the top of a namespace is written as it always was.
    /// </summary>
    public string Open(StringBuilder sb)
    {
        var indent = "";
        if (Namespace is not null)
        {
            sb.AppendLine($"namespace {Namespace}");
            sb.AppendLine("{");
            indent = "    ";
        }

        foreach (var header in ContainingTypeHeaders)
        {
            sb.AppendLine($"{indent}{header}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }
        return indent;
    }

    /// <summary>Closes what <see cref="Open"/> opened.</summary>
    public void Close(StringBuilder sb)
    {
        var indent = Namespace is not null ? "    " : "";
        for (var i = ContainingTypeHeaders.Length; i > 0; i--)
        {
            sb.AppendLine($"{indent}{new string(' ', 4 * (i - 1))}}}");
        }

        if (Namespace is not null)
        {
            sb.AppendLine("}");
        }
    }

    // `accessibility partial kind Name<T>`, with `ref` for a ref struct. Type parameter names only:
    // a partial part may leave out constraints, and variance never appears here, because an
    // interface with a variant type parameter cannot contain types.
    private static string Declaration(INamedTypeSymbol type, string? accessibility)
    {
        var sb = new StringBuilder();
        if (accessibility is not null) sb.Append(accessibility).Append(' ');
        if (type.IsRefLikeType) sb.Append("ref ");
        sb.Append("partial ").Append(Keyword(type)).Append(' ').Append(Identifier(type.Name));
        if (type.TypeParameters.Length > 0)
        {
            sb.Append('<');
            for (var i = 0; i < type.TypeParameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Identifier(type.TypeParameters[i].Name));
            }
            sb.Append('>');
        }
        return sb.ToString();
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(static r =>
            r.GetSyntax() is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static string Keyword(INamedTypeSymbol type) => type switch
    {
        { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Struct } => "struct",
        { TypeKind: TypeKind.Interface } => "interface",
        _ => "class",
    };

    // Every partial part must repeat the same accessibility, or leave it out (CS0262).
    private static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}

/// <summary>A <see cref="Location"/> captured as plain values, so a model holding it compares by value.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo From(Location location) =>
        new(location.SourceTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}
