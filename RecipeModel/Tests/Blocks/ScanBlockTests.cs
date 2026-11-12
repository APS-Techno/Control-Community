using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class ScanBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new ScanStatement("SC1", NodeField.Literal("0"), NodeField.Literal("100"), NodeField.Literal("1.0"))
        { Id = NodeId.FromPosition("scan", "script", 0) };
        var block = new ScanBlock();
        block.BindTo(src);
        var r = Assert.IsType<ScanStatement>(block.ToAst());
        Assert.Equal("SC1", r.Device);
        Assert.Equal("0", r.Start.Value);
        Assert.Equal("100", r.End.Value);
        Assert.Equal("1.0", r.Speed.Value);
    }
}
