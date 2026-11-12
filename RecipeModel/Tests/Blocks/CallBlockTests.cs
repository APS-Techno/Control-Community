using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class CallBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new CallStatement("pumpdown")
        { Id = NodeId.FromPosition("call", "script", 0) };
        var block = new CallBlock();
        block.BindTo(src);
        var r = Assert.IsType<CallStatement>(block.ToAst());
        Assert.Equal("pumpdown", r.RecipeName);
    }
}
