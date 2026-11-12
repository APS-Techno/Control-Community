using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class HomeAbortBlockTests
{
    [StaFact]
    public void HomeBlock_RoundTrips()
    {
        var src = new HomeStatement { Id = NodeId.FromPosition("home", "script", 0) };
        var block = new HomeBlock();
        block.BindTo(src);
        Assert.IsType<HomeStatement>(block.ToAst());
    }

    [StaFact]
    public void AbortBlock_RoundTrips()
    {
        var src = new AbortStatement { Id = NodeId.FromPosition("abort", "script", 0) };
        var block = new AbortBlock();
        block.BindTo(src);
        Assert.IsType<AbortStatement>(block.ToAst());
    }
}
