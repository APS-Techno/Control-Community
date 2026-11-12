using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class WaitBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new WaitStatement(NodeField.Literal("3"), null)
        { Id = NodeId.FromPosition("wait", "script", 0) };
        var block = new WaitBlock();
        block.BindTo(src);
        var r = Assert.IsType<WaitStatement>(block.ToAst());
        Assert.Equal("3", r.Seconds.Value);
    }
}
