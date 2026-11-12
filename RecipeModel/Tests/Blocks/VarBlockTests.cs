using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class VarBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new VarStatement("flow", NodeField.Literal("5"))
        { Id = NodeId.FromPosition("var", "script", 0) };
        var block = new VarBlock();
        block.BindTo(src);
        var r = Assert.IsType<VarStatement>(block.ToAst());
        Assert.Equal("flow", r.Name);
        Assert.Equal("5", r.Value.Value);
    }
}
