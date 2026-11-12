using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

/// <summary>
/// `set ID PARAM VALUE` — the PARAM slot is free text in Worker.xml ('s'), so
/// a wrong name (`set DC_A Freq 40` on a supply that only knows `Frequency`)
/// would pass validation and fail at runtime, pausing a chamber run. When the
/// catalogue carries the driver's own list of setpoint names for a device, the
/// validator checks the name against it; a device with no list stays unchecked.
/// </summary>
public class SetParameterValidationTests
{
    private static RecipeValidator Make(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? setpointNames)
    {
        var ops = new List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)>
        {
            ("set", null, ["Power", "s", "f"]),
        };
        var devices = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Power"] = new[] { "DC_A", "PSU_B", "BIAS_1" },
        };
        var semantics = RecipeSemantics.Build(ops, devices, setpointNames);
        return new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: semantics.AllDeviceNames,
            semantics: semantics);
    }

    private static readonly Dictionary<string, IReadOnlyCollection<string>> Names = new()
    {
        ["DC_A"] = new[] { "Voltage", "Current", "Power", "Frequency", "Freq", "Pulse" },
        ["BIAS_1"] = new[] { "Voltage", "Current", "Mode" },
    };

    [Fact]
    public void Known_parameter_name_is_accepted()
    {
        Assert.Empty(Make(Names).Validate("set DC_A Frequency 40"));
    }

    [Fact]
    public void Driver_alias_is_accepted_when_the_driver_lists_it()
    {
        Assert.Empty(Make(Names).Validate("set DC_A Freq 40"));
    }

    [Fact]
    public void Unknown_parameter_name_is_an_error_listing_the_valid_names()
    {
        var errors = Make(Names).Validate("set DC_A PulseWidth 40");

        var e = Assert.Single(errors);
        Assert.Equal(RecipeErrorSeverity.Error, e.Severity);
        Assert.Contains("PulseWidth", e.Message);
        Assert.Contains("DC_A", e.Message);
        Assert.Contains("Frequency", e.Message);
        Assert.Contains("Pulse", e.Message);
    }

    [Fact]
    public void Parameter_names_are_case_sensitive_like_the_drivers()
    {
        // Every driver switches on the exact string; `frequency` reaches the
        // default branch and throws at runtime, so it must fail here too.
        var errors = Make(Names).Validate("set DC_A frequency 40");
        Assert.Contains(errors, e => e.Message.Contains("frequency"));
    }

    [Fact]
    public void Device_without_a_declared_list_is_not_checked()
    {
        Assert.Empty(Make(Names).Validate("set PSU_B Anything 1"));
    }

    [Fact]
    public void No_lists_at_all_keeps_the_old_behaviour()
    {
        Assert.Empty(Make(null).Validate("set DC_A Anything 1"));
    }

    [Fact]
    public void Variable_parameter_name_is_not_checked_statically()
    {
        var errors = Make(Names).Validate("var p = Frequency\nset DC_A $p 40");
        Assert.DoesNotContain(errors, e => e.Message.Contains("unknown"));
    }

    [Fact]
    public void Unknown_device_reports_only_the_device_error()
    {
        var errors = Make(Names).Validate("set DC_Z Frequency 40");
        Assert.Single(errors);
        Assert.Contains("DC_Z", errors[0].Message);
    }
}
