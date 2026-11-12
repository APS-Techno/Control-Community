using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class CheckBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new CheckStatement("pm", CompareOp.Lt, NodeField.Literal("10"))
        { Id = NodeId.FromPosition("check", "script", 0) };
        var block = new CheckBlock();
        block.BindTo(src);
        var r = Assert.IsType<CheckStatement>(block.ToAst());
        Assert.Equal("pm", r.Sensor);
        Assert.Equal(CompareOp.Lt, r.Op);
        Assert.Equal("10", r.Value.Value);
    }
}
