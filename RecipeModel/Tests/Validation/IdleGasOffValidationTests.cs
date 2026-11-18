using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class IdleGasOffValidationTests
{
    private const string Marker = "Idle ceiling";

    [Fact]
    public void Bound_pwait_gas_off_with_throttle_held_warns_with_delay_window_and_release_form()
    {
        const string recipe = "mfc Ar1 10\nmfc Ar2 5\npwait 2.00mT 60\nmfc Ar1 0\nmfc Ar2 0\n";

        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Bound(), recipe, Marker));

        Assert.Equal(5, w.Line);
        Assert.Contains("'2.00mT'", w.Message);
        Assert.Contains("for 60 s", w.Message);
        Assert.Contains("0.0001 Torr", w.Message);
        Assert.Contains("'pwait 0 1'", w.Message);
        Assert.Contains("'Main'", w.Message);
    }

    [Fact]
    public void Quotes_the_held_literal_that_the_recipe_wrote()
    {
        var w = Assert.Single(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "mfc Ar1 10\npwait 3mT 60\nmfc Ar1 0\n", Marker));

        Assert.Contains("'3mT'", w.Message);
        Assert.DoesNotContain("2.00mT", w.Message);
    }

    [Fact]
    public void Gas_still_flowing_in_the_chamber_does_not_warn()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "mfc Ar1 10\nmfc Ar2 5\npwait 2mT 60\nmfc Ar1 0\n", Marker));
    }

    [Fact]
    public void No_pwait_before_gas_off_does_not_warn()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "mfc Ar1 10\nmfc Ar1 0\n", Marker));
    }

    [Fact]
    public void Bound_park_before_gas_off_does_not_warn()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "mfc Ar1 10\npwait 2mT 60\npwait 0 1\nmfc Ar1 0\n", Marker));
    }

    [Fact]
    public void Unbound_park_before_gas_off_does_not_warn()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Unbound(), "mfc C1_Ar 10\npwait C1_pm 3mT 300\npwait C1_pm 0 1\nmfc C1_Ar 0\n", Marker));
    }

    [Fact]
    public void Isolate_releases_the_held_target()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "mfc Ar1 10\npwait 2mT 60\nisolate\nmfc Ar1 0\n", Marker));
    }

    [Fact]
    public void Unbound_pwait_names_the_sensor_and_the_unless_guard_when_the_rule_has_no_delay()
    {
        const string recipe = "mfc C1_Ar 10\npwait C1_pm 3mT 300\nmfc C1_Ar 0\n";

        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Unbound(), recipe, Marker));

        Assert.Contains("'3mT'", w.Message);
        Assert.Contains("immediately", w.Message);
        Assert.Contains("'C1_A1 open'", w.Message);
        Assert.Contains("'pwait C1_pm 0 1'", w.Message);
        Assert.Contains("'C1'", w.Message);
    }

    [Fact]
    public void Chamber_with_a_looser_idle_ceiling_does_not_warn_below_it()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Unbound(), "mfc TM_Ar 10\npwait TM_pm 0.5mT 60\nmfc TM_Ar 0\n", Marker));
    }

    [Fact]
    public void Chamber_with_a_looser_idle_ceiling_warns_above_it_and_quotes_that_ceiling()
    {
        var w = Assert.Single(ChamberFixture.Warnings(
            ChamberFixture.Unbound(), "mfc TM_Ar 10\npwait TM_pm 5mT 60\nmfc TM_Ar 0\n", Marker));

        Assert.Contains("0.001 Torr", w.Message);
        Assert.DoesNotContain("suppressed only while", w.Message);
    }

    [Fact]
    public void Chamber_without_an_idle_rule_does_not_warn_or_throw()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Unbound(), "mfc LL_Ar 10\npwait LL_pm 50mT 60\nmfc LL_Ar 0\n", Marker));
    }

    [Fact]
    public void Chamber_without_limits_on_a_bound_machine_does_not_warn()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "mfc LL_Ar 10\npwait 50mT 60\nmfc LL_Ar 0\n", Marker));
    }

    [Fact]
    public void Held_target_in_one_chamber_is_not_charged_to_another()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Unbound(), "mfc TM_Ar 10\nmfc C1_Ar 10\npwait C1_pm 3mT 60\nmfc TM_Ar 0\n", Marker));
    }

    [Fact]
    public void Unresolvable_sensor_does_not_warn()
    {
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Unbound(), "mfc C1_Ar 10\npwait $s 3mT 60\nmfc C1_Ar 0\n", Marker));
    }

    [Fact]
    public void Validator_without_chamber_facts_is_unchanged()
    {
        var v = new APS.RecipeModel.Validation.RecipeValidator(
            APS.RecipeModel.Validation.RecipeValidator.AllKnownCommands, new[] { "Ar1" });

        Assert.Empty(ChamberFixture.Warnings(v, "mfc Ar1 10\nmfc Ar1 0\n", Marker));
    }

    [Fact]
    public void Swait_high_vacuum_while_chamber_mfc_still_flowing_warns()
    {
        const string recipe = "mfc Ar1 40\npwait 2mT 60\npwait 0 1\nswait pm 0.05mT 300\nmfc Ar1 0\n";

        var w = Assert.Single(ChamberFixture.Warnings(ChamberFixture.Bound(), recipe, "while gas is still flowing"));

        Assert.Equal(4, w.Line);
        Assert.Contains("Ar1", w.Message);
        Assert.Contains("'Main'", w.Message);
    }

    [Fact]
    public void Swait_after_mfc_zero_does_not_warn_for_gas_flow()
    {
        const string recipe = "mfc Ar1 40\npwait 2mT 60\npwait 0 1\nmfc Ar1 0\nswait pm 0.05mT 300\n";

        Assert.Empty(ChamberFixture.Warnings(ChamberFixture.Bound(), recipe, "while gas is still flowing"));
    }

    [Fact]
    public void Swait_at_or_below_hv_gauge_floor_warns()
    {
        var w = Assert.Single(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "swait pm 1e-4mT 600\n", "high-vacuum gauge floor"));

        Assert.Equal(1, w.Line);
        Assert.Empty(ChamberFixture.Warnings(
            ChamberFixture.Bound(), "swait pm 1e-3mT 300\n", "high-vacuum gauge floor"));
    }
}
