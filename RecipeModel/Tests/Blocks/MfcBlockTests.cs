using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class MfcBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new MfcStatement("Ar1", NodeField.Literal("5"))
        { Id = NodeId.FromPosition("mfc", "script", 0) };
        var block = new MfcBlock();
        block.BindTo(src);
        var r = Assert.IsType<MfcStatement>(block.ToAst());
        Assert.Equal("Ar1", r.Device);
        Assert.Equal("5", r.Flow.Value);
        Assert.False(r.Flow.IsParamRef);
        Assert.Equal(src.Id, r.Id);
    }
}
