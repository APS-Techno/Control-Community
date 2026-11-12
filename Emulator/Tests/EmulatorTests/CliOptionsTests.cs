using APS.SystemEmulator.Setup;

namespace EmulatorTests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsEmpty()
    {
        var o = CliOptions.Parse([]);
        Assert.Null(o.Project);
        Assert.Null(o.StateFile);
    }

    [Fact]
    public void Parse_ProjectAndState_ReadsBoth()
    {
        var o = CliOptions.Parse(["--project", "DemoProfile", "--state", @"Snapshots\All_Pumped.emstate"]);
        Assert.Equal("DemoProfile", o.Project);
        Assert.Equal(@"Snapshots\All_Pumped.emstate", o.StateFile);
    }

    [Fact]
    public void Parse_FlagWithoutValue_IsIgnored()
    {
        var o = CliOptions.Parse(["--project"]);
        Assert.Null(o.Project);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnFlagNames()
    {
        var o = CliOptions.Parse(["--PROJECT", "DemoProfile"]);
        Assert.Equal("DemoProfile", o.Project);
    }

    [Fact]
    public void Parse_UnknownFlags_AreIgnored()
    {
        var o = CliOptions.Parse(["--nonsense", "x", "--state", "s.emstate"]);
        Assert.Equal("s.emstate", o.StateFile);
    }
}
