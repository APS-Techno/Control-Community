using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class SwaitBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new SwaitStatement("pl", NodeField.Literal("50mT"), NodeField.Literal("300"))
        { Id = NodeId.FromPosition("swait", "script", 0) };
        var block = new SwaitBlock();
        block.BindTo(src);
        var r = Assert.IsType<SwaitStatement>(block.ToAst());
        Assert.Equal("pl", r.Sensor);
        Assert.Equal("50mT", r.Target.Value);
        Assert.Equal("300", r.TimeoutSec.Value);
    }
}
