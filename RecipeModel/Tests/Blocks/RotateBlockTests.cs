using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RotateBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new RotateStatement(NodeField.Literal("30"))
        { Id = NodeId.FromPosition("rotate", "script", 0) };
        var block = new RotateBlock();
        block.BindTo(src);
        var r = Assert.IsType<RotateStatement>(block.ToAst());
        Assert.Equal("30", r.Speed.Value);
    }
}
