using APS.SystemControl.Core;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// A time-valued recipe argument may carry a unit suffix (a "sec" parameter, or a
/// hand-authored 30s/5m). The dispatch layer normalizes those args to canonical seconds
/// before the downstream numeric parse — see CoreThread.NormalizeTimeArgs.
/// </summary>
public class TimeArgNormalizeTests
{
    [Theory]
    // deposit <src> <dwell> <end>  — dwell is parts[2]
    [InlineData("deposit Ion 30s 1", 2, "30")]
    [InlineData("deposit BN 5m Home", 2, "300")]
    [InlineData("deposit BN 1.5h Home", 2, "5400")]
    [InlineData("deposit Cu 30 Home", 2, "30")]  // bare seconds round-trip unchanged
    [InlineData("deposit Cu 30.5 Home", 2, "30.5")]
    // bias set <ch> Delay <seconds>  — value is parts[4]
    [InlineData("bias set 1 Delay 0s", 4, "0")]
    [InlineData("bias set 1 Delay 2m", 4, "120")]
    [InlineData("bias set 1 Delay 0", 4, "0")]   // bare round-trip
    // biasArb <ch> <v> <freq> <duty> <delay>  — delay is parts[5]
    [InlineData("biasArb 1 35 10 50 5s", 5, "5")]
    [InlineData("biasArb 1 35 10 50 0", 5, "0")]
    public void Normalizes_time_arg_to_seconds(string line, int idx, string expected)
    {
        var parts = line.Split(' ');
        CoreThread.NormalizeTimeArgs(parts);
        Assert.Equal(expected, parts[idx]);
    }

    [Theory]
    // bias set with a non-time param (Curr/Freq/Duty) is left untouched
    [InlineData("bias set 1 Curr 0.5")]
    [InlineData("bias set 1 Freq 10")]
    [InlineData("bias out 1 Arb 35")]     // 'out', not 'set Delay'
    [InlineData("pwait pm 0.0015 300")]   // unrelated command
    [InlineData("mfc Ar2 40")]
    public void Leaves_non_time_args_untouched(string line)
    {
        var original = line.Split(' ');
        var parts = line.Split(' ');
        CoreThread.NormalizeTimeArgs(parts);
        Assert.Equal(original, parts);
    }

    [Fact]
    public void Short_lines_pass_through()
    {
        var parts = "deposit Cu".Split(' ');
        CoreThread.NormalizeTimeArgs(parts);
        Assert.Equal(new[] { "deposit", "Cu" }, parts);
    }

    [Theory]
    [InlineData("deposit Cu bogus Home")]
    [InlineData("bias set 1 Delay nope")]
    public void Unparseable_time_throws_clear_format_error(string line)
    {
        var parts = line.Split(' ');
        Assert.Throws<System.FormatException>(() => CoreThread.NormalizeTimeArgs(parts));
    }
}
