using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class BarePressureNoteTests
{
    private const string Marker = "interpreted as Torr";

    [Fact]
    public void Bare_value_above_the_chamber_processing_ceiling_gets_the_epistemic_note()
    {
        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Bound(), "pwait 2.00 60\n", Marker));

        Assert.Contains("'2.00'", w.Message);
        Assert.Contains("2000 mTorr", w.Message);
        Assert.Contains("0.2 Torr Processing alarm threshold", w.Message);
        Assert.Contains("'2.00mT'", w.Message);
        Assert.Contains("cannot be determined without running the recipe", w.Message);
        Assert.Contains("'Main'", w.Message);
    }

    [Fact]
    public void Unbound_pwait_resolves_the_chamber_from_the_sensor()
    {
        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Unbound(), "pwait C1_pm 2.00 60\n", Marker));

        Assert.Contains("0.2 Torr Processing alarm threshold", w.Message);
        Assert.Contains("'C1'", w.Message);
    }

    [Fact]
    public void Bare_value_below_the_ceiling_keeps_only_the_original_text()
    {
        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Bound(), "pwait 0.05 60\n", Marker));

        Assert.Contains("= 50 mTorr", w.Message);
        Assert.DoesNotContain("Processing alarm", w.Message);
    }

    [Fact]
    public void Chamber_without_a_processing_rule_keeps_the_original_text()
    {
        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Unbound(), "pwait TM_pm 2.00 60\n", Marker));

        Assert.DoesNotContain("Processing alarm", w.Message);
        Assert.Contains("Add a unit suffix to disambiguate", w.Message);
    }

    [Fact]
    public void Unknown_chamber_keeps_the_original_text()
    {
        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Unbound(), "pwait LL_pm 2.00 60\n", Marker));

        Assert.DoesNotContain("Processing alarm", w.Message);
    }

    [Fact]
    public void Validator_without_chamber_facts_keeps_the_original_text()
    {
        var v = new APS.RecipeModel.Validation.RecipeValidator(
            APS.RecipeModel.Validation.RecipeValidator.AllKnownCommands, new[] { "pm" });

        var found = ChamberFixture.Warnings(v, "pwait 2.00 60\n", Marker);

        Assert.All(found, w => Assert.DoesNotContain("Processing alarm", w.Message));
    }

    [Fact]
    public void Suffixed_value_is_not_noted()
    {
        Assert.Empty(ChamberFixture.Warnings(ChamberFixture.Bound(), "pwait 2.00mT 60\n", Marker));
    }
}
