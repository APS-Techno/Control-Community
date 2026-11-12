using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class GotoBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new GotoStatement("top")
        { Id = NodeId.FromPosition("goto", "script", 0) };
        var block = new GotoBlock();
        block.BindTo(src);
        var r = Assert.IsType<GotoStatement>(block.ToAst());
        Assert.Equal("top", r.Target);
    }
}
