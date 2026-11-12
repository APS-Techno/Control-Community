using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// An unknown power command must be REJECTED LOUDLY. Without a <c>default:</c>
/// case in <c>ProcessPowerCommand</c>, DSL verbs such as <c>["power", true]</c>
/// and <c>["set", 5.0]</c> were accepted, logged nothing, and left the supply
/// de-energised; the dispatcher expects the PANEL names (<c>OutputOn</c>,
/// <c>SetCurrent</c>, ...). This mirrors <c>ApplySetpoint</c>, which throws on an
/// unknown parameter name so that "a recipe typo aborts the run instead of
/// depositing with the setpoint unapplied".
/// </summary>
public class PowerCommandNamesTests
{
    [Theory]
    [InlineData("OutputOn")]
    [InlineData("OutputOff")]
    [InlineData("SetCurrent")]
    [InlineData("SetVoltage")]
    [InlineData("SetPower")]
    [InlineData("SetMode")]
    [InlineData("SetFrequency")]
    [InlineData("SetDutyCycle")]
    [InlineData("SetRampUp")]
    [InlineData("SetRampDown")]
    [InlineData("PulseMode")]
    [InlineData("RampEnabled")]
    [InlineData("SpeedDetect")]
    [InlineData("ClearFault")]
    [InlineData("Reconnect")]
    public void Every_dispatched_command_is_recognised(string command)
    {
        Assert.True(PowerCommands.IsKnown(command),
            $"'{command}' is handled by ProcessPowerCommand but not listed as known — "
            + "the default: branch would reject a command that actually works.");
    }

    [Theory]
    [InlineData("power")]
    [InlineData("set")]
    [InlineData("on")]
    [InlineData("")]
    public void Dsl_verbs_and_junk_are_not_recognised(string command)
    {
        Assert.False(PowerCommands.IsKnown(command));
    }

    [Fact]
    public void Recognition_is_case_insensitive_like_the_rest_of_the_dispatch()
    {
        Assert.True(PowerCommands.IsKnown("outputon"));
        Assert.True(PowerCommands.IsKnown("SETCURRENT"));
    }

    [Fact]
    public void Unknown_message_names_the_offender_and_lists_the_valid_names()
    {
        string msg = PowerCommands.UnknownMessage("PSU-A", "power");

        Assert.Contains("PSU-A", msg);
        Assert.Contains("power", msg);
        Assert.Contains("OutputOn", msg);      // caller can see what to send instead
        Assert.Contains("SetCurrent", msg);
    }
}
