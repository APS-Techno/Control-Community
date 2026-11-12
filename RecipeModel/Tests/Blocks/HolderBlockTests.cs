using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class HolderBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new HolderStatement("2")
        { Id = NodeId.FromPosition("holder", "script", 0) };
        var block = new HolderBlock();
        block.BindTo(src);
        var r = Assert.IsType<HolderStatement>(block.ToAst());
        Assert.Equal("2", r.Position);
    }
}
