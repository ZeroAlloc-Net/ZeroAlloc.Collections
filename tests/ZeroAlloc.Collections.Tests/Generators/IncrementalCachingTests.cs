using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Collections.Generators;

namespace ZeroAlloc.Collections.Tests.Generators;

/// <summary>
/// The [ZeroAllocEnumerable] model carries the diagnostics it reports. They used to sit in an
/// ImmutableArray, which compares by reference, so a model with a diagnostic never matched its
/// previous run and its output was rebuilt on every edit anywhere in the project.
/// </summary>
public class IncrementalCachingTests
{
    [Fact]
    public void EnumerableWithADiagnostic_IsNotRebuiltAfterAnUnrelatedEdit()
    {
        // Two int fields make ZAC011 report, and the generator still emits the enumerator.
        var source = CSharpSyntaxTree.ParseText("""
            using ZeroAlloc.Collections;
            namespace App;
            [ZeroAllocEnumerable]
            public partial class Items { private int[] _items = []; private int _count = 0; private int _other = 0; }
            """);
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(
            "CachingTests",
            [source],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(ZeroAllocListAttribute).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ZeroAllocEnumerableGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        Assert.Contains(driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAC011", StringComparison.Ordinal));

        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace App; public class Unrelated;")));

        var outputs = driver.GetRunResult().Results.Single().TrackedOutputSteps
            .SelectMany(step => step.Value)
            .SelectMany(run => run.Outputs)
            .ToArray();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"output was {output.Reason}"));
    }
}
