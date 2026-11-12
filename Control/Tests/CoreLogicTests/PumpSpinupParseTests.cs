using APS.SystemControl.Core;
using Xunit;

namespace CoreLogicTests;

public class PumpSpinupParseTests
{
    [Theory]
    [InlineData("pump SP1 on 30",   "SP1", 30f)]
    [InlineData("pump SP1 true 30", "SP1", 30f)] // post-substitution form
    [InlineData("pump TP on 0",     "TP",  0f)]
    [InlineData("PUMP SP1 ON 30",   "SP1", 30f)] // case-insensitive
    [InlineData("pump SP1 on 2m",   "SP1", 120f)] // minutes suffix
    [InlineData("pump SP1 on 1h",   "SP1", 3600f)] // hours suffix
    [InlineData("pump SP1 on 30s",  "SP1", 30f)]  // explicit seconds
    public void Parses_spinup_form(string line, string expId, float expSecs)
    {
        bool ok = CoreThread.TryParsePumpSpinup(line.Split(' '), out var id, out var secs);
        Assert.True(ok);
        Assert.Equal(expId, id);
        Assert.Equal(expSecs, secs);
    }

    [Theory]
    [InlineData("pump SP1 on")]      // plain form — no seconds
    [InlineData("pump SP1 off 30")]  // off never spins up
    [InlineData("pump SP1 on fast")] // non-numeric 4th token
    [InlineData("valve M1 open 5")]  // not a pump
    [InlineData("pump SP1 on 30 x")] // too many tokens
    public void Rejects_non_spinup_forms(string line)
    {
        Assert.False(CoreThread.TryParsePumpSpinup(line.Split(' '), out _, out _));
    }
}
