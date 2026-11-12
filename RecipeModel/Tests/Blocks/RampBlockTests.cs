using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RampBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new RampStatement("PS1", NodeField.Literal("500"))
        { Id = NodeId.FromPosition("ramp", "script", 0) };
        var block = new RampBlock();
        block.BindTo(src);
        var r = Assert.IsType<RampStatement>(block.ToAst());
        Assert.Equal("PS1", r.Device);
        Assert.Equal("500", r.Value.Value);
    }
}
