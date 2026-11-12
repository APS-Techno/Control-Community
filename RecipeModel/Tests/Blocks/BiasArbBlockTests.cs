using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BiasArbBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new BiasArbStatement("CH1",
            NodeField.Literal("250"), NodeField.Literal("1000"),
            NodeField.Literal("50"), NodeField.Literal("0"))
        { Id = NodeId.FromPosition("biasArb", "script", 0) };
        var block = new BiasArbBlock();
        block.BindTo(src);
        var r = Assert.IsType<BiasArbStatement>(block.ToAst());
        Assert.Equal("CH1", r.Channel);
        Assert.Equal("250", r.Voltage.Value);
        Assert.Equal("1000", r.Frequency.Value);
        Assert.Equal("50", r.DutyCycle.Value);
        Assert.Equal("0", r.Phase.Value);
    }
}
