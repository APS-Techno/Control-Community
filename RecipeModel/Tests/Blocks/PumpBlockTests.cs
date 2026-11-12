using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class PumpBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new PumpStatement("TP1", OnOff.On)
        { Id = NodeId.FromPosition("pump", "script", 0) };
        var block = new PumpBlock();
        block.BindTo(src);
        var r = Assert.IsType<PumpStatement>(block.ToAst());
        Assert.Equal("TP1", r.Device);
        Assert.Equal(OnOff.On, r.Action);
    }
}
