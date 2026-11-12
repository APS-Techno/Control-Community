using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RotationBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_With_Speed()
    {
        var src = new RotationStatement(OnOff.On, NodeField.Literal("30"))
        { Id = NodeId.FromPosition("rotation", "script", 0) };
        var block = new RotationBlock();
        block.BindTo(src);
        var r = Assert.IsType<RotationStatement>(block.ToAst());
        Assert.Equal(OnOff.On, r.Action);
        Assert.NotNull(r.Speed);
        Assert.Equal("30", r.Speed!.Value);
    }

    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_Without_Speed()
    {
        var src = new RotationStatement(OnOff.Off, null)
        { Id = NodeId.FromPosition("rotation", "script", 0) };
        var block = new RotationBlock();
        block.BindTo(src);
        var r = Assert.IsType<RotationStatement>(block.ToAst());
        Assert.Equal(OnOff.Off, r.Action);
        Assert.Null(r.Speed);
    }
}
