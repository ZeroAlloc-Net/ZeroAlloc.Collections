using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Collections.Generators.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UndisposedPooledCollectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZAC001";

    private static readonly LocalizableString Title =
        "Pooled collection should be disposed";

    private static readonly LocalizableString MessageFormat =
        "'{0}' should be disposed. Use a 'using' statement or call Dispose() explicitly.";

    private static readonly LocalizableString Description =
        "Pooled collections rent buffers from ArrayPool and must be disposed to return them.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>
    /// The set of type names that this analyzer checks for undisposed usage.
    /// </summary>
    private static readonly ImmutableHashSet<string> TrackedTypeNames = ImmutableHashSet.Create(
        "PooledList",
        "PooledStack",
        "PooledQueue",
        "RingBuffer",
        "SpanDictionary");

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var localDecl = (LocalDeclarationStatementSyntax)context.Node;

        // Skip if already in a using statement
        if (localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) &&
            localDecl.UsingKeyword.ValueText == "using")
        {
            return;
        }

        // Check if parent is a using statement
        if (localDecl.Parent is UsingStatementSyntax)
        {
            return;
        }

        foreach (var variable in localDecl.Declaration.Variables)
        {
            if (variable.Initializer?.Value is null)
                continue;

            var typeInfo = context.SemanticModel.GetTypeInfo(variable.Initializer.Value, context.CancellationToken);
            var type = typeInfo.Type;
            if (type is null)
                continue;

            // Check the simple type name (without generic arguments)
            var typeName = type.Name;
            if (!TrackedTypeNames.Contains(typeName))
                continue;

            // Check the type is from ZeroAlloc.Collections namespace
            var containingNamespace = type.ContainingNamespace?.ToDisplayString();
            if (containingNamespace != "ZeroAlloc.Collections")
                continue;

            // Check if Dispose() is called on the variable in the containing block
            var variableName = variable.Identifier.ValueText;
            var containingBlock = localDecl.Parent;
            if (containingBlock is null)
                continue;

            if (IsDisposeCalledOnVariable(variableName, containingBlock, localDecl))
                continue;

            var diagnostic = Diagnostic.Create(Rule, variable.GetLocation(), typeName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsDisposeCalledOnVariable(string variableName, SyntaxNode containingBlock, LocalDeclarationStatementSyntax declaration)
    {
        // Look for .Dispose() calls on the variable after the declaration
        foreach (var invocation in containingBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "Dispose" &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == variableName &&
                invocation.SpanStart > declaration.SpanStart)
            {
                return true;
            }
        }

        return false;
    }
}
