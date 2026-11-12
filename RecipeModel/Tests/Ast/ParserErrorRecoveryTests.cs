using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class ParserErrorRecoveryTests
{
    [Fact]
    public void Garbage_Line_Between_Valid_Lines_Yields_Invalid_In_Middle()
    {
        var s = Parser.Parse("valve M1 open\n@@@ not a command\nmfc Ar1 5");
        Assert.Equal(3, s.Body.Length);
        Assert.IsType<ValveStatement>(s.Body[0]);
        Assert.IsType<InvalidStatement>(s.Body[1]);
        Assert.IsType<MfcStatement>(s.Body[2]);
    }

    [Fact]
    public void Script_Markers_Are_Flagged_On_Script_Container_And_Not_Emitted_As_Nodes()
    {
        var s = Parser.Parse("start\nvalve M1 open\nfinish");
        Assert.True(s.HasStartMarker);
        Assert.True(s.HasFinishMarker);
        Assert.Single(s.Body); // valve only
    }

    [Fact]
    public void Unclosed_Loop_Is_Still_Parsed_With_Body_Until_EOF()
    {
        var s = Parser.Parse("loop 10\n  delay 1");
        var lp = Assert.IsType<LoopContainer>(Assert.Single(s.Body));
        Assert.Single(lp.Body);
    }
}
