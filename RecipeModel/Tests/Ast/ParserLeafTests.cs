using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class ParserLeafTests
{
    private static ScriptContainer Parse(string text) => Parser.Parse(text);

    [Fact]
    public void Empty_Input_Produces_Empty_Script()
    {
        var s = Parse("");
        Assert.Empty(s.Body);
    }

    [Theory]
    [InlineData("valve M1 open", "M1", ValveAction.Open)]
    [InlineData("valve HV close", "HV", ValveAction.Close)]
    public void Valve_Statement(string line, string device, ValveAction action)
    {
        var s = Parse(line);
        var stmt = Assert.IsType<ValveStatement>(Assert.Single(s.Body));
        Assert.Equal(device, stmt.Device);
        Assert.Equal(action, stmt.Action);
    }

    [Fact]
    public void Mfc_With_Literal_Flow()
    {
        var stmt = Assert.IsType<MfcStatement>(Assert.Single(Parse("mfc Ar1 5").Body));
        Assert.Equal("Ar1", stmt.Device);
        Assert.False(stmt.Flow.IsParamRef);
        Assert.Equal("5", stmt.Flow.Value);
    }

    [Fact]
    public void Mfc_With_Param_Flow()
    {
        var stmt = Assert.IsType<MfcStatement>(Assert.Single(Parse("mfc Ar1 $flow").Body));
        Assert.True(stmt.Flow.IsParamRef);
        Assert.Equal("flow", stmt.Flow.Value);
    }

    [Fact]
    public void Pwait_Parses_Three_Args_In_mTorr()
    {
        var stmt = Assert.IsType<PressureStatement>(Assert.Single(Parse("pwait pm 5 60").Body));
        Assert.Equal("pm", stmt.Sensor);
        Assert.Equal("5", stmt.TargetMTorr.Value);
        Assert.Equal("60", stmt.TimeoutSec.Value);
    }

    [Fact]
    public void Swait_Parses_Three_Args()
    {
        var stmt = Assert.IsType<SwaitStatement>(Assert.Single(Parse("swait pl 50mT 300").Body));
        Assert.Equal("pl", stmt.Sensor);
        Assert.Equal("50mT", stmt.Target.Value);
        Assert.Equal("300", stmt.TimeoutSec.Value);
    }

    [Fact]
    public void Delay_With_Caption()
    {
        var stmt = Assert.IsType<DelayStatement>(Assert.Single(Parse("delay 5 \"pumping\"").Body));
        Assert.Equal("5", stmt.Seconds.Value);
        Assert.Equal("pumping", stmt.Caption);
    }

    [Fact]
    public void Unknown_Line_Becomes_InvalidStatement()
    {
        var stmt = Assert.IsType<InvalidStatement>(Assert.Single(Parse("xyzzy foo bar").Body));
        Assert.Equal("xyzzy foo bar", stmt.OriginalText);
    }

    [Fact]
    public void Deposit_With_Literals()
    {
        var stmt = Assert.IsType<DepositStatement>(Assert.Single(Parse("deposit Ion CleaningTime 1").Body));
        Assert.Equal("Ion", stmt.Position.Value);
        Assert.Equal("CleaningTime", stmt.Time.Value);
        Assert.Equal("1", stmt.Retract.Value);
        Assert.False(stmt.Position.IsParamRef);
        Assert.False(stmt.Time.IsParamRef);
        Assert.False(stmt.Retract.IsParamRef);
    }

    [Fact]
    public void Deposit_With_Param_Time_And_Home_Retract()
    {
        var stmt = Assert.IsType<DepositStatement>(Assert.Single(Parse("deposit BN $timeBN Home").Body));
        Assert.Equal("BN", stmt.Position.Value);
        Assert.Equal("timeBN", stmt.Time.Value);
        Assert.True(stmt.Time.IsParamRef);
        Assert.Equal("Home", stmt.Retract.Value);
    }

    [Fact]
    public void Warmup_With_Multiple_Sources()
    {
        var stmt = Assert.IsType<WarmupStatement>(Assert.Single(Parse("warmup 60 Si BN").Body));
        Assert.Equal("60", stmt.Time.Value);
        Assert.Equal(new[] { "Si", "BN" }, stmt.Sources.ToArray());
    }

    [Fact]
    public void Warmup_With_Param_Time_And_Single_Source()
    {
        var stmt = Assert.IsType<WarmupStatement>(Assert.Single(Parse("warmup $warmTime Ion").Body));
        Assert.Equal("warmTime", stmt.Time.Value);
        Assert.True(stmt.Time.IsParamRef);
        Assert.Equal(new[] { "Ion" }, stmt.Sources.ToArray());
    }

    [Fact]
    public void Warmup_With_No_Sources()
    {
        var stmt = Assert.IsType<WarmupStatement>(Assert.Single(Parse("warmup 30").Body));
        Assert.Equal("30", stmt.Time.Value);
        Assert.Empty(stmt.Sources);
    }

    [Fact]
    public void Warmup_Time_Keeps_Unit_Suffix()
    {
        // warmup's time uses the same s/m/h convention as wait/delay; the
        // suffixed literal must fold into a single token and survive parsing.
        var stmt = Assert.IsType<WarmupStatement>(Assert.Single(Parse("warmup 2m Si").Body));
        Assert.Equal("2m", stmt.Time.Value);
        Assert.False(stmt.Time.IsParamRef);
        Assert.Equal(new[] { "Si" }, stmt.Sources.ToArray());
    }
}
