using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Core.Recipe;
using Xunit;

namespace CoreLogicTests;

/// <summary>Fault pause: a pausable step failure soft-holds the machine and waits for
/// Retry / Skip / Abort. Spec: docs/superpowers/specs/2026-09-04-fault-pause-design.md.</summary>
public class FaultPauseTests
{
    private static (CoreThread Thread, ChannelPool Pool) NewThread()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogUserRecipes = true;
        thread.Core.LogEmbeddedRecipes = true;
        thread.Core.IsUserRecipe = true;          // only user recipes pause
        return (thread, pool);
    }

    [Fact]
    public async Task Resolve_completes_the_pending_decision()
    {
        var core = NewThread().Thread.Core;
        var tcs = new TaskCompletionSource<FaultDecision>();
        core.SetPendingFaultDecision(tcs);
        Assert.True(core.IsFaultPaused);
        core.ResolveFault(FaultDecision.Skip);
        Assert.Equal(FaultDecision.Skip, await tcs.Task);
        Assert.False(core.IsFaultPaused);
    }

    [Fact]
    public void EndRecipeSlot_cancels_a_stale_decision()
    {
        var core = NewThread().Thread.Core;
        var tcs = new TaskCompletionSource<FaultDecision>();
        core.SetPendingFaultDecision(tcs);
        Assert.True(core.TryBeginRecipeSlot());
        core.EndRecipeSlot();
        Assert.True(tcs.Task.IsCanceled);
        Assert.False(core.IsFaultPaused);
    }

    [Fact]
    public void Default_watchdog_is_600_seconds()
        => Assert.Equal(600, NewThread().Thread.Core.FaultPauseTimeoutSeconds);

    [Fact]
    public void Dwell_mark_is_recorded()
    {
        var core = NewThread().Thread.Core;
        Assert.Null(core.LastDwell);
        core.NoteDwellStarted("Deposit Si", 100);
        Assert.NotNull(core.LastDwell);
        Assert.Equal(100, core.LastDwell!.Seconds);
        Assert.Equal("Deposit Si", core.LastDwell.Description);
    }

    private sealed class ScriptedExecutor
    {
        public readonly List<string> Calls = [];
        private readonly Dictionary<string, int> _failuresLeft = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Throw { get; set; } = new InvalidOperationException("supply not responding");
        /// <summary>Fail whatever line arrives, without having to predict the normalised form
        /// the interpreter hands to the executor (e.g. <c>pwait pm 50mT 30</c>).</summary>
        public bool FailEverything { get; set; }
        public ScriptedExecutor FailTimes(string step, int n) { _failuresLeft[step] = n; return this; }

        public Task<bool> Execute(string[] parts, CancellationToken ct)
        {
            string step = string.Join(' ', parts);
            Calls.Add(step);
            if (FailEverything)
            {
                if (Throw is not null) throw Throw;
                return Task.FromResult(false);
            }
            if (_failuresLeft.TryGetValue(step, out int left) && left > 0)
            {
                _failuresLeft[step] = left - 1;
                if (Throw is not null) throw Throw;
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }

    /// <summary>A stepper drive that records <see cref="BaseStepperDrive.AbortMotion"/> calls and,
    /// optionally, refuses to stop the way a drive with a dead link would.</summary>
    private sealed class RecordingDrive(string id, bool throws = false, Action? onAbort = null)
        : BaseStepperDrive(new BaseDeviceParams(id, 0, id))
    {
        public int AbortCalls;
        public override void AbortMotion()
        {
            AbortCalls++;
            onAbort?.Invoke();
            if (throws) throw new InvalidOperationException("drive not responding");
        }
    }

    private sealed class RecordingPsu(string id) : BasePowerSupply(new BaseDeviceParams(id, 0, id))
    {
        public readonly List<(string Param, float Value)> Setpoints = [];
        public bool OutputOffCalled;
        public override void ApplySetpoint(string parameter, float value) => Setpoints.Add((parameter, value));
        public override void OutputOff() { OutputOffCalled = true; IsOutputOn = false; }
    }

    private static void Register(Core core, DeviceType type, IBaseComponent device)
    {
        if (!core.hwElements.Item.TryGetValue(type, out var elements))
        {
            elements = [];
            core.hwElements.Item[type] = elements;
        }
        elements.Add(device.Params.ID, device);
    }

    /// <summary>Drains the GUI channel once: the step/event log lines AND the FaultPauseInfo the
    /// dialog would have been constructed from. One drain, because the channel is consumed.</summary>
    private static (List<string> Log, List<FaultPauseInfo> Pauses) DrainAll(ChannelPool pool)
    {
        var log = new List<string>();
        var pauses = new List<FaultPauseInfo>();
        while (pool.GUIReader!.TryRead(out var msg))
        {
            if (msg is ValMsg<string> { Value: not null } v) log.Add(v.Value);
            else if (msg is ValMsg<FaultPauseInfo> { Value: not null } f) pauses.Add(f.Value);
        }
        return (log, pauses);
    }

    private static List<string> DrainLog(ChannelPool pool) => DrainAll(pool).Log;

    private static async Task DecideWhenPaused(Core core, FaultDecision d, int times = 1)
    {
        for (int t = 0; t < times; t++)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(core.IsFaultPaused, "handler never paused");
            core.ResolveFault(d);
            await Task.Delay(20);   // let the handler consume it before we look for the next pause
        }
    }

    private static string HoldXml(params string[] lines) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<recipes>\n" +
        "  <recipe id=\"FAILURE_HOLD\" onStepFailure=\"continue\">\n" +
        string.Concat(lines.Select(l => $"    <l>{l}</l>\n")) +
        "  </recipe>\n</recipes>";

    private static EmbeddedRecipes LoadEmbedded(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        try { return new EmbeddedRecipes(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Retry_reruns_the_failed_step_and_the_run_completes()
    {
        var (thread, pool) = NewThread();
        var exec = new ScriptedExecutor().FailTimes("power PSU_A true", 1);
        thread._stepExecutor = exec.Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on\ndelay 0.01", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(["power PSU_A true", "power PSU_A true", "delay 0.01"], exec.Calls);
        var ev = Assert.Single(thread._pauseEvents);
        Assert.Equal("retry", ev.Decision);
        Assert.Equal("operator", ev.By);
        Assert.Contains("supply not responding", ev.Reason);
        Assert.Empty(thread._skippedSteps);
        var log = DrainLog(pool);
        Assert.Contains(log, l => l.StartsWith("STEP FAILED (paused): power PSU_A true"));
        Assert.Contains(log, l => l.StartsWith("FAULT DECISION: retry (operator"));
    }

    [Fact]
    public async Task Skip_continues_and_records_the_step()
    {
        var (thread, pool) = NewThread();
        var exec = new ScriptedExecutor().FailTimes("power PSU_A true", 99);
        thread._stepExecutor = exec.Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on\ndelay 0.01", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(["power PSU_A true", "delay 0.01"], exec.Calls);
        Assert.Equal("power PSU_A true", Assert.Single(thread._skippedSteps));
        Assert.Equal("skip", Assert.Single(thread._pauseEvents).Decision);
        Assert.Contains(DrainLog(pool), l => l.StartsWith("STEP SKIPPED by operator: power PSU_A true"));
    }

    [Fact]
    public async Task Abort_rethrows_so_the_recipe_fails_as_today()
    {
        var (thread, _) = NewThread();
        var exec = new ScriptedExecutor().FailTimes("power PSU_A true", 99);
        thread._stepExecutor = exec.Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Abort);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on\ndelay 0.01", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.False(ok);
        Assert.Equal(["power PSU_A true"], exec.Calls);          // never reached 'delay'
        Assert.Equal("abort", Assert.Single(thread._pauseEvents).Decision);
        Assert.Contains("supply not responding", thread.LastCommandErrorForTests);
    }

    [Fact]
    public async Task Watchdog_aborts_when_nobody_answers()
    {
        var (thread, pool) = NewThread();
        thread.Core.FaultPauseTimeoutSeconds = 1;
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on", new Interpreter(), CancellationToken.None);

        Assert.False(ok);
        var ev = Assert.Single(thread._pauseEvents);
        Assert.Equal("abort", ev.Decision);
        Assert.Equal("watchdog", ev.By);
        Assert.Contains(DrainLog(pool), l => l.StartsWith("FAULT DECISION: abort (watchdog"));
        Assert.False(thread.Core.IsFaultPaused);
    }

    [Fact]
    public async Task Timeout_zero_disables_pausing()
    {
        var (thread, _) = NewThread();
        thread.Core.FaultPauseTimeoutSeconds = 0;
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on", new Interpreter(), CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(thread._pauseEvents);
        Assert.False(thread.Core.IsFaultPaused);
    }

    [Fact]
    public async Task Plc_comm_failure_never_pauses()
    {
        var (thread, _) = NewThread();
        thread._stepExecutor = new ScriptedExecutor { Throw = new PlcCommException(0x01, "read") }
            .FailTimes("valve V1 true", 99).Execute;

        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "valve V1 open", new Interpreter(), CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(thread._pauseEvents);
    }

    [Fact]
    public async Task Embedded_recipes_never_pause()
    {
        var (thread, _) = NewThread();
        thread.Core.IsUserRecipe = false;
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on", new Interpreter(), CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(thread._pauseEvents);
    }

    [Fact]
    public async Task Continue_on_error_runs_never_pause()
    {
        var (thread, _) = NewThread();
        thread._continueOnError = true;
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on", new Interpreter(), CancellationToken.None);

        Assert.True(ok);                                   // continue-on-error swallows
        Assert.Empty(thread._pauseEvents);
        thread._continueOnError = false;
    }

    [Fact]
    public async Task Operator_stop_during_pause_propagates_cancellation()
    {
        var (thread, _) = NewThread();
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;
        using var cts = new CancellationTokenSource();

        var run = thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(thread.Core.IsFaultPaused);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var ev = Assert.Single(thread._pauseEvents);
        Assert.Equal("abort", ev.Decision);
        Assert.Equal("operator-stop", ev.By);
        Assert.False(thread.Core.IsFaultPaused);
    }

    [Fact]
    public async Task Hold_runs_FAILURE_HOLD_and_zeroes_the_failed_supply_before_pausing()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
        var failed = new RecordingPsu("PSU_A") { Connected = true };
        var other = new RecordingPsu("RF601") { Connected = true };
        Register(thread.Core, DeviceType.Power, failed);
        Register(thread.Core, DeviceType.Power, other);
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.Contains(("Power", 0f), failed.Setpoints);
        Assert.True(failed.OutputOffCalled);
        Assert.Empty(other.Setpoints);
        Assert.False(other.OutputOffCalled);

        var log = DrainLog(pool);
        int hold = log.FindIndex(l => l.Contains("exec: shutter Home"));
        int paused = log.FindIndex(l => l.StartsWith("STEP FAILED (paused)"));
        Assert.True(hold >= 0, "FAILURE_HOLD did not run");
        Assert.True(paused > hold, "pause was reported before the hold ran");
        Assert.DoesNotContain(log, l => l.Contains("FAULT DECISION") && l.Contains("shutter"));  // hold's own failure never paused
    }

    [Fact]
    public async Task Missing_FAILURE_HOLD_still_pauses()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml());    // empty recipe body == "no hold"
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        bool ok = await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Contains(DrainLog(pool), l => l.Contains("no FAILURE_HOLD"));
        Assert.Single(thread._pauseEvents);
    }

    [Fact]
    public async Task Hold_does_not_corrupt_the_outer_recipes_progress_bookkeeping()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home", "valve V1 close", "valve V2 close"));
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 1).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on\ndelay 0.01\ndelay 0.01\ndelay 0.01", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(4, thread.RecipeTotalStepsForTests);     // not the hold's 3
        Assert.False(thread._continueOnError);                 // hold's flag never leaked
    }

    [Fact]
    public async Task Retry_that_fails_again_pauses_again()
    {
        var (thread, _) = NewThread();
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 2).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry, times: 2);
        bool ok = await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(2, thread._pauseEvents.Count);
    }

    [Theory]
    [InlineData("power PSU_A true", true)]
    [InlineData("set PSU_A Power 100", true)]
    [InlineData("MFC Ar1 30", true)]
    [InlineData("valve V1 true", true)]
    [InlineData("delay 5", false)]
    [InlineData("holder Si", false)]
    public void IsEnergyStep_flags_power_gas_and_valve_lines(string line, bool expected)
        => Assert.Equal(expected, CoreThread.IsEnergyStep(line.Split(' ')));

    /// <summary>An interlock block must ABORT, never pause — the machine's state can no longer
    /// be trusted. The step returns false without throwing, so the only marker the handler has
    /// is the reason ExecuteRecipeLine recorded.</summary>
    [Fact]
    public async Task Interlock_block_without_an_exception_never_pauses()
    {
        var (thread, _) = NewThread();
        thread._stepExecutor = new ScriptedExecutor { Throw = null }.FailTimes("valve V1 true", 99).Execute;
        thread.LastCommandErrorForTests =
            "Recipe step 'valve V1 true' blocked by interlock on GVL closed";

        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "valve V1 open", new Interpreter(), CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(thread._pauseEvents);
        Assert.False(thread.Core.IsFaultPaused);
    }

    [Fact]
    public async Task Failure_without_an_exception_pauses_with_the_drivers_reason()
    {
        var (thread, _) = NewThread();
        thread._stepExecutor = new ScriptedExecutor { Throw = null }.FailTimes("power PSU_A true", 99).Execute;
        thread.LastCommandErrorForTests = "command 'power PSU_A true' returned no result";

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal("command 'power PSU_A true' returned no result",
            Assert.Single(thread._pauseEvents).Reason);
    }

    /// <summary>Operation messages are authored with a leading '\n', and FirstLine cuts at the
    /// first newline BEFORE trimming.</summary>
    [Fact]
    public async Task Newline_prefixed_fault_message_still_names_the_cause()
    {
        var (thread, _) = NewThread();
        thread._stepExecutor = new ScriptedExecutor
        {
            Throw = new InvalidOperationException("\nBound device 'X' not found")
        }.FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.Contains("Bound device", Assert.Single(thread._pauseEvents).Reason);
    }

    /// <summary>The pause deposits nothing (the hold closes the shutters), so only the time
    /// between the dwell starting and the step FAILING may be subtracted — never the operator's
    /// deliberation.</summary>
    [Fact]
    public void AdjustForResume_subtracts_the_dwell_that_ran_not_the_pause()
    {
        var (thread, _) = NewThread();
        var t0 = DateTime.UtcNow;
        thread.Core.LastDwell = new DwellMark("Deposit Si", t0, 100);

        string[] adjusted = thread.AdjustForResume(
            ["deposit", "Si", "100"],
            attemptStartedUtc: t0.AddSeconds(-1),
            failedAtUtc: t0.AddSeconds(10));

        Assert.Equal(["deposit", "Si", "90"], adjusted);
    }

    [Fact]
    public void AdjustForResume_leaves_the_line_alone_without_a_dwell_mark()
    {
        var (thread, _) = NewThread();
        thread.Core.LastDwell = null;
        string[] parts = ["deposit", "Si", "100"];

        Assert.Same(parts, thread.AdjustForResume(parts, DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void AdjustForResume_ignores_a_dwell_from_an_earlier_attempt()
    {
        var (thread, _) = NewThread();
        var t0 = DateTime.UtcNow;
        thread.Core.LastDwell = new DwellMark("Deposit Si", t0, 100);   // began before this attempt
        string[] parts = ["deposit", "Si", "100"];

        Assert.Same(parts, thread.AdjustForResume(parts,
            attemptStartedUtc: t0.AddSeconds(5), failedAtUtc: t0.AddSeconds(15)));
    }

    [Fact]
    public void AdjustForResume_leaves_a_non_deposit_line_alone()
    {
        var (thread, _) = NewThread();
        var t0 = DateTime.UtcNow;
        thread.Core.LastDwell = new DwellMark("Deposit Si", t0, 100);
        string[] parts = ["set", "PSU_A", "Power"];

        Assert.Same(parts, thread.AdjustForResume(parts, t0.AddSeconds(-1), t0.AddSeconds(10)));
    }

    [Fact]
    public async Task Retry_of_an_interrupted_deposit_carries_the_remaining_dwell()
    {
        var (thread, pool) = NewThread();
        var calls = new List<string>();
        int attempt = 0;
        thread._stepExecutor = async (parts, ct) =>
        {
            calls.Add(string.Join(' ', parts));
            if (parts[0] == "deposit" && attempt++ == 0)
            {
                // The worker reported the dwell start, then the supply dropped out 2 s in.
                thread.Core.NoteDwellStarted("Deposit Si", 100);
                await Task.Delay(2000, ct);
                throw new InvalidOperationException("PSU_A stopped responding mid-dwell");
            }
            return true;
        };

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(2, calls.Count);
        Assert.Equal("deposit Si 100 Home", calls[0]);
        string[] retried = calls[1].Split(' ');
        double remaining = double.Parse(retried[2], System.Globalization.CultureInfo.InvariantCulture);
        // The dwell is measured up to the FAILURE instant, not up to the retry decision,
        // so none of DecideWhenPaused's polling latency is subtracted here.
        Assert.InRange(remaining, 96, 98.5);     // 100 − ~2 s, never the full 100
        Assert.Equal("Home", retried[3]);
        Assert.Contains(DrainLog(pool), l => l.StartsWith("Deposit resumed with"));
    }

    /// <summary>A queued deposit program runs ON the drive and sets its flange bookkeeping at
    /// queue-load time, so the hold's own <c>shutter Home</c> would be skipped as redundant and
    /// send nothing. The hold must abort drive motion FIRST.</summary>
    [Fact]
    public async Task Hold_aborts_connected_drives_before_the_hold_script_runs()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
        var drive = new RecordingDrive("Shutter") { Connected = true };
        Register(thread.Core, DeviceType.Drive, drive);
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.Equal(1, drive.AbortCalls);
        var log = DrainLog(pool);
        int stopped = log.FindIndex(l => l.Contains("drives stopped") && l.Contains("Shutter"));
        int hold = log.FindIndex(l => l.Contains("exec: shutter Home"));
        Assert.True(stopped >= 0, "no drive-abort line was logged");
        Assert.True(hold > stopped, "the hold script ran before the drives were stopped");
    }

    /// <summary>A drive that will not stop is exactly what the operator must be told about
    /// before choosing Retry — the pause must still happen, and the dialog must name it.</summary>
    [Fact]
    public async Task Hold_reports_a_drive_that_refuses_to_stop_and_still_pauses()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
        var bad = new RecordingDrive("Shutter", throws: true) { Connected = true };
        var good = new RecordingDrive("Holder") { Connected = true };
        Register(thread.Core, DeviceType.Drive, bad);
        Register(thread.Core, DeviceType.Drive, good);
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        bool ok = await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Single(thread._pauseEvents);
        Assert.Equal(1, good.AbortCalls);                       // one bad drive does not stop the sweep
        var (log, pauses) = DrainAll(pool);
        var hold = Assert.Single(pauses).Hold;
        Assert.Contains(hold.Problems, p => p.Contains("could not stop drive Shutter"));
        Assert.Contains(hold.Done, d => d.Contains("Holder"));
        Assert.Contains(log, l => l.StartsWith("HOLD INCOMPLETE: could not stop drive Shutter"));
    }

    [Fact]
    public async Task Hold_skips_a_disconnected_drive()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
        var drive = new RecordingDrive("Shutter") { Connected = false };
        Register(thread.Core, DeviceType.Drive, drive);
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.Equal(0, drive.AbortCalls);
    }

    /// <summary>A profile with an empty FAILURE_HOLD body runs nothing, so the dialog must not claim
    /// the shutters closed or the failed supply de-energised.</summary>
    [Fact]
    public async Task No_FAILURE_HOLD_is_reported_as_nothing_closed()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml());        // empty body == no hold
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        var hold = Assert.Single(DrainAll(pool).Pauses).Hold;
        Assert.Contains(hold.Problems, p => p.Contains("no FAILURE_HOLD") && p.Contains("nothing was closed"));
        Assert.DoesNotContain(hold.Done, d => d.Contains("FAILURE_HOLD ran"));
        Assert.DoesNotContain(hold.Done, d => d.Contains("Shutters closed"));
    }

    /// <summary>Most pausable failures (deposit, pwait, holder, delay, state) name no supply, so
    /// nothing is de-energised — the dialog must say that rather than imply the plasma is off.</summary>
    [Fact]
    public async Task A_failed_line_naming_no_supply_is_reported_as_nothing_de_energised()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
        thread._stepExecutor = new ScriptedExecutor { FailEverything = true }.Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("pwait pm 50mT 30", new Interpreter(), CancellationToken.None);
        await decide;

        var hold = Assert.Single(DrainAll(pool).Pauses).Hold;
        Assert.Contains(hold.Done, d => d.Contains("no power supply") && d.Contains("nothing was de-energised"));
        Assert.Contains(hold.Done, d => d.Contains("FAILURE_HOLD ran"));
        Assert.DoesNotContain(hold.Done, d => d.Contains("setpoint 0"));
    }

    /// <summary>The same failed line — naming no supply — but the HOLD itself de-energises one.
    /// The "nothing was de-energised" claim is decided by the unresolved-psuId branch, which runs
    /// BEFORE the sweep that finds what FAILURE_HOLD killed; unguarded, the operator is handed a
    /// report that says nothing was de-energised beside a line saying a supply was. The claim is
    /// about the whole hold, so it may only be made once the whole hold is known.</summary>
    [Fact]
    public async Task A_line_naming_no_supply_never_claims_nothing_when_the_hold_de_energised_one()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
        var psu = new RecordingPsu("X");
        psu.OutputOn();
        Register(thread.Core, DeviceType.Power, psu);
        // Stands in for a FAILURE_HOLD line that kills a supply: it runs inside the hold, after
        // the pre-hold snapshot, on a supply the failed line never named.
        Register(thread.Core, DeviceType.Drive,
            new RecordingDrive("Shutter", onAbort: psu.OutputOff) { Connected = true });

        Assert.True(thread.Core.TryBeginRecipeSlot());
        HoldReport hold;
        try { hold = await thread.RunFailureHold(["pwait", "pm", "50mT", "30"], null); }
        finally { thread.Core.EndRecipeSlot(); }

        Assert.Contains(hold.Zeroed, z => z.PsuId == "X");
        Assert.Contains(hold.Done, d => d.Contains("X output off"));
        Assert.DoesNotContain(hold.Done, d => d.Contains("nothing was de-energised"));
        // That the failed line resolved no supply still has to reach the operator.
        Assert.Contains(hold.Done, d => d.Contains("no power supply resolved from the failed line"));
    }

    /// <summary>Attribution. The sweep sees a before/after diff, and a diff is not a cause: on
    /// RfPsuA IsOutputOn comes from the polled 'Q' status bit, so an arc trip during the hold
    /// window is byte-for-byte a hold kill. A FAILURE_HOLD of one
    /// <c>shutter Home</c> de-energises nothing, so blaming the hold unconditionally named the
    /// software as the cause of every hardware fault this machine can raise. Nothing in the hold
    /// names the supply → say so, and warn that a Retry will still command it back on.</summary>
    [Fact]
    public async Task A_supply_that_drops_during_a_hold_that_names_it_nowhere_is_not_blamed_on_the_hold()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));   // de-energises nothing
        var psu = new RecordingPsu("RF301");
        psu.OutputOn();
        Register(thread.Core, DeviceType.Power, psu);
        Register(thread.Core, DeviceType.Drive,
            new RecordingDrive("Shutter", onAbort: psu.OutputOff) { Connected = true });

        Assert.True(thread.Core.TryBeginRecipeSlot());
        HoldReport hold;
        try { hold = await thread.RunFailureHold(["valve", "V1", "true"], null); }
        finally { thread.Core.EndRecipeSlot(); }

        Assert.Contains(hold.Zeroed, z => z.PsuId == "RF301");            // still armed
        Assert.DoesNotContain(hold.Done,
            d => d.Contains("RF301") && d.Contains("de-energised by FAILURE_HOLD"));
        Assert.Contains(hold.Done, d => d.Contains("RF301") && d.Contains("went off during the hold"));
        Assert.Contains(hold.Problems,
            p => p.Contains("RF301") && p.Contains("may have tripped on its own")
              && p.Contains("Retry WILL command it back on"));
    }

    /// <summary>The other half: a hold whose script really does name and kill the supply IS the
    /// cause, says so, and raises no "it may have tripped" doubt.</summary>
    [Fact]
    public async Task A_supply_the_hold_script_names_and_kills_is_attributed_to_the_hold()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home", "power RF301 off"));
        var psu = new RecordingPsu("RF301");
        psu.OutputOn();
        Register(thread.Core, DeviceType.Power, psu);
        Register(thread.Core, DeviceType.Drive,
            new RecordingDrive("Shutter", onAbort: psu.OutputOff) { Connected = true });

        Assert.True(thread.Core.TryBeginRecipeSlot());
        HoldReport hold;
        try { hold = await thread.RunFailureHold(["valve", "V1", "true"], null); }
        finally { thread.Core.EndRecipeSlot(); }

        Assert.Contains(hold.Zeroed, z => z.PsuId == "RF301");
        Assert.Contains(hold.Done,
            d => d.Contains("RF301") && d.Contains("de-energised by FAILURE_HOLD"));
        Assert.DoesNotContain(hold.Problems, p => p.Contains("may have tripped on its own"));
    }

    /// <summary>A hold with a long wait in it must not make Stop and app exit unresponsive:
    /// the WAIT is bounded, the operator is paused in, and the overrun is reported.</summary>
    [Fact]
    public async Task A_hold_that_overruns_its_budget_is_reported_and_the_pause_still_happens()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("delay 1"));
        thread._holdBudget = TimeSpan.FromMilliseconds(100);
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        bool ok = await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Single(thread._pauseEvents);
        var hold = Assert.Single(DrainAll(pool).Pauses).Hold;
        Assert.Contains(hold.Problems, p => p.StartsWith("hold did not finish within"));
    }

    /// <summary>A hold's LATER lines run after the handler has restored the outer recipe's
    /// bookkeeping. Routed into ExecuteWithFaultPause they would start a nested pause and cancel
    /// the operator's own pending decision — the dialog vanishes and the run aborts as a Stop
    /// nobody pressed. The hold's line must stay inside the hold: recorded, never paused.</summary>
    [Fact]
    public async Task A_hold_line_after_the_overrun_never_pauses_and_never_cancels_the_decision()
    {
        var (thread, pool) = NewThread();
        // Two lines: the first outruns the budget, the second runs after the restore.
        thread.Core.embRecipes = LoadEmbedded(HoldXml("delay 0.6", "shutter Home"));
        thread._holdBudget = TimeSpan.FromMilliseconds(50);
        // Deliberately NO _stepExecutor seam: both the outer line and the hold's lines go
        // through the real executor and fail on their own (no devices are registered), which is
        // the production shape. With the seam installed, the outer finally restores it and the
        // late hold line would run the TEST executor and quietly succeed — hiding the defect.
        // A short watchdog so that, if a nested pause ever starts again, this test ends in
        // seconds with a second PauseEvent rather than hanging for ten minutes.
        thread.Core.FaultPauseTimeoutSeconds = 2;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        bool ok = await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        var ev = Assert.Single(thread._pauseEvents);            // exactly one pause, not two
        Assert.Equal("skip", ev.Decision);
        Assert.Equal("operator", ev.By);                        // NOT "operator-stop"
        var log = DrainLog(pool);
        // The late hold line failed inside the hold and was recorded there...
        Assert.Contains(log, l => l.Contains("STEP FAILED (continuing): shutter Home"));
        // ...and never became a pause of its own.
        Assert.DoesNotContain(log, l => l.Contains("STEP FAILED (paused): shutter Home"));
        // The decision was acted on only after the hold had finished commanding the machine.
        Assert.Contains(log, l => l.Contains("waiting for the unfinished hold"));
    }

    /// <summary>...and the outer recipe's own failures still pause normally once the hold that
    /// outran its budget has ended. (A fix that simply left the hold's flags set would make the
    /// resumed recipe swallow its own failures instead.)</summary>
    [Fact]
    public async Task The_outer_recipe_still_pauses_after_an_overrunning_hold()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("delay 0.3", "shutter Home"));
        thread._holdBudget = TimeSpan.FromMilliseconds(50);
        thread._stepExecutor = new ScriptedExecutor()
            .FailTimes("power PSU_A true", 99)
            .FailTimes("power RF601 true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip, times: 2);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on\npower RF601 on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(2, thread._pauseEvents.Count);
        Assert.False(thread._continueOnError);                  // the hold's flag never leaked
    }

    /// <summary>
    /// A hold that outlives BOTH bounds, so the recipe resumes while it is still running and a
    /// second failure starts a second, concurrent hold. A shared flag would be cleared by hold
    /// #1's exit under hold #2, and the resumed recipe's own failure would be routed into the
    /// hold's sink instead of pausing. Each hold must carry its own flow-scoped flag: the second
    /// failure must pause like the first.
    /// </summary>
    [Fact]
    public async Task A_second_failure_still_pauses_while_the_first_hold_is_still_running()
    {
        var (thread, _) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("delay 1"));   // outlives both bounds
        thread._holdBudget = TimeSpan.FromMilliseconds(50);
        thread._holdDrainBudget = TimeSpan.FromMilliseconds(50);
        thread.Core.FaultPauseTimeoutSeconds = 2;                     // no seam: production shape

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip, times: 2);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "power PSU_A on\npower RF601 on", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Equal(2, thread._pauseEvents.Count);
        Assert.Equal(["power PSU_A true", "power RF601 true"],
            thread._skippedSteps);              // the second failure paused, it was not swallowed
    }

    [Fact]
    public async Task Hold_failures_recorded_before_the_overrun_reach_the_dialog()
    {
        var (thread, pool) = NewThread();
        thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home", "delay 1"));
        thread._holdBudget = TimeSpan.FromMilliseconds(100);
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        var hold = Assert.Single(DrainAll(pool).Pauses).Hold;
        Assert.Contains(hold.Problems, p => p.StartsWith("hold did not finish within"));
        Assert.Contains(hold.Problems, p => p.Contains("shutter Home"));
    }

    /// <summary>A clean hold names its actions and carries no problems — the only case in which
    /// the operator may read the dialog as "the machine is held".</summary>
    [Fact]
    public async Task A_clean_hold_reports_its_actions_and_no_problems()
    {
        var (thread, pool) = NewThread();
        // `delay`, not `shutter Home`: there is no drive worker in a unit run, so a real hold
        // line fails here and would (correctly) produce a problem note.
        thread.Core.embRecipes = LoadEmbedded(HoldXml("delay 0.01"));
        var drive = new RecordingDrive("Shutter") { Connected = true };
        Register(thread.Core, DeviceType.Drive, drive);
        Register(thread.Core, DeviceType.Power, new RecordingPsu("PSU_A") { Connected = true });
        thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
        await thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), CancellationToken.None);
        await decide;

        var hold = Assert.Single(DrainAll(pool).Pauses).Hold;
        Assert.Contains(hold.Done, d => d.Contains("drive motion aborted: Shutter"));
        Assert.Contains(hold.Done, d => d.Contains("FAILURE_HOLD ran"));
        Assert.Contains(hold.Done, d => d.Contains("PSU_A setpoint 0, output off"));
        Assert.Empty(hold.Problems);
    }

    [Fact]
    public void An_absurd_watchdog_is_clamped_to_24_hours_and_logged()
    {
        var (thread, pool) = NewThread();
        thread.Core.ApplyFaultPauseTimeout(int.MaxValue);

        Assert.Equal(86_400, thread.Core.FaultPauseTimeoutSeconds);
        Assert.Contains(DrainLog(pool), l => l.Contains("out of range") && l.Contains("clamped to 86400 s"));
    }

    [Fact]
    public void A_negative_watchdog_is_clamped_to_zero_and_logged()
    {
        var (thread, pool) = NewThread();
        thread.Core.ApplyFaultPauseTimeout(-5);

        Assert.Equal(0, thread.Core.FaultPauseTimeoutSeconds);
        var log = DrainLog(pool);
        Assert.Contains(log, l => l.Contains("out of range") && l.Contains("clamped to 0 s"));
        Assert.Contains(log, l => l.Contains("Fault pause disabled"));
    }

    [Fact]
    public void An_in_range_watchdog_is_applied_without_a_clamp_line()
    {
        var (thread, pool) = NewThread();
        thread.Core.ApplyFaultPauseTimeout(120);

        Assert.Equal(120, thread.Core.FaultPauseTimeoutSeconds);
        var log = DrainLog(pool);
        Assert.DoesNotContain(log, l => l.Contains("clamped"));
        Assert.Contains(log, l => l.Contains("Fault pause watchdog: 120 s"));
    }

    [Fact]
    public async Task Retry_of_a_deposit_that_never_started_its_dwell_reruns_it_unchanged()
    {
        var (thread, _) = NewThread();
        var exec = new ScriptedExecutor().FailTimes("deposit Si 100 Home", 1);
        thread._stepExecutor = exec.Execute;
        // holder move failed before the shutter opened: no dwell mark was recorded

        var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
        bool ok = await thread.ExecuteRecipeWithInterpreter(
            "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
        await decide;

        Assert.True(ok);
        Assert.Null(thread.Core.LastDwell);
        // second attempt used the full dwell (the scripted executor recorded both calls)
        Assert.Equal(["deposit Si 100 Home", "deposit Si 100 Home"], exec.Calls);
    }
}
