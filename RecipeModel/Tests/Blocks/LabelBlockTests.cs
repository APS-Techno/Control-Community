using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class LabelBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new LabelStatement("top")
        { Id = NodeId.FromPosition("label", "script", 0) };
        var block = new LabelBlock();
        block.BindTo(src);
        var r = Assert.IsType<LabelStatement>(block.ToAst());
        Assert.Equal("top", r.Name);
    }
}
