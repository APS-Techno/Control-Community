using APS.RecipeModel.Validation;

namespace CoreLogicTests;

/// <summary>
/// Locks in the warning for <c>power ID on|off VALUE</c>. The worker op is 2-arg,
/// so the runtime silently discards the trailing level and energizes the supply at
/// whatever the previous <c>set</c> left — usually 0 W. The recipe reads as "turn on
/// at 350 W" and deposits nothing, which is the single most expensive authoring trap
/// in a sputter recipe. Only <c>set ID Power 350</c> actually moves the setpoint.
/// </summary>
public class PowerTrailingValueValidationTests
{
    private static RecipeValidator V() => new(
        RecipeValidator.AllKnownCommands,
        knownDevices: ["RF301", "PSU_A"]);

    [Theory]
    [InlineData("power RF301 on 350")]
    [InlineData("power RF301 off 0")]
    [InlineData("power PSU_A on $Level")]
    public void Trailing_value_emits_warning(string script)
    {
        var findings = V().Validate(script);
        var warnings = findings.Where(f => f.Severity == RecipeErrorSeverity.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("IGNORED", warnings[0].Message);
        Assert.Contains("set ", warnings[0].Message);
    }

    [Fact]
    public void Warning_names_the_supply_and_the_dropped_value()
    {
        var w = V().Validate("power RF301 on 350")
                   .Single(f => f.Severity == RecipeErrorSeverity.Warning);
        Assert.Contains("'350'", w.Message);
        Assert.Contains("set RF301 Power 350", w.Message);
    }

    [Theory]
    [InlineData("power RF301 on")]
    [InlineData("power RF301 off")]
    [InlineData("power RF301 on   # energize after the set above")]
    public void Two_argument_form_is_clean(string script)
    {
        var findings = V().Validate(script);
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Warning);
    }

    [Fact]
    public void Correct_idiom_validates_clean()
    {
        var findings = V().Validate("set RF301 Power 350\npower RF301 on");
        Assert.Empty(findings);
    }
}
