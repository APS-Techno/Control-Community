using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class AlarmConfigLoaderTests : IDisposable
{
    private readonly string _tmp;

    public AlarmConfigLoaderTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "alarm-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { /* best-effort */ }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_applies_initial_state_and_rules()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="startup" />
              <alarm sensor="pl" val="1.0" cond="more" state="processing">Pressure too high</alarm>
              <alarm sensor="pm" val="0.1" cond="less" state="processing">Pressure too low</alarm>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);
        var alarms = result.ChamberAlarms[CoreConstants.DefaultChamberId];

        Assert.Equal(SystemState.Startup, result.InitialStates[CoreConstants.DefaultChamberId]);
        Assert.Equal(2, alarms.Rules.Count);
        Assert.Contains(alarms.Rules, r => r.Sensor == "pl" && r.Condition == 1  && r.Value == 1.0f);
        Assert.Contains(alarms.Rules, r => r.Sensor == "pm" && r.Condition == -1 && r.Value == 0.1f);
    }

    [Fact]
    public void Load_parses_optional_settling_delay_and_defaults_to_zero()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="startup" />
              <alarm sensor="pm" val="1e-4" cond="more" state="idle" delay="60">Pressure too high</alarm>
              <alarm sensor="pl" val="1.0" cond="more" state="processing">Pressure too high</alarm>
            </alarms>
            """);
        var alarms = AlarmConfigLoader.Load(path).ChamberAlarms[CoreConstants.DefaultChamberId];

        Assert.Contains(alarms.Rules, r => r.Sensor == "pm" && r.DelaySeconds == 60f);
        // A rule without the attribute keeps the original fire-immediately behaviour.
        Assert.Contains(alarms.Rules, r => r.Sensor == "pl" && r.DelaySeconds == 0f);
    }

    /// <summary>
    /// A malformed delay must not silently degrade into "no delay" — that would
    /// quietly bring the nuisance alarm back. It warns and skips the rule, the
    /// same contract as an unknown condition.
    /// </summary>
    [Fact]
    public void Load_invalid_delay_warns_and_skips_the_rule()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="startup" />
              <alarm sensor="pm" val="1e-4" cond="more" state="idle" delay="soon">Pressure too high</alarm>
            </alarms>
            """);

        var result = AlarmConfigLoader.Load(path);

        Assert.Single(result.Warnings);
        Assert.Contains("delay", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("soon", result.Warnings[0]);
        Assert.Empty(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }

    [Fact]
    public void Load_collects_transition_log_lines()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="idle">
                <transition from="idle" to="processing" sensor="pm" cond="less" val="0.001" />
                <transition from="processing" to="idle" valve="GVM" state="closed" />
              </states>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);

        Assert.Equal(SystemState.Idle, result.InitialStates[CoreConstants.DefaultChamberId]);
        Assert.Equal(2, result.StateTransitionLog.Count);
        Assert.Contains(result.StateTransitionLog, s => s.Contains("pm") && s.Contains("less"));
        Assert.Contains(result.StateTransitionLog, s => s.Contains("valve GVM"));
    }

    [Fact]
    public void Load_unknown_condition_warns_with_context_and_skips_rule()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm sensor="pl" val="1.0" cond="greater" state="processing">x</alarm>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);
        Assert.Single(result.Warnings);
        Assert.Contains("greater", result.Warnings[0]);
        Assert.Contains("pl", result.Warnings[0]);
        Assert.Empty(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }

    [Fact]
    public void Load_transition_missing_cond_throws()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states>
                <transition from="idle" to="processing" sensor="pm" />
              </states>
            </alarms>
            """);
        var ex = Assert.Throws<InvalidOperationException>(
            () => AlarmConfigLoader.Load(path));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void Load_empty_path_throws()
    {
        Assert.Throws<ArgumentException>(() => AlarmConfigLoader.Load(""));
    }

    [Fact]
    public void Load_no_initial_state_returns_null()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm sensor="pl" val="1.0" cond="more" state="processing">x</alarm>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);
        Assert.Null(result.InitialStates[CoreConstants.DefaultChamberId]);
    }

    [Fact]
    public void Load_group_transition_any_open_parses()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="idle">
                <transition from="idle" to="processing" group="gasvalve" state="anyOpen" />
              </states>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);

        Assert.Single(result.StateTransitionLog);
        Assert.Contains("group gasvalve anyOpen", result.StateTransitionLog[0]);
    }

    [Fact]
    public void Load_group_transition_all_off_parses()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="processing">
                <transition from="processing" to="idle" group="gas" state="allOff" />
              </states>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);

        Assert.Single(result.StateTransitionLog);
        Assert.Contains("group gas allOff", result.StateTransitionLog[0]);
    }

    [Fact]
    public void Load_group_transition_unknown_group_throws()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states>
                <transition from="idle" to="processing" group="plasma" state="anyOpen" />
              </states>
            </alarms>
            """);
        Assert.Throws<InvalidOperationException>(() => AlarmConfigLoader.Load(path));
    }

    [Fact]
    public void Load_flat_config_yields_implicit_Main_chamber()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="startup">
                <transition from="startup" to="idle" sensor="pm" cond="less" val="1e-6" />
              </states>
              <alarm sensor="pm" cond="more" val="1e-4" state="idle">msg</alarm>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);
        Assert.Single(result.ChamberAlarms);
        Assert.True(result.ChamberAlarms.ContainsKey(CoreConstants.DefaultChamberId));
        Assert.Equal(SystemState.Startup, result.InitialStates[CoreConstants.DefaultChamberId]);
        Assert.Equal(CoreConstants.DefaultChamberId,
            result.ChamberAlarms[CoreConstants.DefaultChamberId].ChamberId);
        Assert.Single(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }

    [Fact]
    public void Load_chamber_wrapper_yields_named_chambers()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <chamber id="Main">
                <states initial="startup">
                  <transition from="startup" to="idle" sensor="pm" cond="less" val="1e-6" />
                </states>
                <alarm sensor="pm" cond="more" val="1e-4" state="idle">msg</alarm>
              </chamber>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);
        Assert.Single(result.ChamberAlarms);
        Assert.True(result.ChamberAlarms.ContainsKey("Main"));
        Assert.Equal(SystemState.Startup, result.InitialStates["Main"]);
        Assert.Equal("Main", result.ChamberAlarms["Main"].ChamberId);
        Assert.Single(result.ChamberAlarms["Main"].Rules);
    }

    [Fact]
    public void Load_multiple_chambers_yields_distinct_keyed_entries()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <chamber id="Main">
                <states initial="startup" />
                <alarm sensor="pm" cond="more" val="1e-4" state="idle">main-msg</alarm>
              </chamber>
              <chamber id="LoadLock">
                <states initial="idle" />
                <alarm sensor="pll" cond="more" val="1e-2" state="idle">ll-msg</alarm>
              </chamber>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path);
        Assert.Equal(2, result.ChamberAlarms.Count);
        Assert.True(result.ChamberAlarms.ContainsKey("Main"));
        Assert.True(result.ChamberAlarms.ContainsKey("LoadLock"));
        Assert.Equal(SystemState.Startup, result.InitialStates["Main"]);
        Assert.Equal(SystemState.Idle, result.InitialStates["LoadLock"]);
        Assert.Single(result.ChamberAlarms["Main"].Rules);
        Assert.Single(result.ChamberAlarms["LoadLock"].Rules);
    }

    [Fact]
    public void Load_chamber_missing_id_throws()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <chamber>
                <states initial="startup" />
              </chamber>
            </alarms>
            """);
        Assert.Throws<InvalidOperationException>(() => AlarmConfigLoader.Load(path));
    }

    [Fact]
    public void Load_group_transition_unknown_state_throws()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states>
                <transition from="idle" to="processing" group="gasvalve" state="sometimes" />
              </states>
            </alarms>
            """);
        Assert.Throws<InvalidOperationException>(() => AlarmConfigLoader.Load(path));
    }
}
