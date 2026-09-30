using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Collections.Generators;

namespace ZeroAlloc.Collections.Tests.Generators;

/// <summary>
/// Runs the three generators on an in-memory source and compiles the result, so a test can
/// check both what was generated and that the code using it builds.
/// </summary>
internal static class GeneratorRunner
{
    public const string List = nameof(List);
    public const string Pooled = nameof(Pooled);
    public const string Enumerable = nameof(Enumerable);

    public sealed record Result(string[] HintNames, string[] Sources, Diagnostic[] Diagnostics);

    /// <summary>Each generator with the suffix of the files it generates.</summary>
    public static TheoryData<string, string> Generators() => new()
    {
        { List, ".ZeroAllocList.g.cs" },
        { Pooled, ".PooledCollection.g.cs" },
        { Enumerable, ".ZeroAllocEnumerable.g.cs" },
    };

    /// <summary>
    /// A type the generator handles, named <paramref name="name"/>, which may carry type
    /// parameters and constraints, as in <c>Items&lt;T&gt; where T : class</c>. List and Pooled
    /// targets are structs over <c>int</c>; Enumerable targets are classes over an <c>int[]</c>.
    /// </summary>
    public static string Declare(string generator, string name, string modifiers = "public") => generator switch
    {
        List => $"[ZeroAllocList(typeof(int))] {modifiers} partial struct {name};",
        Pooled => $"[PooledCollection(typeof(int))] {modifiers} partial struct {name};",
        _ => $"[ZeroAllocEnumerable] {modifiers} partial class {name} {{ private int[] _items = []; private int _count = 0; }}",
    };

    /// <summary>A statement that uses a generated member of <paramref name="value"/>.</summary>
    public static string Use(string generator, string value) => generator switch
    {
        Enumerable => $"foreach (var item in {value}) {{ _ = item; }}",
        _ => $"{value}.Add(1); _ = {value}.Count; {value}.Dispose();",
    };

    /// <summary>
    /// Generated hint names and sources in hint-name order, and every warning or error from the
    /// generators and from compiling their output with the source.
    /// </summary>
    public static Result Run(string source)
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
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText("using ZeroAlloc.Collections;\n" + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        CSharpGeneratorDriver.Create(
                new ZeroAllocListGenerator().AsSourceGenerator(),
                new PooledCollectionGenerator().AsSourceGenerator(),
                new ZeroAllocEnumerableGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var generated = output.SyntaxTrees
            .Skip(1)
            .OrderBy(t => Path.GetFileName(t.FilePath), StringComparer.Ordinal)
            .ToArray();
        var diagnostics = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
        return new Result(
            generated.Select(t => Path.GetFileName(t.FilePath)).ToArray(),
            generated.Select(t => t.GetText().ToString()).ToArray(),
            diagnostics);
    }
}
