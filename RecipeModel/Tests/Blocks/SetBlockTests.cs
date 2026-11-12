using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class SetBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new SetStatement("PS1", "mode", NodeField.Literal("dc"))
        { Id = NodeId.FromPosition("set", "script", 0) };
        var block = new SetBlock();
        block.BindTo(src);
        var r = Assert.IsType<SetStatement>(block.ToAst());
        Assert.Equal("PS1", r.Device);
        Assert.Equal("mode", r.Property);
        Assert.Equal("dc", r.Value.Value);
    }
}
