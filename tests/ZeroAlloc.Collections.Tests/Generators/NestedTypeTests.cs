using Microsoft.CodeAnalysis;
using Xunit;
using static ZeroAlloc.Collections.Tests.Generators.GeneratorRunner;

namespace ZeroAlloc.Collections.Tests.Generators;

/// <summary>
/// Each generator emits into the real attributed type, wherever it is declared: nested in other
/// types, or generic. The generators used to write every type as a non-generic type at the top
/// of its namespace, so a nested or generic type got none of the generated members (#141). Each
/// case here compiles code that uses the generated members on the real type.
/// </summary>
public class NestedTypeTests
{
    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void NestedTypes_WithTheSameName_AreGeneratedIntoTheirOwnContainingTypes(string generator, string suffix)
    {
        var result = Run($$"""
            namespace App
            {
                public partial class Orders { {{Declare(generator, "Items")}} }
                public partial class Customers { {{Declare(generator, "Items")}} }
                public static class Use
                {
                    public static void Run(Orders.Items a, Customers.Items b) { {{Use(generator, "a")}} {{Use(generator, "b")}} }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["App.Customers+Items" + suffix, "App.Orders+Items" + suffix], result.HintNames);
    }

    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void GenericTypes_OfEveryArity_AreGeneratedWithTheirTypeParameters(string generator, string suffix)
    {
        var result = Run($$"""
            namespace App
            {
                {{Declare(generator, "Items")}}
                {{Declare(generator, "Items<T> where T : class")}}
                {{Declare(generator, "Items<T1, T2> where T1 : struct where T2 : new()")}}
                public static class Use
                {
                    public static void Run(Items a, Items<string> b, Items<int, object> c)
                    {
                        {{Use(generator, "a")}}
                        {{Use(generator, "b")}}
                        {{Use(generator, "c")}}
                    }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["App.Items" + suffix, "App.Items`1" + suffix, "App.Items`2" + suffix], result.HintNames);
    }

    // The element type of [ZeroAllocEnumerable] comes from the backing array, so it may be one of
    // the type's own type parameters.
    [Fact]
    public void EnumerableOverItsOwnTypeParameter_IsGenerated()
    {
        var result = Run("""
            namespace App
            {
                [ZeroAllocEnumerable]
                public partial class Bag<T>
                {
                    private T[] _items = [];
                    private int _count = 0;
                }
                public static class Use
                {
                    public static string First(Bag<string> bag)
                    {
                        foreach (var item in bag) return item;
                        return "";
                    }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void TypesInContainingTypesOfEveryKind_AreGeneratedIntoTheRealType(string generator, string suffix)
    {
        var result = Run($$"""
            namespace App
            {
                public partial struct Holder<T> where T : class { {{Declare(generator, "InStruct")}} }
                public partial interface IHolder { {{Declare(generator, "InInterface")}} }
                public partial record struct RecordStruct { {{Declare(generator, "InRecordStruct")}} }
                public partial record Record(int X) { {{Declare(generator, "InRecord")}} }
                public readonly ref partial struct RefHolder { {{Declare(generator, "InRefStruct")}} }
                public static partial class Outer<T>
                {
                    public partial class Middle<U> { {{Declare(generator, "Deep<V>")}} }
                }
                public static class Use
                {
                    public static void Run(
                        Holder<string>.InStruct a, IHolder.InInterface b, RecordStruct.InRecordStruct c,
                        Record.InRecord d, RefHolder.InRefStruct e, Outer<int>.Middle<string>.Deep<long> f)
                    {
                        {{Use(generator, "a")}}
                        {{Use(generator, "b")}}
                        {{Use(generator, "c")}}
                        {{Use(generator, "d")}}
                        {{Use(generator, "e")}}
                        {{Use(generator, "f")}}
                    }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("App.Outer`1+Middle`1+Deep`1" + suffix, result.HintNames, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void TypesWithKeywordNames_AreGeneratedWithVerbatimIdentifiers(string generator, string suffix)
    {
        var result = Run($$"""
            namespace App
            {
                public partial class @class<@int> { {{Declare(generator, "@static<@void>")}} }
                public static class Use
                {
                    public static void Run(@class<string>.@static<int> x) { {{Use(generator, "x")}} }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["App.class`1+static`1" + suffix], result.HintNames);
    }

    // Every partial part must repeat the declared accessibility. Nested types can be protected,
    // protected internal or private protected, which the generators used to write as internal.
    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void NestedTypes_KeepTheirDeclaredAccessibility(string generator, string suffix)
    {
        var result = Run($$"""
            namespace App
            {
                public partial class Outer
                {
                    {{Declare(generator, "A", "protected")}}
                    {{Declare(generator, "B", "protected internal")}}
                    {{Declare(generator, "C", "private protected")}}
                    {{Declare(generator, "D", "private")}}
                    private void Run(A a, B b, C c, D d)
                    {
                        {{Use(generator, "a")}}
                        {{Use(generator, "b")}}
                        {{Use(generator, "c")}}
                        {{Use(generator, "d")}}
                    }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(4, result.HintNames.Length);
        Assert.All(result.HintNames, h => Assert.EndsWith(suffix, h, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void TypeNestedInATypeDeclaredInSeveralParts_IsGenerated(string generator, string suffix)
    {
        var result = Run($$"""
            namespace App
            {
                public partial class Outer { public int A; }
                public partial class Outer { {{Declare(generator, "Items")}} }
                public static class Use
                {
                    public static void Run(Outer.Items x) { {{Use(generator, "x")}} }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["App.Outer+Items" + suffix], result.HintNames);
    }

    // A record or ref struct target is reopened with its own kind; the generators used to write
    // every target as a plain struct or class, which C# rejects for a record.
    [Theory]
    [InlineData("[ZeroAllocList(typeof(int))] public partial record struct Items;", GeneratorRunner.List)]
    [InlineData("[ZeroAllocList(typeof(int))] public ref partial struct Items;", GeneratorRunner.List)]
    [InlineData("[PooledCollection(typeof(int))] public partial record struct Items;", GeneratorRunner.Pooled)]
    [InlineData("[ZeroAllocEnumerable] public partial record Items { private int[] _items = []; private int _count = 0; }", GeneratorRunner.Enumerable)]
    public void TargetsOfEveryKind_AreReopenedWithTheirOwnKind(string declaration, string generator)
    {
        var result = Run($$"""
            namespace App
            {
                {{declaration}}
                public static class Use
                {
                    public static void Run(Items x) { {{Use(generator, "x")}} }
                }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.HintNames);
    }

    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void TypeInANonPartialContainingType_ReportsZac013_AndGeneratesNothing(string generator, string suffix)
    {
        _ = suffix;
        var source = $$"""
            namespace App
            {
                public class Outer { {{Declare(generator, "Items")}} }
            }
            """;

        var result = Run(source);

        var diagnostic = Assert.Single(WithoutUnusedFieldWarnings(result.Diagnostics));
        Assert.Equal("ZAC013", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "'App.Outer.Items' is not generated because its containing type 'App.Outer' is not partial",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var start = ("using ZeroAlloc.Collections;\n" + source).IndexOf('[', StringComparison.Ordinal);
        Assert.Equal(start, diagnostic.Location.SourceSpan.Start);
        Assert.Empty(result.HintNames);
    }

    [Theory]
    [MemberData(nameof(Generators), MemberType = typeof(GeneratorRunner))]
    public void TypeUnderANonPartialOuterType_ReportsZac013_NamingTheOutermostOne(string generator, string suffix)
    {
        _ = suffix;
        var result = Run($$"""
            namespace App
            {
                public class Outer
                {
                    public partial class Middle { {{Declare(generator, "Items")}} }
                }
            }
            """);

        var diagnostic = Assert.Single(WithoutUnusedFieldWarnings(result.Diagnostics));
        Assert.Equal("ZAC013", diagnostic.Id);
        Assert.Equal(
            "'App.Outer.Middle.Items' is not generated because its containing type 'App.Outer' is not partial",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(result.HintNames);
    }

    // With nothing generated, the backing fields of an Enumerable target are never read, which
    // the compiler reports as CS0414. That follows from ZAC013, so these tests set it aside.
    private static Diagnostic[] WithoutUnusedFieldWarnings(Diagnostic[] diagnostics) =>
        diagnostics.Where(d => !string.Equals(d.Id, "CS0414", StringComparison.Ordinal)).ToArray();
}
