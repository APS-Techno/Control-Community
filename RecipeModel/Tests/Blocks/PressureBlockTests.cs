using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class PressureBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new PressureStatement("pm", NodeField.Literal("5"), NodeField.Literal("60"))
        { Id = NodeId.FromPosition("pressure", "script", 0) };
        var block = new PressureBlock();
        block.BindTo(src);
        var r = Assert.IsType<PressureStatement>(block.ToAst());
        Assert.Equal("pm", r.Sensor);
        Assert.Equal("5", r.TargetMTorr.Value);
        Assert.Equal("60", r.TimeoutSec.Value);
    }
}
