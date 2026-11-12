using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class DelayBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_Without_Caption()
    {
        var src = new DelayStatement(NodeField.Literal("5"), null)
        { Id = NodeId.FromPosition("delay", "script", 0) };
        var block = new DelayBlock();
        block.BindTo(src);
        var r = Assert.IsType<DelayStatement>(block.ToAst());
        Assert.Equal("5", r.Seconds.Value);
        Assert.Null(r.Caption);
    }

    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_With_Caption()
    {
        var src = new DelayStatement(NodeField.Literal("5"), "pumping")
        { Id = NodeId.FromPosition("delay", "script", 0) };
        var block = new DelayBlock();
        block.BindTo(src);
        var r = Assert.IsType<DelayStatement>(block.ToAst());
        Assert.Equal("pumping", r.Caption);
    }
}
