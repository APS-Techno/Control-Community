using System.Globalization;
using System.Text;
using System.Text.Json;
using APS.Mcp;
using APS.Wire;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Xunit;

public sealed class WatchRecipeToolsTests : IDisposable
{
    private readonly string _telemetryDir = Path.Combine(Path.GetTempPath(), $"aps-watch-test-{Guid.NewGuid():N}");

    public WatchRecipeToolsTests() => Directory.CreateDirectory(_telemetryDir);

    public void Dispose()
    {
        try { Directory.Delete(_telemetryDir, recursive: true); } catch { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    private sealed class CapturingProgress : IProgress<ProgressNotificationValue>
    {
        public readonly List<ProgressNotificationValue> Values = [];
        public void Report(ProgressNotificationValue value) => Values.Add(value);
    }

    private sealed class ScriptedStatusWire : IWireCaller
    {
        public readonly Queue<string> Responses = new();
        public int Calls;

        public Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (cmd != "recipe.status") throw new InvalidOperationException(cmd);
            Calls++;
            string json = Responses.Count > 1 ? Responses.Dequeue() : Responses.Peek();
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
    }

    private static string StatusJson(
        bool running = true,
        int state = 1,
        string stepDesc = "deposit Ru 31.26 Home",
        long stepSequence = 1,
        int deposits = 0,
        object? loopStack = null,
        int loopIter = 0,
        object? lastOutcome = null,
        object? alarms = null,
        object? psus = null,
        object? pressures = null)
    {
        var payload = new
        {
            isRecipeRunning = running,
            isUserRecipe = true,
            failureProtocolActive = false,
            progress = new { currentStep = 5, totalSteps = 20, stepDescription = stepDesc, state },
            executedDepositsCount = deposits,
            stepSequence,
            loopStack = loopStack ?? Array.Empty<object>(),
            currentLoopIteration = loopIter,
            lastOutcome,
            activeAlarms = alarms ?? Array.Empty<object>(),
            activeTelemetry = new
            {
                psus = psus ?? Array.Empty<object>(),
                pressures = pressures ?? Array.Empty<object>(),
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    private static ControlTools CreateTools(IWireCaller wire, FakeTimeProvider time, string telemetryDir) =>
        new(wire, (d, ct) => { ct.ThrowIfCancellationRequested(); time.Advance(d); return Task.CompletedTask; },
            timeProvider: time, telemetryDirOverride: telemetryDir);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Wake_after_deposits_non_positive_throws_bad_request_without_polling(int badValue)
    {
        var wire = new ScriptedStatusWire();
        var tools = CreateTools(wire, new FakeTimeProvider(), _telemetryDir);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.WatchRecipe(wake_after_deposits: badValue));
        Assert.Contains("bad_request", ex.Message);
        Assert.Equal(0, wire.Calls);
    }

    [Fact]
    public async Task Wakes_on_deposit_milestone_after_relative_delta_writes_tsv_and_emits_progress()
    {
        var time = new FakeTimeProvider();
        var wire = new ScriptedStatusWire();
        object[] psu = [new { id = "PSU_C", voltageV = 382.5, currentMa = 50.0, powerW = 19.1, supportsCurrent = true, setCurrentMa = 50.0, setPowerW = 0.0 }];
        object[] press = [new { chamber = "Main", sensor = "pm", pressureTorr = 0.003 }];
        object[] loops = [new { iteration = 2, total = 50 }];

        wire.Responses.Enqueue(StatusJson(stepSequence: 10, deposits: 2, loopStack: loops, loopIter: 2, psus: psu, pressures: press));
        wire.Responses.Enqueue(StatusJson(stepSequence: 11, deposits: 3, loopStack: loops, loopIter: 2, psus: psu, pressures: press));
        wire.Responses.Enqueue(StatusJson(stepSequence: 12, deposits: 4, loopStack: loops, loopIter: 3, psus: psu, pressures: press));

        var progress = new CapturingProgress();
        var tools = CreateTools(wire, time, _telemetryDir);
        string receipt = await tools.WatchRecipe(poll_interval_seconds: 2.0, wake_after_deposits: 2, progress: progress);

        Assert.True(Encoding.UTF8.GetByteCount(receipt) < 1024);
        var doc = JsonDocument.Parse(receipt).RootElement;
        Assert.Equal("deposit_milestone", doc.GetProperty("wakeReason").GetString());
        Assert.Equal(4, doc.GetProperty("executedDepositsCount").GetInt32());
        Assert.Equal(3, doc.GetProperty("currentLoopIteration").GetInt32());
        Assert.Equal(382.5, doc.GetProperty("psuSummary").GetProperty("PSU_C").GetProperty("voltageV").GetProperty("mean").GetDouble(), 2);
        Assert.Equal(0.003, doc.GetProperty("pressureSummary").GetProperty("Main").GetProperty("mean").GetDouble(), 5);
        Assert.Equal(3, progress.Values.Count);

        string tsvPath = doc.GetProperty("telemetryTsvPath").GetString()!;
        Assert.True(File.Exists(tsvPath));
        string[] lines = File.ReadAllLines(tsvPath);
        Assert.True(lines.Length >= 4);
        Assert.StartsWith("timestampUtc\telapsedS\trecipeState\tstepSequence\tloopIteration\texecutedDepositsCount", lines[0]);
    }

    [Fact]
    public async Task Wakes_immediately_on_completed_paused_aborted_and_fault_or_alarm_while_ignoring_sticky_alarms()
    {
        var time = new FakeTimeProvider();
        var sticky = new[] { new { chamber = "Main", message = "Old alarm" } };
        var newAlarm = new[] { new { chamber = "Main", message = "Old alarm" }, new { chamber = "Main", message = "Overpressure!" } };

        var wireAlarm = new ScriptedStatusWire();
        wireAlarm.Responses.Enqueue(StatusJson(stepSequence: 1, alarms: sticky));
        wireAlarm.Responses.Enqueue(StatusJson(stepSequence: 2, alarms: newAlarm));
        string rAlarm = await CreateTools(wireAlarm, time, _telemetryDir).WatchRecipe();
        Assert.Equal("fault_or_alarm", JsonDocument.Parse(rAlarm).RootElement.GetProperty("wakeReason").GetString());

        var wirePaused = new ScriptedStatusWire();
        wirePaused.Responses.Enqueue(StatusJson(state: 1));
        wirePaused.Responses.Enqueue(StatusJson(state: 2));
        string rPaused = await CreateTools(wirePaused, time, _telemetryDir).WatchRecipe();
        Assert.Equal("paused", JsonDocument.Parse(rPaused).RootElement.GetProperty("wakeReason").GetString());

        var wireAborted = new ScriptedStatusWire();
        wireAborted.Responses.Enqueue(StatusJson(lastOutcome: new { name = "R", state = "Complete", message = "Complete" }));
        wireAborted.Responses.Enqueue(StatusJson(lastOutcome: new { name = "R", state = "Idle", message = "Aborted" }));
        string rAborted = await CreateTools(wireAborted, time, _telemetryDir).WatchRecipe();
        Assert.Equal("aborted", JsonDocument.Parse(rAborted).RootElement.GetProperty("wakeReason").GetString());

        var wireDone = new ScriptedStatusWire();
        wireDone.Responses.Enqueue(StatusJson(running: true, state: 1));
        wireDone.Responses.Enqueue(StatusJson(running: false, state: 4, lastOutcome: new { name = "R", state = "Complete", message = "Complete" }));
        string rDone = await CreateTools(wireDone, time, _telemetryDir).WatchRecipe();
        Assert.Equal("completed", JsonDocument.Parse(rDone).RootElement.GetProperty("wakeReason").GetString());
    }

    [Fact]
    public async Task Wake_on_every_step_wakes_when_stepSequence_changes()
    {
        var wire = new ScriptedStatusWire();
        wire.Responses.Enqueue(StatusJson(stepSequence: 4));
        wire.Responses.Enqueue(StatusJson(stepSequence: 4));
        wire.Responses.Enqueue(StatusJson(stepSequence: 5));

        string r = await CreateTools(wire, new FakeTimeProvider(), _telemetryDir).WatchRecipe(wake_on_every_step: true);
        var doc = JsonDocument.Parse(r).RootElement;
        Assert.Equal("step_changed", doc.GetProperty("wakeReason").GetString());
        Assert.Equal(5, doc.GetProperty("stepSequence").GetInt64());
    }

    [Fact]
    public async Task Min_discharge_current_ma_skips_one_settling_sample_and_ignores_rf_and_non_deposit()
    {
        object[] lowPdc = [new { id = "PSU_C", voltageV = 100.0, currentMa = 5.0, powerW = 0.5, supportsCurrent = true, setCurrentMa = 50.0, setPowerW = 0.0 }];
        object[] lowRf = [new { id = "PSU_RF", voltageV = 0.0, currentMa = 0.0, powerW = 150.0, supportsCurrent = false, setCurrentMa = 0.0, setPowerW = 150.0 }];
        object[] okPdc = [new { id = "PSU_C", voltageV = 380.0, currentMa = 50.0, powerW = 19.0, supportsCurrent = true, setCurrentMa = 50.0, setPowerW = 0.0 }];

        var wire = new ScriptedStatusWire();
        wire.Responses.Enqueue(StatusJson(stepDesc: "pwait 2mT 120", stepSequence: 1, psus: lowPdc));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit SiO2 40", stepSequence: 2, psus: lowRf));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit Ru 30 Home", stepSequence: 3, psus: lowPdc));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit Ru 30 Home", stepSequence: 3, psus: okPdc));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit Ru 30 Home", stepSequence: 4, psus: lowPdc));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit Ru 30 Home", stepSequence: 4, psus: lowPdc));

        string r = await CreateTools(wire, new FakeTimeProvider(), _telemetryDir).WatchRecipe(min_discharge_current_ma: 20.0);
        Assert.Equal("discharge_drop", JsonDocument.Parse(r).RootElement.GetProperty("wakeReason").GetString());
        Assert.Equal(6, wire.Calls);
    }

    [Fact]
    public async Task Telemetry_drift_resets_baseline_on_setpoint_change_and_ignores_pwait_pressure()
    {
        object[] pwaitLow = [new { chamber = "Main", sensor = "pm", pressureTorr = 0.0005 }];
        object[] pwaitHigh = [new { chamber = "Main", sensor = "pm", pressureTorr = 0.0030 }];
        object[] depStableP = [new { chamber = "Main", sensor = "pm", pressureTorr = 0.0020 }];
        object[] depDriftP = [new { chamber = "Main", sensor = "pm", pressureTorr = 0.0025 }];
        object[] psu50 = [new { id = "PSU_C", voltageV = 380.0, currentMa = 50.0, powerW = 19.0, supportsCurrent = true, setCurrentMa = 50.0, setPowerW = 0.0 }];
        object[] psu300 = [new { id = "PSU_C", voltageV = 540.0, currentMa = 300.0, powerW = 162.0, supportsCurrent = true, setCurrentMa = 300.0, setPowerW = 0.0 }];

        var wire = new ScriptedStatusWire();
        wire.Responses.Enqueue(StatusJson(stepDesc: "pwait 2mT 120", stepSequence: 1, psus: psu50, pressures: pwaitLow));
        wire.Responses.Enqueue(StatusJson(stepDesc: "pwait 2mT 120", stepSequence: 1, psus: psu50, pressures: pwaitHigh));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit Ru 30 Home", stepSequence: 2, psus: psu50, pressures: depStableP));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit Ru 30 Home", stepSequence: 2, psus: psu50, pressures: depStableP));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit C 100 Home", stepSequence: 3, psus: psu300, pressures: depStableP));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit C 100 Home", stepSequence: 3, psus: psu300, pressures: depStableP));
        wire.Responses.Enqueue(StatusJson(stepDesc: "deposit C 100 Home", stepSequence: 3, psus: psu300, pressures: depDriftP));

        string r = await CreateTools(wire, new FakeTimeProvider(), _telemetryDir)
            .WatchRecipe(max_voltage_drift_pct: 5.0, max_pressure_drift_pct: 10.0);
        Assert.Equal("telemetry_drift", JsonDocument.Parse(r).RootElement.GetProperty("wakeReason").GetString());
        Assert.Equal(7, wire.Calls);
    }

    [Fact]
    public async Task Nominal_timeout_wakes_at_clamped_max_wait_seconds_and_cancellation_propagates()
    {
        var time = new FakeTimeProvider();
        var wire = new ScriptedStatusWire();
        wire.Responses.Enqueue(StatusJson(stepSequence: 1));

        string r = await CreateTools(wire, time, _telemetryDir).WatchRecipe(poll_interval_seconds: 30.0, max_wait_seconds: 60.0);
        var doc = JsonDocument.Parse(r).RootElement;
        Assert.Equal("nominal_timeout", doc.GetProperty("wakeReason").GetString());
        Assert.Equal(60.0, doc.GetProperty("elapsedSeconds").GetDouble(), 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateTools(wire, new FakeTimeProvider(), _telemetryDir).WatchRecipe(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Receipt_truncates_below_1024_bytes_with_truncated_flag_when_many_psus_chambers_and_loops()
    {
        var psus = Enumerable.Range(1, 10)
            .Select(i => new { id = $"PSU_LONG_CHANNEL_{i:D2}", voltageV = 400.0 + i, currentMa = 100.0 + i, powerW = 40.0 + i, supportsCurrent = true, setCurrentMa = 100.0, setPowerW = 0.0 })
            .ToArray();
        var pressures = Enumerable.Range(1, 8)
            .Select(i => new { chamber = $"CHAMBER_MODULE_{i:D2}", sensor = $"gauge_{i:D2}", pressureTorr = 0.002 })
            .ToArray();
        var loops = Enumerable.Range(1, 6).Select(i => new { iteration = i, total = 10 }).ToArray();

        var wire = new ScriptedStatusWire();
        wire.Responses.Enqueue(StatusJson(running: false, state: 4, stepDesc: "deposit PSU_LONG_CHANNEL_01 10",
            loopStack: loops, loopIter: 6, lastOutcome: new { name = "Big", state = "Complete" }, psus: psus, pressures: pressures));

        string r = await CreateTools(wire, new FakeTimeProvider(), _telemetryDir).WatchRecipe();
        Assert.True(Encoding.UTF8.GetByteCount(r) < 1024, $"Receipt bytes: {Encoding.UTF8.GetByteCount(r)}");
        var doc = JsonDocument.Parse(r).RootElement;
        Assert.True(doc.GetProperty("truncated").GetBoolean());
        Assert.Equal(3, doc.GetProperty("loopStack").GetArrayLength());
    }

    [Fact]
    public void ControlTools_resolves_through_McpServer_DI_and_exposes_watch_recipe_schema()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWireCaller>(new ScriptedStatusWire());
        services.AddSingleton(new McpBridgeConfig("127.0.0.1", 8026, "k", null));
        services.AddMcpServer().WithTools<ControlTools>();
        using var sp = services.BuildServiceProvider();
        var tools = sp.GetServices<McpServerTool>().ToList();
        var watch = Assert.Single(tools, t => t.ProtocolTool.Name == "watch_recipe");
        Assert.True(watch.ProtocolTool.InputSchema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("wake_after_deposits", out _));
        Assert.False(props.TryGetProperty("progress", out _));
        Assert.False(props.TryGetProperty("cancellationToken", out _));
    }

    [Theory]
    [InlineData(double.NaN, 10.0)]
    [InlineData(2.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 10.0)]
    public async Task WatchRecipe_rejects_non_finite_timers_with_McpException(double pollS, double maxWaitS)
    {
        var tools = CreateTools(new ScriptedStatusWire(), new FakeTimeProvider(), _telemetryDir);
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            tools.WatchRecipe(poll_interval_seconds: pollS, max_wait_seconds: maxWaitS));
        Assert.Contains("bad_request", ex.Message);
    }
}
