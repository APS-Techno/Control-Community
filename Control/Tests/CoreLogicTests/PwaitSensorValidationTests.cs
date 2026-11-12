using APS.RecipeModel.Validation;

namespace CoreLogicTests;

/// <summary>
/// Locks in the pwait-sensor rule: pwait drives the GAUGE_A throttle-valve PID
/// and ignores the named sensor for the convergence check, so using it on a
/// sensor without a throttle valve (e.g. loadlock pl, roughed by SP2) yields
/// nonsense: "pwait pl 50mT 60" exits on whatever the main-chamber pm sensor
/// reads. swait is the correct command.
/// </summary>
public class PwaitSensorValidationTests
{
    private static RecipeValidator V() => new(
        RecipeValidator.AllKnownCommands,
        knownDevices: ["pm", "pl"],
        throttleValveSensor: "pm");

    [Theory]
    [InlineData("pwait pl 50mT 60")]
    [InlineData("pwait pl 0.05 60")]
    public void Pwait_on_non_throttle_sensor_is_error(string script)
    {
        var findings = V().Validate(script);
        var errors = findings.Where(f => f.Severity == RecipeErrorSeverity.Error).ToList();
        Assert.Single(errors);
        Assert.Contains("pl", errors[0].Message);
        Assert.Contains("swait", errors[0].Message);
        Assert.Contains("'pm'", errors[0].Message);
    }

    [Theory]
    [InlineData("pwait pm 50mT 60")]
    [InlineData("pwait pm 0 1")]        // open-throttle-valve idiom
    public void Pwait_on_throttle_sensor_is_allowed(string script)
    {
        var findings = V().Validate(script);
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Error);
    }

    [Theory]
    [InlineData("swait pl 50mT 60")]
    [InlineData("swait pm 1uT 1800")]
    public void Swait_is_unrestricted(string script)
    {
        var findings = V().Validate(script);
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Error);
    }

    [Fact]
    public void No_throttle_sensor_configured_disables_rule()
    {
        // When the caller doesn't supply throttleValveSensor (e.g. systems
        // without a controlled throttle valve), pwait is unrestricted —
        // back-compat for existing callers and tests.
        var v = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: ["pm", "pl"]);
        var findings = v.Validate("pwait pl 50mT 60");
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Error);
    }
}
