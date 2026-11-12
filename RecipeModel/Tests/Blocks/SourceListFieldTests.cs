using System.Collections.Immutable;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class SourceListFieldTests
{
    [StaFact]
    public void Value_Roundtrips_When_No_Catalog_Set()
    {
        var f = new SourceListField();
        f.Value = ImmutableArray.Create("Si", "BN");
        Assert.Equal(new[] { "Si", "BN" }, f.Value.ToArray());
    }

    [StaFact]
    public void DisplayText_Is_Comma_Joined()
    {
        var f = new SourceListField();
        f.Value = ImmutableArray.Create("Si", "BN", "Ion");
        Assert.Equal("Si, BN, Ion", f.DisplayTextForTest);
    }

    [StaFact]
    public void SetAvailableSources_Then_Toggle_Adds_To_Value()
    {
        var f = new SourceListField();
        f.SetAvailableSources(new[] { "Si", "BN", "Ion" });
        f.SetCheckedForTest("BN", true);
        f.SetCheckedForTest("Ion", true);
        Assert.Equal(new[] { "BN", "Ion" }, f.Value.ToArray());
    }

    [StaFact]
    public void SetAvailableSources_Preserves_Existing_Custom_Items()
    {
        var f = new SourceListField();
        f.Value = ImmutableArray.Create("Custom1");
        f.SetAvailableSources(new[] { "Si", "BN" });
        Assert.Contains("Custom1", f.Value);
    }
}
