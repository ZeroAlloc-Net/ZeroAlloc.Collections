using Xunit;

namespace ZeroAlloc.Collections.Tests.Generators;

/// <summary>
/// The generated indexers are <c>AggressiveInlining</c>. A <c>throw</c> inside them is inlined
/// into every caller and can stop the JIT from inlining the indexer at all, so the bounds check
/// calls a non-inlined throw helper instead.
/// </summary>
public class IndexerThrowHelperTests
{
    [Theory]
    [InlineData(GeneratorRunner.List)]
    [InlineData(GeneratorRunner.Pooled)]
    public void GeneratedIndexer_DoesNotThrowInline(string generator)
    {
        var (_, sources, diagnostics) = GeneratorRunner.Run(GeneratorRunner.Declare(generator, "Items"));

        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        var indexer = source[source.IndexOf("this[int index]", StringComparison.Ordinal)..];
        indexer = indexer[..indexer.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.DoesNotContain("throw", indexer, StringComparison.Ordinal);
        Assert.Contains("ThrowIndexOutOfRange();", indexer, StringComparison.Ordinal);
        Assert.Contains("[MethodImpl(MethodImplOptions.NoInlining)]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedIndexers_ThrowArgumentOutOfRange_ForIndex()
    {
        var list = new GeneratedIntList(4);
        var buffer = new GeneratedDoubleBuffer();
        try
        {
            list.Add(1);
            buffer.Add(1.0);
            var listError = Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
            var bufferError = Assert.Throws<ArgumentOutOfRangeException>(() => buffer[-1]);
            Assert.Equal("index", listError.ParamName);
            Assert.Equal("index", bufferError.ParamName);
        }
        finally
        {
            list.Dispose();
            buffer.Dispose();
        }
    }
}
