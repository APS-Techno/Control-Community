using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class StageBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new StageStatement("VS", NodeField.Literal("-10"))
        { Id = NodeId.FromPosition("stage", "script", 0) };
        var block = new StageBlock();
        block.BindTo(src);
        var r = Assert.IsType<StageStatement>(block.ToAst());
        Assert.Equal("VS", r.Device);
        Assert.Equal("-10", r.Position.Value);
    }
}
