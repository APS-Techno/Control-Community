using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class InterlocksTests
{
    [Fact]
    public void Registry_get_unknown_key_returns_null()
    {
        var r = new Interlocks();
        Assert.Null(r.Get("missing"));
    }

    [Fact]
    public void Registry_add_and_get_round_trip()
    {
        var r = new Interlocks();
        var il = new Interlock();
        il.Conditions.Add(new InterlockCond { Value = true, Condition = 0 });
        r.Add("valve_a1_true", il);

        var fetched = r.Get("valve_a1_true");
        Assert.NotNull(fetched);
        Assert.Single(fetched!.Conditions);
    }

    [Fact]
    public void Registry_add_replaces_existing_key()
    {
        var r = new Interlocks();
        r.Add("k", new Interlock());
        var second = new Interlock();
        second.Conditions.Add(new InterlockCond());
        r.Add("k", second);

        Assert.Single(r.Get("k")!.Conditions);
    }

    [Fact]
    public void Registry_GetAllInterlocks_exposes_all_keys()
    {
        var r = new Interlocks();
        r.Add("a", new Interlock());
        r.Add("b", new Interlock());
        var all = r.GetAllInterlocks();
        Assert.Equal(2, all.Count);
        Assert.Contains("a", all.Keys);
        Assert.Contains("b", all.Keys);
    }

    [Theory]
    [InlineData(true,  true,  false)]
    [InlineData(false, false, false)]
    [InlineData(true,  false, true)]
    [InlineData(false, true,  true)]
    public void ValveFails_matches_expected(bool actual, bool required, bool expected)
    {
        Assert.Equal(expected, InterlockEvaluator.ValveFails(actual, required));
    }

    [Theory]
    [InlineData(ValveState.Open,      true,  false)]
    [InlineData(ValveState.Closed,    false, false)]
    [InlineData(ValveState.Open,      false, true)]
    [InlineData(ValveState.Closed,    true,  true)]
    [InlineData(ValveState.Undefined, false, true)]  // Undefined = disconnected
    [InlineData(ValveState.Undefined, true,  true)]
    public void ValveFails_state_overload_fails_safe_on_undefined(ValveState state, bool required, bool expected)
    {
        Assert.Equal(expected, InterlockEvaluator.ValveFails(state, required));
    }

    [Theory]
    [InlineData(true,  true,  false)]
    [InlineData(false, false, false)]
    [InlineData(true,  false, true)]
    [InlineData(false, true,  true)]
    public void PumpFails_matches_expected(bool actual, bool required, bool expected)
    {
        Assert.Equal(expected, InterlockEvaluator.PumpFails(actual, required));
    }

    // Sensor condition codes:
    //   -1 (less)  fails when actual >  expected
    //    0 (equal) fails when actual != expected
    //    1 (more)  fails when actual <  expected

    [Theory]
    [InlineData(0.5f, -1, 1.0f, false)]
    [InlineData(2.0f, -1, 1.0f, true)]
    [InlineData(1.0f, -1, 1.0f, false)]
    public void SensorFails_less_rule(float actual, int cond, float expected, bool fails)
    {
        Assert.Equal(fails, InterlockEvaluator.SensorFails(actual, cond, expected));
    }

    [Theory]
    [InlineData(2.0f, 1, 1.0f, false)]
    [InlineData(0.5f, 1, 1.0f, true)]
    [InlineData(1.0f, 1, 1.0f, false)]
    public void SensorFails_more_rule(float actual, int cond, float expected, bool fails)
    {
        Assert.Equal(fails, InterlockEvaluator.SensorFails(actual, cond, expected));
    }

    [Theory]
    [InlineData(1.0f, 0, 1.0f, false)]
    [InlineData(1.01f, 0, 1.0f, true)]    // no tolerance
    public void SensorFails_equal_rule(float actual, int cond, float expected, bool fails)
    {
        Assert.Equal(fails, InterlockEvaluator.SensorFails(actual, cond, expected));
    }

    [Fact]
    public void SensorFails_unknown_condition_does_not_fail()
    {
        // Unknown codes deliberately do not block, preserving Core behaviour.
        Assert.False(InterlockEvaluator.SensorFails(999f, 42, 0f));
    }

    // Stage position interlock guards moves that would collide a stage
    // with a closing valve (e.g. GVL must not close unless HS at home=15).

    [Theory]
    [InlineData(15.0f,  15.0f, 1.0f, false)]
    [InlineData(15.5f,  15.0f, 1.0f, false)]
    [InlineData(14.5f,  15.0f, 1.0f, false)]
    [InlineData(16.0f,  15.0f, 1.0f, false)]
    [InlineData(14.0f,  15.0f, 1.0f, false)]
    [InlineData(16.1f,  15.0f, 1.0f, true)]
    [InlineData(10.0f,  15.0f, 1.0f, true)]
    [InlineData( 0.0f,  15.0f, 1.0f, true)]   // PLC zero = arm out
    public void StageFails_matches_expected(float actual, float expected, float tolerance, bool fails)
    {
        Assert.Equal(fails, InterlockEvaluator.StageFails(actual, expected, tolerance));
    }

    // Stage moves build runtime keys like "stage_hs_280" because the
    // position arg is continuous. Position-specific XML rules would only
    // catch one target. ResolveInterlockKey falls back to a
    // position-agnostic key (e.g. "stage_hs") when no exact rule exists,
    // so a single XML entry can guard every target position.

    private static BaseStage MakeStage(string id) =>
        new BaseStage(new BaseDeviceParams(id, 0, ""));

    private static BaseValve MakeValve(string id) =>
        new BaseValve(new BaseDeviceParams(id, 0, ""));

    [Fact]
    public void ResolveKey_exact_match_wins_for_valve()
    {
        var r = new Interlocks();
        r.Add("valve_gvl_false", new Interlock());

        string key = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Valve", new object[] { MakeValve("GVL"), false });

        Assert.Equal("valve_gvl_false", key);
    }

    [Fact]
    public void ResolveKey_stage_falls_back_to_position_agnostic_key()
    {
        var r = new Interlocks();
        r.Add("stage_hs", new Interlock());

        string key280 = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Stage", new object[] { MakeStage("HS"), 280f });
        string keyHome = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Stage", new object[] { MakeStage("HS"), 15f });
        string keyNeg = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Stage", new object[] { MakeStage("HS"), -10f });

        Assert.Equal("stage_hs", key280);
        Assert.Equal("stage_hs", keyHome);
        Assert.Equal("stage_hs", keyNeg);
    }

    [Fact]
    public void ResolveKey_stage_exact_position_match_wins_over_fallback()
    {
        var r = new Interlocks();
        r.Add("stage_hs", new Interlock());
        r.Add("stage_hs_280", new Interlock());

        string key = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Stage", new object[] { MakeStage("HS"), 280f });

        Assert.Equal("stage_hs_280", key);
    }

    [Fact]
    public void ResolveKey_no_match_returns_full_key_for_diagnostics()
    {
        // Caller treats a null Get(key) as unguarded; the full key is for logging.
        var r = new Interlocks();

        string key = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Stage", new object[] { MakeStage("VS"), 25f });

        Assert.Equal("stage_vs_25", key);
    }

    [Fact]
    public void ResolveKey_non_stage_does_not_fall_back()
    {
        // Falling back to the bare "valve_pp" would change valve/pump interlock semantics.
        var r = new Interlocks();
        r.Add("valve_pp", new Interlock());

        string key = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "Valve", new object[] { MakeValve("PP"), true });

        Assert.Equal("valve_pp_true", key);
    }

    [Fact]
    public void ResolveKey_op_with_non_device_first_arg_does_not_throw()
    {
        // 'deposit Cu 60' has the material string, not an IBaseComponent, as fArgs[0];
        // a cast here aborted the recipe at the first deposit.
        var r = new Interlocks();

        string key = APS.SystemControl.Core.Core.ResolveInterlockKey(
            r, "deposit", new object[] { "Cu", 60f });

        Assert.Equal("deposit_cu_60", key);
    }

    [Fact]
    public void ResolveKey_formats_fractional_float_arg_invariant_on_comma_locale()
    {
        // ru-RU formats 10.5 as "10,5", but registry keys come from culture-neutral
        // XML text. The runtime key must use '.' or it never matches the rule and the
        // interlock silently fails open. (audit U-18)
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

            var r = new Interlocks();
            string key = APS.SystemControl.Core.Core.ResolveInterlockKey(
                r, "Stage", new object[] { MakeStage("VS"), -10.5f });

            Assert.Equal("stage_vs_-10.5", key);
            Assert.DoesNotContain(",", key);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prev;
        }
    }
}
