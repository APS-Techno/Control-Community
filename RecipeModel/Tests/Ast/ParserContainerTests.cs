using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class ParserContainerTests
{
    [Fact]
    public void Loop_With_Body()
    {
        var s = Parser.Parse("loop 10\n  delay 5\nendloop");
        var loop = Assert.IsType<LoopContainer>(Assert.Single(s.Body));
        Assert.Equal("10", loop.Count.Value);
        Assert.Single(loop.Body);
        Assert.IsType<DelayStatement>(loop.Body[0]);
    }

    [Fact]
    public void IfElse_With_Both_Arms()
    {
        var s = Parser.Parse("if pm < 5\n  valve M1 open\nelse\n  valve M1 close\nendif");
        var ie = Assert.IsType<IfElseContainer>(Assert.Single(s.Body));
        Assert.Equal("pm", ie.Condition.Lhs);
        Assert.Equal("<", ie.Condition.Op);
        Assert.Equal("5", ie.Condition.Rhs.Value);
        Assert.Single(ie.Body);
        Assert.NotNull(ie.ElseBody);
        Assert.Single(ie.ElseBody!.Value);
    }

    [Fact]
    public void IfElse_Without_Else()
    {
        var s = Parser.Parse("if pm < 5\n  valve M1 open\nendif");
        var ie = Assert.IsType<IfElseContainer>(Assert.Single(s.Body));
        Assert.Null(ie.ElseBody);
    }

    [Fact]
    public void Function_Definition()
    {
        var s = Parser.Parse("function pumpdown(sec)\n  delay $sec\nendfunction");
        var fn = Assert.IsType<FunctionContainer>(Assert.Single(s.Body));
        Assert.Equal("pumpdown", fn.Name);
        Assert.Single(fn.Parameters);
        Assert.Equal("sec", fn.Parameters[0]);
        Assert.Single(fn.Body);
    }

    [Fact]
    public void BeginEnd_Block()
    {
        var s = Parser.Parse("begin\n  valve M1 open\nend");
        var be = Assert.IsType<BeginEndContainer>(Assert.Single(s.Body));
        Assert.Single(be.Body);
    }

    [Fact]
    public void Nested_Loop_Inside_If()
    {
        var s = Parser.Parse("if pm < 5\n  loop 3\n    delay 1\n  endloop\nendif");
        var ie = Assert.IsType<IfElseContainer>(Assert.Single(s.Body));
        var lp = Assert.IsType<LoopContainer>(Assert.Single(ie.Body));
        Assert.Single(lp.Body);
    }
}
