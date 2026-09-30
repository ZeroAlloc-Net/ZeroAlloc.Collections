using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Collections.Generators;

namespace ZeroAlloc.Collections.Tests.Generators;

/// <summary>
/// A generated file is named after its type's namespace and containing types, so two types with
/// the same simple name never produce the same hint name. A duplicate hint name made the generator
/// throw CS8785, and then that generator produced nothing for the whole project.
/// </summary>
public class HintNameTests
{
    private static string List(string name) =>
        $"[ZeroAllocList(typeof(int))] public partial struct {name};";

    private static string Pooled(string name) =>
        $"[PooledCollection(typeof(int))] public partial struct {name};";

    private static string Enumerable(string name) =>
        $"[ZeroAllocEnumerable] public partial class {name} {{ private int[] _items = []; private int _count = 0; }}";

    public static TheoryData<string, string> Generators() => new()
    {
        { nameof(List), ".ZeroAllocList.g.cs" },
        { nameof(Pooled), ".PooledCollection.g.cs" },
        { nameof(Enumerable), ".ZeroAllocEnumerable.g.cs" },
    };

    private static string Declare(string generator, string name) => generator switch
    {
        nameof(List) => List(name),
        nameof(Pooled) => Pooled(name),
        _ => Enumerable(name),
    };

    private static (string[] HintNames, Diagnostic[] Diagnostics) Run(string source)
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(typeof(System.Buffers.ArrayPool<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAllocListAttribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "HintNameTests",
            [CSharpSyntaxTree.ParseText("using ZeroAlloc.Collections;\n" + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        CSharpGeneratorDriver.Create(
                new ZeroAllocListGenerator().AsSourceGenerator(),
                new PooledCollectionGenerator().AsSourceGenerator(),
                new ZeroAllocEnumerableGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var hintNames = output.SyntaxTrees
            .Skip(1)
            .Select(t => Path.GetFileName(t.FilePath))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var diagnostics = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
        return (hintNames, diagnostics);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void SameNamedTypes_InDifferentNamespaces_AreBothGenerated(string generator, string suffix)
    {
        var (hintNames, diagnostics) = Run($$"""
            namespace App.Orders { {{Declare(generator, "Items")}} }
            namespace App.Customers { {{Declare(generator, "Items")}} }
            """);

        Assert.Empty(diagnostics);
        Assert.Equal(["App.Customers.Items" + suffix, "App.Orders.Items" + suffix], hintNames);
    }

    // The generators emit a nested or generic type at namespace level without its containing
    // types or type parameters, so its members land on a different type (#141). These tests
    // pin only the hint names: each type gets its own file and none is dropped.
    [Theory]
    [MemberData(nameof(Generators))]
    public void SameNamedTypes_InDifferentContainingTypes_GetUniqueHintNames(string generator, string suffix)
    {
        var (hintNames, diagnostics) = Run($$"""
            namespace App
            {
                public partial class Orders { {{Declare(generator, "Items")}} }
                public partial class Customers { {{Declare(generator, "Items")}} }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "CS8785", StringComparison.Ordinal));
        Assert.Equal(["App.Customers+Items" + suffix, "App.Orders+Items" + suffix], hintNames);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void SameNamedTypes_OfDifferentArity_GetUniqueHintNames(string generator, string suffix)
    {
        var (hintNames, diagnostics) = Run($$"""
            namespace App
            {
                {{Declare(generator, "Items")}}
                {{Declare(generator, "Items<T>")}}
                public partial class Outer { {{Declare(generator, "Items")}} }
                public partial class Outer<T> { {{Declare(generator, "Items")}} }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "CS8785", StringComparison.Ordinal));
        Assert.Equal(
            [
                "App.Items" + suffix,
                "App.Items`1" + suffix,
                "App.Outer+Items" + suffix,
                "App.Outer`1+Items" + suffix,
            ],
            hintNames);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void TypeInGlobalNamespace_IsNamedWithoutANamespacePrefix(string generator, string suffix)
    {
        var (hintNames, diagnostics) = Run(Declare(generator, "Items"));

        Assert.Empty(diagnostics);
        Assert.Equal(["Items" + suffix], hintNames);
    }

    /// <summary>
    /// A C# identifier cannot hold a character that is invalid in a hint name, so escaping guards
    /// names the generator is handed rather than names users write. The escape starts with '-',
    /// which no identifier contains, so an escaped name cannot collide with one that needed none.
    /// </summary>
    [Theory]
    [InlineData("M", "M")]
    [InlineData("App.M", "App.M")]
    [InlineData("App.Outer+M`1", "App.Outer+M`1")]
    [InlineData("Café.Ωmega_1", "Café.Ωmega_1")]
    [InlineData("a/b|c:d*e?f<g>h", "a-u002Fb-u007Cc-u003Ad-u002Ae-u003Ff-u003Cg-u003Eh")]
    [InlineData("a-b", "a-u002Db")]
    public void Sanitize_KeepsIdentifierCharactersAndEscapesTheRest(string name, string expected)
    {
        Assert.Equal(expected, HintNames.Sanitize(name));
    }
}
