using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class WarmupBlockTests
{
    private static WarmupStatement Make(string time, params string[] sources)
        => new(NodeField.Literal(time), sources.ToImmutableArray())
        { Id = NodeId.FromPosition("warmup", "script", 0) };

    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = Make("600", "SRC1", "SRC2");
        var block = new WarmupBlock();
        block.BindTo(src);
        var r = Assert.IsType<WarmupStatement>(block.ToAst());
        Assert.Equal("600", r.Time.Value);
        Assert.Equal(new[] { "SRC1", "SRC2" }, r.Sources.ToArray());
    }

    [StaFact]
    public void ApplyContext_Populates_Source_Picker_And_Time_Param_List()
    {
        var block = new WarmupBlock();
        block.BindTo(Make("60", "Si"));
        block.ApplyContext(new BlockContext
        {
            SourceNames = new[] { "Si", "BN", "Ion" },
            ParameterNames = new[] { "warmTime" },
        });

        block.ToggleSourceForTest("BN", true);
        var r = Assert.IsType<WarmupStatement>(block.ToAst());
        Assert.Contains("Si", r.Sources);
        Assert.Contains("BN", r.Sources);
    }

    [StaFact]
    public void ApplyContext_Null_Allows_Free_Text_Edit_Of_Source_List()
    {
        var block = new WarmupBlock();
        block.BindTo(Make("30"));
        block.ApplyContext(null);
        block.SetSourcesByTextForTest("CustomSrc1, CustomSrc2");
        var r = Assert.IsType<WarmupStatement>(block.ToAst());
        Assert.Equal(new[] { "CustomSrc1", "CustomSrc2" }, r.Sources.ToArray());
    }
}
