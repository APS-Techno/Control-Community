using System.Text.Json;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.Diagnostics;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class RunReceiptTests : IDisposable
{
    public RunReceiptTests() => RecipeProgressTracker.Reset();

    public void Dispose() => RecipeProgressTracker.Reset();

    private sealed class Gauge : BaseSensor
    {
        private readonly float _value;

        public Gauge(string id, string chamber, float value, string description = "gauge", bool connected = true)
            : base(new BaseDeviceParams(id, 0, description))
        {
            _value = value;
            Chamber = chamber;
            Connected = true;
            Value();
            Connected = connected;
        }

        protected override float ReadHW() => _value;
    }

    private const string OneLiner = """
        <?xml version="1.0" encoding="utf-8"?>
        <recipes>
          <recipe id="TRIVIAL">
            <l>valve A1 open</l>
          </recipe>
        </recipes>
        """;

    private static async Task RunTrivialRecipe(CoreThread thread)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, OneLiner);
        try
        {
            thread.Core.embRecipes = new EmbeddedRecipes(path);
            await thread.ProcessEmbeddedRecipe("TRIVIAL");
        }
        finally { File.Delete(path); }
    }

    private static List<string> Drain(ChannelPool pool)
    {
        var lines = new List<string>();
        while (pool.GUIReader!.TryRead(out var msg))
            if (msg is ValMsg<string> { Value: not null } v) lines.Add(v.Value);
        return lines;
    }

    private static void AddSensor(CoreThread thread, Gauge sensor) =>
        thread.Core.hwElements.Item[DeviceType.Sensor].Add(sensor.Params.ID, sensor);

    [Fact]
    public async Task Without_pm_each_connected_chamber_logs_its_primary_pressure_sensor()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;
        AddSensor(thread, new Gauge("C2_pm", "C2", 2e-6f));
        AddSensor(thread, new Gauge("C1_pm", "C1", 1e-6f));
        AddSensor(thread, new Gauge("C1_tmp", "C1", 25f, "C1 temperature"));
        AddSensor(thread, new Gauge("C3_pm", "C3", 3e-6f, connected: false));

        await RunTrivialRecipe(thread);

        string[] expected =
        [
            "Base pressure (C1, C1_pm): 1.00E-006 Torr",
            "Base pressure (C2, C2_pm): 2.00E-006 Torr",
        ];
        Assert.Equal(expected, RecipeProgressTracker.Receipt().BasePressureLogQuotes);
        var log = Drain(pool);
        foreach (var line in expected) Assert.Contains(line, log);
    }

    [Fact]
    public async Task With_pm_only_the_pm_line_is_logged_and_recorded()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;
        AddSensor(thread, new Gauge("pm", "Main", 1.5e-6f));
        AddSensor(thread, new Gauge("C1_pm", "C1", 1e-6f));

        await RunTrivialRecipe(thread);

        string line = RecipeProgressTracker.Receipt().BasePressureLogQuotes.Single();
        Assert.StartsWith("Base pressure: ", line);
        Assert.Contains(line, Drain(pool));
    }

    [Fact]
    public async Task Pm_quote_uses_invariant_number_format_on_a_comma_locale()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var pool = new ChannelPool();
            var thread = new CoreThread(pool);
            thread.Core.LogEmbeddedRecipes = true;
            AddSensor(thread, new Gauge("pm", "Main", 1.5e-6f));

            await RunTrivialRecipe(thread);

            Assert.Equal("Base pressure: 1.50E-006 Torr", RecipeProgressTracker.Receipt().BasePressureLogQuotes.Single());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public async Task Disconnected_pm_records_the_unavailable_line()
    {
        var thread = new CoreThread(new ChannelPool());
        thread.Core.LogEmbeddedRecipes = true;
        AddSensor(thread, new Gauge("pm", "Main", 1.5e-6f, connected: false));

        await RunTrivialRecipe(thread);

        Assert.Equal(["Base pressure: unavailable (pm not connected)"],
            RecipeProgressTracker.Receipt().BasePressureLogQuotes);
    }

    [Fact]
    public async Task A_new_run_replaces_the_previous_quotes_and_zeroes_the_deposit_count()
    {
        var thread = new CoreThread(new ChannelPool());
        thread.Core.LogEmbeddedRecipes = true;
        RecipeProgressTracker.RecordBasePressureLog("stale");
        RecipeProgressTracker.RecordDepositCompleted();
        AddSensor(thread, new Gauge("C1_pm", "C1", 1e-6f));

        await RunTrivialRecipe(thread);

        var (quotes, deposits) = RecipeProgressTracker.Receipt();
        Assert.Equal(["Base pressure (C1, C1_pm): 1.00E-006 Torr"], quotes);
        Assert.Equal(0, deposits);
    }

    [Fact]
    public void Chamber_alarm_sensor_is_preferred_over_ordinal_order()
    {
        BasePressureSensors.Candidate[] sensors =
        [
            new("C1_a", "C1", "gauge"),
            new("C1_z", "C1", "gauge"),
            new("C1_tmp", "C1", "temperature"),
        ];

        var withRule = BasePressureSensors.PickPrimaries(sensors, _ => ["C1_z"]);
        var withoutRule = BasePressureSensors.PickPrimaries(sensors, _ => []);

        Assert.Equal([("C1", "C1_z")], withRule);
        Assert.Equal([("C1", "C1_a")], withoutRule);
    }

    [Fact]
    public async Task Chamber_alarm_rule_sensor_is_the_one_logged()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;
        AddSensor(thread, new Gauge("C1_a", "C1", 1e-6f));
        AddSensor(thread, new Gauge("C1_z", "C1", 4e-6f));
        var alarms = new Alarms("C1");
        alarms.Add("C1_z", 1e-4f, 1, SystemState.Idle, "C1 pressure high");
        thread.Core.chamberAlarms["C1"] = alarms;

        await RunTrivialRecipe(thread);

        Assert.Equal(["Base pressure (C1, C1_z): 4.00E-006 Torr"],
            RecipeProgressTracker.Receipt().BasePressureLogQuotes);
    }

    [Fact]
    public void RecipeStatus_reports_the_receipt()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        RecipeProgressTracker.RecordBasePressureLog("Base pressure: 1.00E-006 Torr");
        RecipeProgressTracker.RecordDepositCompleted();
        var agent = new CoreAgentSystemAccess(thread, pool, new EventLogRingBuffer());

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(agent.RecipeStatus()));

        Assert.Equal("Base pressure: 1.00E-006 Torr",
            doc.RootElement.GetProperty("basePressureLogQuotes").EnumerateArray().Single().GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("executedDepositsCount").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("isRecipeRunning", out _));
        Assert.True(doc.RootElement.TryGetProperty("lastOutcome", out _));
    }

    [Fact]
    public void DescribeMachine_reports_identity_keys()
    {
        var pool = new ChannelPool();
        var agent = new CoreAgentSystemAccess(new CoreThread(pool), pool, new EventLogRingBuffer());

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(agent.DescribeMachine()));

        Assert.False(doc.RootElement.GetProperty("isEmulator").GetBoolean());
        Assert.Equal("physical", doc.RootElement.GetProperty("executionTarget").GetString());
        Assert.True(doc.RootElement.TryGetProperty("profile", out _));
        Assert.True(doc.RootElement.TryGetProperty("controlMode", out _));
    }
}
