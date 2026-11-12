using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class PowerBlockTests
{
    // A legacy `power PS1 on 500` line keeps its trailing value across a bind/emit
    // cycle: the runtime ignores that value, but silently rewriting the operator's
    // file when they touch an unrelated field would be worse than leaving it.
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new PowerStatement("PS1", OnOff.On, NodeField.Literal("500"))
        { Id = NodeId.FromPosition("power", "script", 0) };
        var block = new PowerBlock();
        block.BindTo(src);
        var r = Assert.IsType<PowerStatement>(block.ToAst());
        Assert.Equal("PS1", r.Device);
        Assert.Equal(OnOff.On, r.Action);
        Assert.Equal("500", r.Value.Value);
    }

    // The block has no value editor at all, so a block dropped fresh from the palette
    // cannot author `power ID on <level>` — the trap the runtime silently swallows.
    [StaFact]
    public void Fresh_block_emits_no_trailing_value()
    {
        var r = Assert.IsType<PowerStatement>(new PowerBlock().ToAst());
        Assert.Equal("", r.Value.Value);
    }

    // ...and the printer must not leave a dangling separator for that empty field.
    [Fact]
    public void Two_token_power_line_round_trips_without_trailing_space()
    {
        Assert.Equal("power PS1 on\n", PrettyPrinter.Print(Parser.Parse("power PS1 on")));
        Assert.Equal("power PS1 off\n", PrettyPrinter.Print(Parser.Parse("power PS1 off")));
        // A legacy line with a level survives verbatim.
        Assert.Equal("power PS1 on 500\n", PrettyPrinter.Print(Parser.Parse("power PS1 on 500")));
    }
}
