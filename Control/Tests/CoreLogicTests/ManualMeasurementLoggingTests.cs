using System.Globalization;
using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

public sealed class ManualMeasurementLoggingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aps-manual-log", Guid.NewGuid().ToString("N"));
    private readonly string _prevRecipes = AppPaths.RecipesDir;
    private readonly string _prevLogs = AppPaths.LogsDir;
    private readonly string _prevData = AppPaths.DataDir;

    public ManualMeasurementLoggingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        AppPaths.Initialize(new Config(new AppConfig
        {
            ConfigDir = ".", Prefix = "T", WorkDir = ".", RecipesDir = _prevRecipes, LogsDir = _prevLogs, DataDir = _prevData,
        }));
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class TestSensor : BaseSensor
    {
        public float Hw;
        public TestSensor(string id, string description = "", float hw = 0f) : base(new BaseDeviceParams(id, 0, description))
        {
            Connected = true;
            Hw = hw;
            Poll();
        }
        protected override float ReadHW() => Hw;
        public void Set(float value) { Hw = value; Poll(); }
    }

    private sealed class TestGauge : BaseController, IPressureController
    {
        public TestGauge(string id) : base(new BaseDeviceParams(id, 0, "GAUGE_A")) => Connected = true;
        public float PressureTorr { get; set; }
        public float ValvePositionPercent { get; set; }
    }

    private sealed class TestValve : BaseValve
    {
        public bool IsOpenHw;
        public TestValve(string id) : base(new BaseDeviceParams(id, 0, id)) => Connected = true;
        protected override bool ReadHW() => IsOpenHw;
    }

    private sealed class TestPump : BasePump
    {
        public bool[] Bits = [false];
        public TestPump(string id) : base(new BaseDeviceParams(id, 0, id)) => _connected = true;
        protected override bool[] ReadStatus() => Bits;
    }

    private sealed class NamedTestPump : BasePump, IStatusBitNamed
    {
        public bool[] Bits = [false, false, false, false];
        public NamedTestPump(string id) : base(new BaseDeviceParams(id, 0, id)) => _connected = true;
        public IReadOnlyList<string> StatusBitNames { get; } = ["accel", "normal", "rotating", "alarm"];
        protected override bool[] ReadStatus() => Bits;
    }

    private static HwElementCollection Hw(params (DeviceType Type, string Id, IBaseComponent Device)[] devices)
    {
        var hw = new HwElementCollection();
        foreach (var (type, id, device) in devices) hw.Item[type].Add(id, device);
        return hw;
    }

    private static string[][] Rows(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines.Select(l => l.Split('\t')).ToArray();
    }

    private static void Same(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.ToArray(), actual.ToArray());

    private static Dictionary<string, string> Cols(params string[] names) =>
        names.ToDictionary(n => n, _ => "u");

    private static double Parse(string cell) => double.Parse(cell, CultureInfo.InvariantCulture);

    private string Dir(string name = "Manual") => Path.Combine(_root, name);

    // ---- DataLogger formatting ----------------------------------------------------------------

    [Fact]
    public void Wall_clock_file_keeps_the_two_fixed_columns_and_F1_elapsed()
    {
        using var logger = new DataLogger(Dir("Data"));
        logger.Start("run", new Dictionary<string, string> { ["S_pm"] = "Torr" });
        logger.LogRow(new Dictionary<string, double> { ["S_pm"] = 0.001234 });
        logger.Stop();

        var rows = Rows(Directory.GetFiles(Dir("Data"), "*.tsv").Single());
        Same(["Time", "Elapsed", "S_pm"], rows[0]);
        Same(["HH:mm:ss", "s", "Torr"], rows[1]);
        Assert.Matches(@"^\d+\.\d$", rows[2][1]);
        Assert.Equal("0.001234", rows[2][2]);
    }

    [Fact]
    public void Over_under_and_invalid_readings_are_written_as_status_labels_never_zero()
    {
        using var logger = new DataLogger(Dir("Data"));
        logger.Start("run", Cols("A", "B", "C", "D"));
        logger.LogRow(new Dictionary<string, double>
        {
            ["A"] = double.PositiveInfinity, ["B"] = double.NegativeInfinity, ["C"] = double.NaN, ["D"] = 1.5,
        });
        logger.Stop();

        var row = Rows(Directory.GetFiles(Dir("Data"), "*.tsv").Single())[2];
        Same(["OVER", "UNDER", "INVALID", "1.5"], row[2..]);
    }

    // ---- LogConfig ------------------------------------------------------------------------------

    [Fact]
    public void Pressure_that_never_reported_or_is_out_of_range_logs_a_status_not_a_number()
    {
        var zero = new TestSensor("pm", "System", 0f);
        var over = new TestSensor("cm", "CM", float.PositiveInfinity);
        var under = new TestSensor("pl", "PL", float.NegativeInfinity);
        var invalid = new TestSensor("pq", "PQ", float.NaN);
        var good = new TestSensor("pg", "PG", 2.5e-6f);
        var hw = Hw((DeviceType.Sensor, "pm", zero), (DeviceType.Sensor, "cm", over), (DeviceType.Sensor, "pl", under),
            (DeviceType.Sensor, "pq", invalid), (DeviceType.Sensor, "pg", good));

        var values = new LogConfig().CollectValues(hw, new HashSet<string>());

        Assert.True(double.IsNaN(values["S_pm"]));
        Assert.True(double.IsPositiveInfinity(values["S_cm"]));
        Assert.True(double.IsNegativeInfinity(values["S_pl"]));
        Assert.True(double.IsNaN(values["S_pq"]));
        Assert.Equal(2.5e-6, values["S_pg"], 9);
    }

    [Fact]
    public void Temperature_sensor_keeps_a_zero_or_negative_reading()
    {
        var hw = Hw((DeviceType.Sensor, "tmp", new TestSensor("tmp", "Substrate Temp", -5f)));
        var values = new LogConfig().CollectValues(hw, new HashSet<string>());
        Assert.Equal(-5.0, values["S_tmp"], 3);
    }

    [Fact]
    public void GAUGE_A_pressure_and_throttle_position_report_status_when_unavailable()
    {
        var gauge = new TestGauge("GAUGE_A#1") { PressureTorr = 0f, ValvePositionPercent = 140f };
        var hw = Hw((DeviceType.Controller, "GAUGE_A#1", gauge));

        var values = new LogConfig().CollectValues(hw, new HashSet<string>());

        Assert.True(double.IsNaN(values["S_GAUGE_A#1"]));
        Assert.True(double.IsNaN(values["TV_GAUGE_A#1"]));

        gauge.PressureTorr = 0.0021f;
        gauge.ValvePositionPercent = 37.5f;
        values = new LogConfig().CollectValues(hw, new HashSet<string>());
        Assert.Equal(0.0021, values["S_GAUGE_A#1"], 6);
        Assert.Equal(37.5, values["TV_GAUGE_A#1"], 3);
    }

    [Fact]
    public void Clock_sensor_has_no_data_column()
    {
        var hw = Hw((DeviceType.Sensor, "pm", new TestSensor("pm", "System", 1e-3f)),
                    (DeviceType.Sensor, "simtime", new TestSensor("simtime", "Simulation time", 12f)));
        var config = new LogConfig { ClockSensorId = "SimTime" };

        Assert.DoesNotContain("S_simtime", config.BuildColumns(hw, new HashSet<string>()).Keys);
        Assert.DoesNotContain("S_simtime", config.CollectValues(hw, new HashSet<string>()).Keys);
        Assert.Contains("S_simtime", new LogConfig().BuildColumns(hw, new HashSet<string>()).Keys);
    }

    [Fact]
    public void States_are_opt_in_and_name_every_valve_and_pump_bit()
    {
        var valve = new TestValve("PP") { IsOpenHw = true };
        valve.Poll();
        var turbo = new NamedTestPump("TP") { Bits = [false, true, true, false] };
        turbo.Poll();
        var roughing = new TestPump("SP1") { Bits = [true] };
        roughing.Poll();
        var hw = Hw((DeviceType.Valve, "PP", valve), (DeviceType.Pump, "TP", turbo), (DeviceType.Pump, "SP1", roughing));
        var config = new LogConfig();

        Assert.Empty(config.BuildColumns(hw, new HashSet<string>()));
        Assert.Empty(config.CollectValues(hw, new HashSet<string>()));

        var columns = config.BuildColumns(hw, new HashSet<string>(), includeStates: true);
        var values = config.CollectValues(hw, new HashSet<string>(), includeStates: true);

        Assert.Equal(columns.Keys.OrderBy(k => k), values.Keys.OrderBy(k => k));
        Assert.Equal(1, values["VLV_PP"]);
        Assert.Equal(0, values["PMP_TP_accel"]);
        Assert.Equal(1, values["PMP_TP_normal"]);
        Assert.Equal(1, values["PMP_SP1"]);
    }

    [Fact]
    public void Disconnected_valve_and_pump_state_is_invalid_not_closed_or_off()
    {
        var valve = new TestValve("PP") { IsOpenHw = true, Connected = false };
        var pump = new TestPump("SP1") { Connected = false };
        var hw = Hw((DeviceType.Valve, "PP", valve), (DeviceType.Pump, "SP1", pump));

        var values = new LogConfig().CollectValues(hw, new HashSet<string>(), includeStates: true);

        Assert.True(double.IsNaN(values["VLV_PP"]));
        Assert.True(double.IsNaN(values["PMP_SP1"]));
    }

    // ---- ManualDataSession ----------------------------------------------------------------------

    private static (ManualDataSession Session, List<string> Notes, Func<long> Now, Action<long> Advance) NewSession(string dir)
    {
        long now = 0;
        var notes = new List<string>();
        var session = new ManualDataSession(dir, notes.Add, () => now);
        return (session, notes, () => now, ms => now += ms);
    }

    [Fact]
    public void Explicit_clock_gives_sim_elapsed_and_absolute_sim_time_in_seconds()
    {
        var (session, _, _, advance) = NewSession(Dir());
        Assert.True(session.Start(Cols("S_pm"), 100, "simtime"));

        double[] clocks = [1000.0, 1002.5, 1007.25];
        foreach (double c in clocks)
        {
            Assert.True(session.Offer(() => new() { ["S_pm"] = 0.5 }, () => c));
            advance(100);
        }
        string path = session.Stop()!;

        var rows = Rows(path);
        Same(["Time", "Elapsed", "SimTime", "WallElapsed", "S_pm"], rows[0]);
        Same(["HH:mm:ss", "s", "s", "s", "u"], rows[1]);
        Same(["0", "2.5", "7.25"], rows[2..].Select(r => r[1]));
        Same(["1000", "1002.5", "1007.25"], rows[2..].Select(r => r[2]));
        Assert.Equal(4.75, session.MaxClockStepSeconds, 6);
        Assert.Equal(3, session.RowCount);
    }

    [Fact]
    public void Clock_precision_survives_at_thousands_of_seconds_where_G4_would_not()
    {
        var (session, _, _, advance) = NewSession(Dir());
        session.Start(Cols("S_pm"), 100, "simtime");
        float first = 3000.123f, second = 3004.623f;
        session.Offer(() => new() { ["S_pm"] = 1 }, () => (double)first);
        advance(100);
        session.Offer(() => new() { ["S_pm"] = 1 }, () => (double)second);
        var rows = Rows(session.Stop()!);

        Assert.Equal(first, (float)Parse(rows[2][2]));
        Assert.Equal(second, (float)Parse(rows[3][2]));
        Assert.NotEqual("3000", rows[2][2]);
        Assert.Equal(4.5, Parse(rows[3][1]), 3);
    }

    [Fact]
    public void Clock_reset_continues_in_a_new_part_file_and_loses_no_row()
    {
        var (session, notes, _, advance) = NewSession(Dir());
        session.Start(Cols("S_pm"), 100, "simtime");
        foreach (double c in new[] { 100.0, 102.0, 5.0, 7.0 })
        {
            Assert.True(session.Offer(() => new() { ["S_pm"] = c }, () => c));
            advance(100);
        }
        session.Stop();

        var files = session.Files;
        Assert.Equal(2, files.Count);
        Assert.EndsWith("_part2.tsv", files[1]);
        Same(["0", "2"], Rows(files[0])[2..].Select(r => r[1]));
        Same(["0", "2"], Rows(files[1])[2..].Select(r => r[1]));
        Same(["5", "7"], Rows(files[1])[2..].Select(r => r[2]));
        Assert.Equal(4, session.RowCount);
        Assert.Contains(notes, n => n.Contains("restarted") && n.Contains("part 2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Unavailable_clock_writes_no_row_and_is_counted(double? clock)
    {
        var (session, _, _, advance) = NewSession(Dir());
        session.Start(Cols("S_pm"), 100, "simtime");

        Assert.False(session.Offer(() => new() { ["S_pm"] = 1 }, () => clock));
        advance(100);
        Assert.True(session.Offer(() => new() { ["S_pm"] = 1 }, () => 10.0));
        var rows = Rows(session.Stop()!);

        Assert.Equal(3, rows.Length);
        Assert.Equal("0", rows[2][1]);
        Assert.Equal(1, session.RowsWithoutClock);
    }

    [Fact]
    public void Without_a_clock_option_the_file_uses_wall_time_and_the_plain_header()
    {
        var (session, _, _, _) = NewSession(Dir());
        session.Start(Cols("S_pm"), 100);
        session.Offer(() => new() { ["S_pm"] = 1 });
        var rows = Rows(session.Stop()!);

        Same(["Time", "Elapsed", "S_pm"], rows[0]);
        Assert.Matches(@"^\d+\.\d$", rows[2][1]);
    }

    [Fact]
    public void Rows_follow_the_wall_interval_and_collect_runs_only_for_a_due_row()
    {
        var (session, _, _, advance) = NewSession(Dir());
        session.Start(Cols("S_pm"), 500);
        int collected = 0;
        Dictionary<string, double> Collect() { collected++; return new() { ["S_pm"] = 1 }; }

        Assert.True(session.Offer(Collect));
        advance(200);
        Assert.False(session.Offer(Collect));
        advance(200);
        Assert.False(session.Offer(Collect));
        advance(100);
        Assert.True(session.Offer(Collect));
        session.Stop();

        Assert.Equal(2, collected);
        Assert.Equal(2, session.RowCount);
    }

    [Fact]
    public void Interval_changes_apply_to_the_running_session_and_are_clamped()
    {
        var (session, _, _, advance) = NewSession(Dir());
        session.Start(Cols("S_pm"), 5000);
        session.Offer(() => new() { ["S_pm"] = 1 });
        session.SetIntervalMs(1);
        Assert.Equal(ManualLoggingSettings.MinIntervalMs, session.IntervalMs);
        advance(100);
        Assert.True(session.Offer(() => new() { ["S_pm"] = 1 }));
        session.Stop();
    }

    [Fact]
    public void A_column_without_a_value_is_invalid_not_empty_or_zero()
    {
        var (session, _, _, _) = NewSession(Dir());
        session.Start(new Dictionary<string, string> { ["S_pm"] = "Torr", ["VLV_PP"] = "1=open" }, 100);
        session.Offer(() => new() { ["S_pm"] = 0.01 });
        var rows = Rows(session.Stop()!);

        Same(["0.01", "INVALID"], rows[2][2..]);
    }

    [Fact]
    public void Rows_are_on_disk_before_the_session_stops_so_an_export_in_flight_is_complete()
    {
        var (session, _, _, _) = NewSession(Dir());
        session.Start(Cols("S_pm"), 100);
        session.Offer(() => new() { ["S_pm"] = 1 });

        Assert.Equal(3, Rows(session.ActiveFilePath!).Length);
        session.Stop();
    }

    [Fact]
    public void A_second_session_in_the_same_second_never_overwrites_the_first()
    {
        string dir = Dir();
        var (first, _, _, _) = NewSession(dir);
        var (second, _, _, _) = NewSession(dir);
        first.Start(Cols("S_pm"), 100);
        first.Offer(() => new() { ["S_pm"] = 1 });
        first.Stop();
        second.Start(Cols("S_pm"), 100);
        second.Offer(() => new() { ["S_pm"] = 2 });
        second.Stop();

        Assert.Equal(2, Directory.GetFiles(dir, "*.tsv").Length);
        Assert.Equal("1", Rows(first.LastFilePath!)[2][2]);
    }

    [Fact]
    public void Manual_file_is_invisible_to_run_discovery_and_a_run_keeps_its_own_file()
    {
        string data = Dir("Data");
        var (session, _, _, _) = NewSession(Path.Combine(data, "Manual"));
        session.Start(Cols("S_pm"), 100);
        session.Offer(() => new() { ["S_pm"] = 1 });
        session.Stop();
        Assert.StartsWith("Manual_", Path.GetFileName(session.LastFilePath!));

        Directory.CreateDirectory(Dir("Logs"));
        Directory.CreateDirectory(Dir("Recipes"));
        File.WriteAllText(Path.Combine(data, "2026-10-15_10-00-00_Run.tsv"), "Time\tElapsed\nHH:mm:ss\ts\n10:00:00\t0.0\n");
        var runs = new RunArtifacts(data, Dir("Logs"), Dir("Recipes")).List(10, null);

        Same(["2026-10-15_10-00-00_Run"], runs.Select(r => r.RunId));
    }

    // ---- Export ---------------------------------------------------------------------------------

    [Fact]
    public void Export_copies_a_file_that_is_still_open_as_tsv_or_csv()
    {
        var (session, _, _, _) = NewSession(Dir());
        session.Start(Cols("S_pm", "S_cm"), 100);
        session.Offer(() => new() { ["S_pm"] = 0.5, ["S_cm"] = double.PositiveInfinity });

        var tsv = ManualLogExport.Write(session.Files, Path.Combine(_root, "out", "lab.tsv"), csv: false);
        var csv = ManualLogExport.Write(session.Files, Path.Combine(_root, "out", "lab.tsv"), csv: true);
        session.Stop();

        Assert.Equal(Path.Combine(_root, "out", "lab.tsv"), tsv.Single());
        Assert.Equal(Path.Combine(_root, "out", "lab.csv"), csv.Single());
        var lines = File.ReadAllLines(csv.Single());
        Assert.Equal("Time,Elapsed,S_pm,S_cm", lines[0]);
        Assert.EndsWith(",0.5,OVER", lines[2]);
        Assert.Equal(File.ReadAllText(session.LastFilePath!), File.ReadAllText(tsv.Single()));
    }

    [Fact]
    public void Export_of_a_reset_session_writes_every_part()
    {
        var (session, _, _, advance) = NewSession(Dir());
        session.Start(Cols("S_pm"), 100, "simtime");
        foreach (double c in new[] { 50.0, 1.0 })
        {
            session.Offer(() => new() { ["S_pm"] = 1 }, () => c);
            advance(100);
        }
        session.Stop();

        var written = ManualLogExport.Write(session.Files, Path.Combine(_root, "out", "lab.csv"), csv: true);

        Assert.Equal(2, written.Count);
        Assert.All(written, p => Assert.True(File.Exists(p)));
        Assert.EndsWith("lab_part2.csv", written[1]);
    }

    // ---- Profile settings -----------------------------------------------------------------------

    [Theory]
    [InlineData("""<Configurations><ManualLogging clockSensor="simtime" intervalMs="500"/></Configurations>""", "simtime", 500)]
    [InlineData("""<Configurations><ManualLogging clockSensor=" simtime "/></Configurations>""", "simtime", 1000)]
    [InlineData("""<Configurations><ManualLogging intervalMs="5"/></Configurations>""", null, 100)]
    [InlineData("""<Configurations><ManualLogging intervalMs="999999"/></Configurations>""", null, 60000)]
    [InlineData("""<Configurations><ManualLogging clockSensor="" intervalMs="x"/></Configurations>""", null, 1000)]
    [InlineData("""<Configurations/>""", null, 1000)]
    [InlineData("""<not xml""", null, 1000)]
    public void Profile_settings_parse_with_safe_defaults(string xml, string? clock, int interval)
    {
        File.WriteAllText(Path.Combine(_root, "Config.xml"), xml);

        var settings = ManualLoggingSettings.Load(_root);

        Assert.Equal(clock, settings.ClockSensorId);
        Assert.Equal(interval, settings.IntervalMs);
    }

    [Fact]
    public void Profile_without_Config_xml_or_directory_uses_wall_clock_defaults()
    {
        Assert.Equal(ManualLoggingSettings.Default, ManualLoggingSettings.Load(null));
        Assert.Equal(ManualLoggingSettings.Default, ManualLoggingSettings.Load(Path.Combine(_root, "missing")));
    }

    // ---- Core: manual log beside recipe logging -------------------------------------------------

    private APS.SystemControl.Core.Core NewCore(string? configXml, out TestSensor pm, out TestSensor? clock, bool withClockSensor = true)
    {
        string cfg = Path.Combine(_root, "cfg");
        Directory.CreateDirectory(cfg);
        if (configXml is not null) File.WriteAllText(Path.Combine(cfg, "Config.xml"), configXml);
        AppPaths.Initialize(new Config(new AppConfig
        {
            ConfigDir = _root, Prefix = "t", WorkDir = _root, RecipesDir = _root, LogsDir = _root,
            DataDir = Path.Combine(_root, "Data"),
        }));

        var core = new APS.SystemControl.Core.Core(Channel.CreateUnbounded<ThreadMsg>().Writer) { ConfigDir = cfg };
        core.ApplyConfiguredLogPaths();
        pm = new TestSensor("pm", "System", 0.02f);
        core.hwElements.Item[DeviceType.Sensor].Add("pm", pm);
        var valve = new TestValve("PP") { IsOpenHw = true };
        valve.Poll();
        core.hwElements.Item[DeviceType.Valve].Add("PP", valve);
        clock = null;
        if (withClockSensor)
        {
            clock = new TestSensor("simtime", "Simulation time", 100f);
            core.hwElements.Item[DeviceType.Sensor].Add("simtime", clock);
        }
        return core;
    }

    private const string ClockProfile = """<Configurations><ManualLogging clockSensor="simtime" intervalMs="100"/></Configurations>""";

    [Fact]
    public void Core_manual_log_records_pressure_and_states_and_survives_a_recipe_run_in_its_own_file()
    {
        var core = NewCore(null, out _, out _, withClockSensor: false);
        core.ManualLogIntervalMs = 100;
        Assert.True(core.TryStartManualLogging(out string manualPath));
        core.LogDataRow();

        Assert.True(core.TryBeginRecipeSlot());
        core.StartDataLogging("Run A");
        string recipePath = core.ActiveDataLogPath!;
        Thread.Sleep(130);
        core.LogDataRow();
        string? closedRecipe = core.StopDataLogging();
        core.EndRecipeSlot();
        Thread.Sleep(130);
        core.LogDataRow();
        string? closedManual = core.StopManualLogging();

        Assert.Equal(recipePath, closedRecipe);
        Assert.NotEqual(manualPath, recipePath);
        Assert.Equal(Path.Combine(_root, "Data", "Manual"), Path.GetDirectoryName(manualPath));
        Assert.Equal(manualPath, closedManual);

        var manual = Rows(manualPath);
        Same(["Time", "Elapsed", "S_pm", "VLV_PP", "RecipeRunning"], manual[0]);
        Same(["0.02", "0.02", "0.02"], manual[2..].Select(r => r[2]));
        Same(["1", "1", "1"], manual[2..].Select(r => r[3]));
        Same(["0", "1", "0"], manual[2..].Select(r => r[4]));

        var recipe = Rows(recipePath);
        Same(["Time", "Elapsed", "S_pm"], recipe[0]);
        Assert.Equal(3, recipe.Length);
        Assert.Null(core.ActiveDataLogPath);
    }

    [Fact]
    public void Core_stopping_the_manual_log_never_touches_the_recipe_data_file()
    {
        var core = NewCore(null, out _, out _, withClockSensor: false);
        Assert.True(core.TryBeginRecipeSlot());
        core.StartDataLogging("Run A");
        string recipePath = core.ActiveDataLogPath!;
        Assert.True(core.TryStartManualLogging(out _));

        core.StopManualLogging();

        Assert.Equal(recipePath, core.ActiveDataLogPath);
        Assert.False(core.IsManualLogging);
        core.StopDataLogging();
        core.EndRecipeSlot();
    }

    [Fact]
    public void Core_with_a_profile_clock_logs_sim_seconds_and_no_pressure_column_for_the_clock()
    {
        var core = NewCore(ClockProfile, out _, out var clock, withClockSensor: true);
        Assert.True(core.TryStartManualLogging(out string path));

        foreach (float t in new[] { 100f, 104.5f, 109f })
        {
            clock!.Set(t);
            core.LogDataRow();
            Thread.Sleep(120);
        }
        core.StopManualLogging();

        var rows = Rows(path);
        Same(["Time", "Elapsed", "SimTime", "WallElapsed", "S_pm", "VLV_PP", "RecipeRunning"], rows[0]);
        Same(["0", "4.5", "9"], rows[2..].Select(r => r[1]));
        Same(["100", "104.5", "109"], rows[2..].Select(r => r[2]));
    }

    [Fact]
    public void Core_recipe_logging_stays_on_the_wall_clock_even_when_the_profile_names_a_clock()
    {
        var core = NewCore(ClockProfile, out _, out _, withClockSensor: true);
        Assert.True(core.TryBeginRecipeSlot());
        core.StartDataLogging("Run A");
        core.LogDataRow();
        string path = core.StopDataLogging()!;
        core.EndRecipeSlot();

        var rows = Rows(path);
        Same(["Time", "Elapsed", "S_pm"], rows[0]);
        Assert.Matches(@"^\d+\.\d$", rows[2][1]);
    }

    [Fact]
    public void Core_refuses_to_start_when_the_profile_clock_sensor_is_missing()
    {
        var core = NewCore(ClockProfile, out _, out _, withClockSensor: false);

        Assert.False(core.TryStartManualLogging(out string message));

        Assert.Contains("simtime", message);
        Assert.False(core.IsManualLogging);
        Assert.Empty(core.ManualLogFiles);
    }

    [Fact]
    public void Core_starting_twice_keeps_the_first_file_and_reports_it()
    {
        var core = NewCore(null, out _, out _, withClockSensor: false);
        Assert.True(core.TryStartManualLogging(out string first));

        Assert.False(core.TryStartManualLogging(out string message));

        Assert.Contains(first, message);
        Assert.Equal(first, core.ManualLogPath);
        core.StopManualLogging();
    }

    [Fact]
    public void Core_unavailable_pressure_reaches_the_file_as_a_status_label()
    {
        var core = NewCore(null, out var pm, out _, withClockSensor: false);
        core.ManualLogIntervalMs = 100;
        Assert.True(core.TryStartManualLogging(out string path));
        foreach (float value in new[] { 0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 0.5f })
        {
            pm.Set(value);
            core.LogDataRow();
            Thread.Sleep(120);
        }
        core.StopManualLogging();

        Same(["INVALID", "OVER", "UNDER", "INVALID", "0.5"], Rows(path)[2..].Select(r => r[2]));
    }

    [Fact]
    public void Core_disconnect_closes_the_measurement_log()
    {
        var core = NewCore(null, out _, out _, withClockSensor: false);
        Assert.True(core.TryStartManualLogging(out _));

        core.DisconnectAll();

        Assert.False(core.IsManualLogging);
    }
}
