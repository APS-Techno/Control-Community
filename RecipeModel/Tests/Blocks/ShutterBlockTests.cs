using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class ShutterBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new ShutterStatement("S1")
        { Id = NodeId.FromPosition("shutter", "script", 0) };
        var block = new ShutterBlock();
        block.BindTo(src);
        var r = Assert.IsType<ShutterStatement>(block.ToAst());
        Assert.Equal("S1", r.Position);
    }
}
