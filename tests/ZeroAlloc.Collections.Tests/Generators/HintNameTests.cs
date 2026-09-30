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
    [Theory]
    [MemberData(nameof(GeneratorRunner.Generators), MemberType = typeof(GeneratorRunner))]
    public void SameNamedTypes_InDifferentNamespaces_AreBothGenerated(string generator, string suffix)
    {
        var (hintNames, _, diagnostics) = GeneratorRunner.Run($$"""
            namespace App.Orders { {{GeneratorRunner.Declare(generator, "Items")}} }
            namespace App.Customers { {{GeneratorRunner.Declare(generator, "Items")}} }
            """);

        Assert.Empty(diagnostics);
        Assert.Equal(["App.Customers.Items" + suffix, "App.Orders.Items" + suffix], hintNames);
    }

    [Theory]
    [MemberData(nameof(GeneratorRunner.Generators), MemberType = typeof(GeneratorRunner))]
    public void SameNamedTypes_InDifferentContainingTypes_AreBothGenerated(string generator, string suffix)
    {
        var (hintNames, _, diagnostics) = GeneratorRunner.Run($$"""
            namespace App
            {
                public partial class Orders { {{GeneratorRunner.Declare(generator, "Items")}} }
                public partial class Customers { {{GeneratorRunner.Declare(generator, "Items")}} }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Equal(["App.Customers+Items" + suffix, "App.Orders+Items" + suffix], hintNames);
    }

    [Theory]
    [MemberData(nameof(GeneratorRunner.Generators), MemberType = typeof(GeneratorRunner))]
    public void SameNamedTypes_OfDifferentArity_AreAllGenerated(string generator, string suffix)
    {
        var (hintNames, _, diagnostics) = GeneratorRunner.Run($$"""
            namespace App
            {
                {{GeneratorRunner.Declare(generator, "Items")}}
                {{GeneratorRunner.Declare(generator, "Items<T>")}}
                public partial class Outer { {{GeneratorRunner.Declare(generator, "Items")}} }
                public partial class Outer<T> { {{GeneratorRunner.Declare(generator, "Items")}} }
            }
            """);

        Assert.Empty(diagnostics);
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
    [MemberData(nameof(GeneratorRunner.Generators), MemberType = typeof(GeneratorRunner))]
    public void TypeInGlobalNamespace_IsNamedWithoutANamespacePrefix(string generator, string suffix)
    {
        var (hintNames, _, diagnostics) = GeneratorRunner.Run(GeneratorRunner.Declare(generator, "Items"));

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
