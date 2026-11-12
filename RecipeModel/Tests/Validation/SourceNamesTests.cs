using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class SourceNamesTests
{
    [Theory]
    [InlineData("W_1", "W", "W_1")]
    [InlineData("", "W", "W")]
    [InlineData(null, "Si", "Si")]
    [InlineData("  ", "Si", "Si")]   // whitespace name -> fall back to material
    [InlineData("  N ", "W", "N")]   // trimmed
    public void Effective_PrefersNameThenMaterial(string? name, string? material, string expected)
        => Assert.Equal(expected, SourceNames.Effective(name, material));

    [Fact]
    public void Duplicates_AreCaseInsensitive_Distinct_FirstSeenOrder()
    {
        var dups = SourceNames.Duplicates(new[] { "W", "Si", "w", "Si", "Mo" });
        Assert.Equal(new[] { "W", "Si" }, dups);
    }

    [Fact]
    public void Resolvable_HalfNamed_ExcludesAmbiguousMaterial_KeepsUniqueName()
    {
        // W_1 (named) and a nameless W share material "W" -> "W" is ambiguous
        // (2 sources), but "W_1" is a distinct effective name -> stays offered.
        var names = SourceNames.Resolvable(new (string?, string?)[]
        {
            ("W_1", "W"),
            (null, "W"),
        });
        Assert.Equal(new[] { "W_1" }, names);
        Assert.DoesNotContain("W", names);
    }

    [Fact]
    public void Resolvable_FullyNamelessDuplicateMaterial_ExcludesBoth()
    {
        var names = SourceNames.Resolvable(new (string?, string?)[]
        {
            (null, "W"),
            (null, "W"),
        });
        Assert.Empty(names);
    }

    [Fact]
    public void Resolvable_UniqueMaterials_ReturnsAllInDocumentOrder()
    {
        var names = SourceNames.Resolvable(new (string?, string?)[]
        {
            (null, "Si"),
            ("W_1", "W"),
            (null, "BN"),
        });
        Assert.Equal(new[] { "Si", "W_1", "BN" }, names);
    }
}
