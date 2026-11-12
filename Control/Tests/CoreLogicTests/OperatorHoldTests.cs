using System.Reflection;
using APS.Controls.Core;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.Diagnostics;
using APS.SystemControl.Core.Recipe;
using APS.SystemControl.Core.Remote.Agent;
using CoreLogicTests.Fixtures;
using APS.SystemControl.Forms;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Hold — the fourth fault-pause decision. A fault pause keeps <c>IsRecipeRunning</c> true for its
/// whole duration, so the dialog asked the operator to fix a supply and then refused them every
/// tool for fixing it: no SetPower, no OutputOn, no ClearFault, no jog. Hold closes the dialog,
/// disarms the watchdog and PARKS the recipe thread exactly where it stands — nothing is unwound,
/// no step re-runs, the soft hold's actions are not undone — while the operator works on the
/// machine; then it waits, with no deadline, for Resume (Retry), Skip or Abort.
/// <para>A hold never auto-aborts. <c>heldWarningSeconds</c> is therefore the only thing that
/// makes an unattended one visible — whoever began it, operator or agent.</para>
/// </summary>
public class OperatorHoldTests
{
    /// <summary>Claims the single recipe slot before returning, modelling what the real entry
    /// point (<c>ExecuteRecipe</c>) does before it ever calls <c>ExecuteRecipeWithInterpreter</c>
    /// — the method every test here calls directly. Without it <c>Core.IsRecipeRunning</c> is
    /// false for the lifetime of the test and <c>IsManualControlGated</c> reads false whether the
    /// machine is held or not, which would make the gating assertions meaningless. Callers MUST
    /// release the slot (<c>core.EndRecipeSlot()</c>) or it leaks into the next test.</summary>
    private static (CoreThread Thread, ChannelPool Pool) NewThread()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogUserRecipes = true;
        thread.Core.LogEmbeddedRecipes = true;
        thread.Core.IsUserRecipe = true;          // only user recipes pause
        thread.Core.TryBeginRecipeSlot();
        return (thread, pool);
    }

    /// <summary>A fake step executor: fails the listed step N times, then succeeds. Steps not
    /// listed succeed. Records every call so tests can see re-runs — a hold must produce
    /// none.</summary>
    private sealed class ScriptedExecutor
    {
        public readonly List<string> Calls = [];
        private readonly Dictionary<string, int> _failuresLeft = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Throw { get; set; } = new InvalidOperationException("supply not responding");
        public ScriptedExecutor FailTimes(string step, int n) { _failuresLeft[step] = n; return this; }

        public Task<bool> Execute(string[] parts, CancellationToken ct)
        {
            string step = string.Join(' ', parts);
            Calls.Add(step);
            if (_failuresLeft.TryGetValue(step, out int left) && left > 0)
            {
                _failuresLeft[step] = left - 1;
                if (Throw is not null) throw Throw;
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
    }

    /// <summary>A stepper drive that records <see cref="BaseStepperDrive.AbortMotion"/> calls, so
    /// a test can see whether the soft hold was re-run.</summary>
    private sealed class RecordingDrive(string id) : BaseStepperDrive(new BaseDeviceParams(id, 0, id))
    {
        public int AbortCalls;
        public readonly List<string> Commands = [];
        public override void AbortMotion() => AbortCalls++;
        public override void ProcessCommand(string command, object[] args) => Commands.Add(command);
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

    /// <summary>Drains the GUI channel once: the step/event log lines AND every FaultPauseInfo
    /// the dialog would have been constructed from. One drain, because the channel is consumed.
    /// The pause list is what Ruling C is tested against: a held wait must add nothing to it.
    /// </summary>
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

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), what);
    }

    private static Task WaitUntilPaused(Core core) => WaitUntil(() => core.IsFaultPaused, "handler never paused");

    /// <summary>Waits for the pause, answers Hold, and waits until the HELD wait is actually
    /// listening again.
    /// <para>That last step is not belt-and-braces. <c>SetHeldForOperator(true)</c> runs in the
    /// decision loop, BEFORE the loop re-enters <c>WaitForFaultDecisionAsync</c> and installs the
    /// hold's own <c>TaskCompletionSource</c>. A test that resolved on
    /// <see cref="Core.IsHeldForOperator"/> alone would race that window, drop its decision into
    /// a <c>ResolveFault</c> with nothing pending — exactly the no-op the production seam is
    /// specified to be — and then wait forever for a run that is never answered.</para></summary>
    private static async Task HoldWhenPaused(Core core)
    {
        await WaitUntilPaused(core);
        core.ResolveFault(FaultDecision.Hold);
        await WaitUntil(() => core.IsHeldForOperator, "the hold was never entered");
        await WaitUntil(() => core.IsFaultPaused, "the held wait never installed its decision");
    }

    /// <summary>Answers Hold on a pause that is ALREADY held, and waits until the next held wait
    /// is listening. Cannot resolve on <c>IsHeldForOperator</c> (already true) or on
    /// <c>IsFaultPaused</c> alone (still true until the decision is taken), so it steps through
    /// the pause-event count: the loop appends the event AFTER the wait's finally has cleared the
    /// pending decision, and installs the next one after that.</summary>
    private static async Task HoldAgain(CoreThread thread, int expectedEventsAfter)
    {
        thread.Core.ResolveFault(FaultDecision.Hold);
        await WaitUntil(() => thread._pauseEvents.Count >= expectedEventsAfter,
            "the second hold decision was never taken");
        await WaitUntil(() => thread.Core.IsFaultPaused, "the second held wait never installed its decision");
        Assert.True(thread.Core.IsHeldForOperator, "the second hold did not re-enter the held state");
    }

    /// <summary>A held pause has NO watchdog by design, so a regression that loses the decision
    /// would hang the whole test run instead of failing it. Every test that holds awaits the run
    /// through this ceiling: a lost decision surfaces as a TimeoutException in 30 s.</summary>
    private static Task<bool> WithCeiling(Task<bool> run) => run.WaitAsync(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Hold_closes_the_dialog_and_unlocks_manual_control()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var gated = new List<bool>();
            var decide = Task.Run(async () =>
            {
                await WaitUntilPaused(thread.Core);
                gated.Add(thread.Core.IsManualControlGated);      // true: paused, but not held yet
                await HoldWhenPaused(thread.Core);
                gated.Add(thread.Core.IsManualControlGated);      // false: the machine is theirs
                thread.Core.ResolveFault(FaultDecision.Abort);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.False(ok);                                     // the run ended on Abort
            Assert.Equal([true, false], gated);
            Assert.False(thread.Core.IsHeldForOperator);          // cleared on the way out
            Assert.Equal(["hold", "abort"], thread._pauseEvents.Select(e => e.Decision));
            var (log, _) = DrainAll(pool);
            Assert.Contains(log, l => l.StartsWith("FAULT DECISION: hold (operator"));
            Assert.Contains(log, l => l.Contains("HELD on: power PSU_A true"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>RULING C. The dialog must close ONCE and stay closed. The held wait re-enters
    /// <c>WaitForFaultDecisionAsync</c> on a fresh decision source, and that method posts the
    /// pause to the UI unconditionally — so without the held carve-out it re-opens the very
    /// dialog Hold just closed, and the operator is locked out of the machine again while being
    /// asked, once more, to go and fix it.</summary>
    [Fact]
    public async Task A_held_wait_never_re_opens_the_dialog()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);
            var (_, pauses) = DrainAll(pool);
            Assert.Single(pauses);                                // ONE dialog, for two waits
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The watchdog is what Hold exists to disarm: aborting the run out from under an
    /// operator who is mid-repair — because they took longer than the pause deadline — is the
    /// exact failure this feature prevents.</summary>
    [Fact]
    public async Task A_held_pause_never_fires_the_watchdog()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 1;             // would abort in 1 s if armed
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                await Task.Delay(2500);                           // 2.5x the watchdog
                Assert.True(thread.Core.IsHeldForOperator, "the watchdog ended the hold");
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);                                      // Skip, not a watchdog Abort
            Assert.Equal(["hold", "skip"], thread._pauseEvents.Select(e => e.Decision));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>A hold parks the thread; it does not unwind it. The failed line must not re-run,
    /// and the soft hold must not run a second time — it has already made the machine safe, and
    /// re-aborting the drives while the operator is driving them by hand is precisely the
    /// collision the ordering of this branch avoids.</summary>
    [Fact]
    public async Task A_hold_re_runs_neither_the_step_nor_the_soft_hold()
    {
        var (thread, _) = NewThread();
        try
        {
            var drive = new RecordingDrive("Shutter") { Connected = true };
            Register(thread.Core, DeviceType.Drive, drive);
            var exec = new ScriptedExecutor().FailTimes("power PSU_A true", 99);
            thread._stepExecutor = exec.Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.Equal(["power PSU_A true"], exec.Calls);     // parked, not re-run
            Assert.Equal(1, drive.AbortCalls);                    // one soft hold, not two
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The fault-handling window stays OPEN across a hold. A panel power-OFF landing
    /// while the operator has the machine must be armed into the restoration set exactly like one
    /// landing while the dialog was up, and anything reporting what a press is about to do must
    /// not tell the operator it is "pausing" a recipe that is already paused.</summary>
    [Fact]
    public async Task The_run_is_still_handling_a_step_failure_while_held()
    {
        var (thread, _) = NewThread();
        try
        {
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            bool handlingWhileHeld = false;
            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                handlingWhileHeld = thread.Core.IsHandlingStepFailure;
                thread.Core.ResolveFault(FaultDecision.Abort);
            });

            await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(handlingWhileHeld, "the fault-handling window closed during the hold");
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Resume after a hold: the run picks up where it was parked and the step re-runs
    /// once, so a hold really is a deferral rather than a decision.</summary>
    [Fact]
    public async Task Resume_after_a_hold_re_runs_the_step_and_the_run_completes()
    {
        var (thread, _) = NewThread();
        try
        {
            var exec = new ScriptedExecutor().FailTimes("power PSU_A true", 1);
            thread._stepExecutor = exec.Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);
            Assert.Equal(["power PSU_A true", "power PSU_A true"], exec.Calls);
            Assert.Equal(["hold", "retry"], thread._pauseEvents.Select(e => e.Decision));
            Assert.False(thread.Core.IsHeldForOperator);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Abort still ends a held run — and, the case the decision loop cannot cover on its
    /// own, the hold flag is cleared when the WAIT THROWS. This is the toolbar's Stop /
    /// <c>AbortRecipe</c>, which cancels the run token: the handler leaves through the
    /// OperationCanceledException path and never returns to the loop at all. A flag left set
    /// there would hand manual control to the operator on a run that has already ended.</summary>
    [Fact]
    public async Task Abort_from_the_toolbar_ends_a_held_run_and_clears_the_hold()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.StartRecipe();                            // a real RecipeCancellationToken
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                thread.Core.AbortRecipe();                        // what the toolbar's Abort sends
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(),
                    thread.Core.RecipeCancellationToken).WaitAsync(TimeSpan.FromSeconds(30)));
            await decide;

            Assert.False(thread.Core.IsHeldForOperator);
            Assert.False(thread.Core.IsFaultPaused);
            Assert.Equal(["hold", "abort"], thread._pauseEvents.Select(e => e.Decision));
            Assert.Equal("operator-stop", thread._pauseEvents[1].By);
            Assert.Contains(DrainAll(pool).Log, l => l.StartsWith("FAULT DECISION: abort (operator-stop"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The hold warning is load-bearing, not a nicety: the watchdog is disarmed and
    /// either an operator or the agent may begin a hold, so nothing else in the system ever says
    /// that a run is parked mid-recipe with the machine hand-drivable. It must repeat, so a hold
    /// left overnight is visible more than once, and it must never end the run.</summary>
    [Fact]
    public async Task An_unattended_hold_warns_in_the_event_log_and_keeps_warning()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.HeldWarningSeconds = 1;
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                await Task.Delay(2600);                           // two warning intervals
                Assert.True(thread.Core.IsHeldForOperator, "a warning ended the hold");
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);                                      // warning ≠ abort
            var (log, _) = DrainAll(pool);
            Assert.True(log.Count(l => l.Contains("Recipe HELD for")) >= 2,
                "the hold warning did not repeat");
            Assert.Contains(log, l => l.Contains("Recipe HELD for") && l.Contains("power PSU_A true"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Zero_disables_the_hold_warning()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.HeldWarningSeconds = 0;
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                await Task.Delay(1500);
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.DoesNotContain(DrainAll(pool).Log, l => l.Contains("Recipe HELD for"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The whole feature in one assertion set: <c>IsManualControlGated</c> is
    /// <c>IsRecipeRunning</c> MINUS the hold. Every manual command path reads this, so if this
    /// is wrong the hold either unlocks nothing (the bug Hold exists to fix) or unlocks the
    /// machine under a recipe that is still running it.</summary>
    [Fact]
    public void The_manual_gate_is_the_run_flag_minus_the_hold()
    {
        var idle = new CoreThread(new ChannelPool()).Core;
        Assert.False(idle.IsManualControlGated);              // idle: nothing is gated

        var core = NewThread().Thread.Core;                   // NewThread already holds the slot
        try
        {
            Assert.True(core.IsManualControlGated);           // running: gated
            core.SetHeldForOperator(true);
            Assert.False(core.IsManualControlGated);          // held: unlocked
            Assert.True(core.IsRecipeRunning);                // but the run still owns the machine
            core.SetHeldForOperator(false);
            Assert.True(core.IsManualControlGated);           // resumed: gated again
        }
        finally { core.EndRecipeSlot(); }
    }

    /// <summary>"Hardware yes, config no." Sources / Reload Sources / the HW self-test
    /// gate on <c>IsDepositionActive</c>, which a hold does NOT clear — the run still owns the
    /// machine and its accounting. A hold hands over the knobs, not the configuration the
    /// erosion engine is booking against.</summary>
    [Fact]
    public void Config_stays_read_only_throughout_a_hold()
    {
        var core = NewThread().Thread.Core;
        try
        {
            core.SetHeldForOperator(true);
            Assert.True(core.IsDepositionActive);
            Assert.False(core.IsManualControlGated);          // the two must disagree

            // Only call sites where production code READS the flag itself are pinned here:
            // frmSources.ValidateSave and Core.RunHardwareSelfTest — switching either to
            // IsManualControlGated turns this test red. Passing the flag in from the test pins
            // the rule, not the choice of flag. FrmMain.Toolbar (FrmMain cannot be
            // constructed here) is not reachable from a unit test.

            // Sources (frmSources.ValidateSave) — the dialog's own re-check at Save. The form
            // reads core.IsDepositionActive itself, so this pins the flag choice.
            using var sources = new frmSources(new SourceManager(6), core: core);
            var err = sources.ValidateSave();
            Assert.NotNull(err);
            Assert.Contains("deposition", err, StringComparison.OrdinalIgnoreCase);

            // HW self-test — called through CORE, not through HwDiagnostics directly, so the
            // flag read (Core.Main.cs RunHardwareSelfTest) is the production one.
            var report = core.RunHardwareSelfTest();
            Assert.False(report.Ran);
            Assert.Contains("deposition is active", report.BlockedReason);
        }
        finally { core.EndRecipeSlot(); }
    }

    [Fact]
    public void Held_manual_commands_are_recorded_and_drained()
    {
        var core = NewThread().Thread.Core;
        try
        {
            core.NoteHeldManualCommand("Power", "PSU_A OutputOn");
            core.NoteHeldManualCommand("Valve", "V1 open");
            var taken = core.TakeHeldManualCommands();
            Assert.Equal(2, taken.Count);
            Assert.Contains(taken, t => t.EndsWith("Power: PSU_A OutputOn"));
            Assert.Empty(core.TakeHeldManualCommands());      // draining clears
        }
        finally { core.EndRecipeSlot(); }
    }

    private sealed class RecordingPsu(string id) : BasePowerSupply(new BaseDeviceParams(id, 0, "psu"))
    {
        public readonly List<(string Name, float Value)> Setpoints = [];
        public bool OutputOnCalled;
        public override void ApplySetpoint(string parameter, float value) => Setpoints.Add((parameter, value));
        public override void OutputOn() => OutputOnCalled = true;
    }

    private static void DispatchPower(CoreThread thread, string psuId, params object[] args) =>
        typeof(CoreThread).GetMethod("ProcessPowerCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(thread, [new ParamMsg(MsgType.Command, DeviceType.Power, psuId, args)]);

    private static void DispatchDrive(CoreThread thread, string driveId, params object[] args) =>
        typeof(CoreThread).GetMethod("ProcessDriveCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(thread, [new ParamMsg(MsgType.Command, DeviceType.Drive, driveId, args)]);

    private static void DispatchWorker(CoreThread thread, string operation, params object[] args) =>
        ((Task)typeof(CoreThread)
            .GetMethod("ProcessSingleWorkerCommandCore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(thread, [new ParamMsg(MsgType.Command, DeviceType.Worker, operation, args)])!)
            .GetAwaiter().GetResult();

    /// <summary>The reason Hold exists. The dialog asks the operator to fix a supply. Held, the
    /// command reaches the hardware — and lands in the run record (the .run.json sidecar), because
    /// a record that shows an uninterrupted deposition of a hand-driven run is worse than none.
    /// </summary>
    [Fact]
    public void A_held_psu_command_reaches_the_supply_and_the_run_record()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new RecordingPsu("PSU_A") { Connected = true };
            Register(thread.Core, DeviceType.Power, psu);

            DispatchPower(thread, "PSU_A", "SetPower", 120f);
            Assert.Empty(psu.Setpoints);                      // running, not held: refused

            thread.Core.SetHeldForOperator(true);
            DispatchPower(thread, "PSU_A", "SetPower", 120f);
            DispatchPower(thread, "PSU_A", "OutputOn");

            Assert.Equal([("Power", 120f)], psu.Setpoints);
            Assert.True(psu.OutputOnCalled);
            var noted = thread.Core.TakeHeldManualCommands();
            Assert.Contains(noted, n => n.EndsWith("Power: PSU_A SetPower"));
            Assert.Contains(noted, n => n.EndsWith("Power: PSU_A OutputOn"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void A_held_jog_reaches_the_drive_and_the_run_record()
    {
        var (thread, _) = NewThread();
        try
        {
            var drive = new RecordingDrive("Holder") { Connected = true };
            Register(thread.Core, DeviceType.Drive, drive);

            DispatchDrive(thread, "Holder", "JogTo", 10f);
            Assert.Empty(drive.Commands);                     // running, not held: refused

            thread.Core.SetHeldForOperator(true);
            DispatchDrive(thread, "Holder", "JogTo", 10f);

            Assert.Equal(["JogTo"], drive.Commands);
            Assert.Contains(thread.Core.TakeHeldManualCommands(), n => n.EndsWith("Drive: Holder JogTo"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    private sealed class RecordingValve(string id) : BaseValve(new BaseDeviceParams(id, 0, "valve"))
    {
        public readonly List<bool> Commands = [];
        /// <summary>Set true to model a failed VERIFIED write — the valve was commanded but the
        /// read-back did not confirm it, which is what makes OpValve return null.</summary>
        public bool FailVerification { get; set; }
        public override void Open() { Commands.Add(true); LastWriteVerified = !FailVerification; }
        public override void Close() { Commands.Add(false); LastWriteVerified = !FailVerification; }
    }

    private sealed class RecordingMfc(string id) : BaseMFC(new BaseDeviceParams(id, 0, "mfc"))
    {
        public readonly List<float> Flows = [];
        private float _flow;
        protected override void WriteHW(float v) { Flows.Add(v); _flow = v; }
        protected override float ReadHW() => _flow;
    }

    private sealed class RecordingPump(string id) : BasePump(new BaseDeviceParams(id, 0, "pump"))
    {
        public readonly List<bool> Commands = [];
        private bool _on;
        public override bool IsRunning => _on;
        protected override void WriteHW(bool state) { Commands.Add(state); _on = state; }
    }

    /// <summary>A gauge whose reading a test can move, so an interlock can be flipped from
    /// blocking to satisfied inside one test. (Sensor is used rather than Pump because the Pump
    /// branch of CheckInterlocks casts to the concrete PlcPump.)</summary>
    private sealed class StubSensor(string id) : BaseSensor(new BaseDeviceParams(id, 0, "sensor"))
    {
        public float Reading { get; set; }
        protected override float ReadHW() => Reading;
    }

    /// <summary>Manual gas and pumping. Held,
    /// it opens — venting a chamber, re-striking gas flow or restarting a backing pump is exactly
    /// the repair an operator is handed the machine to make — and every command lands in the run
    /// record. All three device kinds, not just the valve.</summary>
    [Fact]
    public void A_held_gas_and_pumping_command_reaches_the_hardware_and_the_run_record()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.LoadEmbeddedOperations();
            var valve = new RecordingValve("V1") { Connected = true };
            var mfc = new RecordingMfc("Ar") { Connected = true };
            var pump = new RecordingPump("SP1") { Connected = true };
            Register(thread.Core, DeviceType.Valve, valve);
            Register(thread.Core, DeviceType.MFC, mfc);
            Register(thread.Core, DeviceType.Pump, pump);

            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);
            DispatchWorker(thread, "MFC", DeviceType.MFC, "Ar", 0f);
            DispatchWorker(thread, "Pump", DeviceType.Pump, "SP1", true);
            Assert.Empty(valve.Commands);                     // running, not held: all refused
            Assert.Empty(mfc.Flows);
            Assert.Empty(pump.Commands);

            thread.Core.SetHeldForOperator(true);
            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);
            DispatchWorker(thread, "MFC", DeviceType.MFC, "Ar", 0f);
            DispatchWorker(thread, "Pump", DeviceType.Pump, "SP1", true);

            Assert.Equal([true], valve.Commands);
            Assert.Equal([0f], mfc.Flows);
            Assert.Equal([true], pump.Commands);
            var noted = thread.Core.TakeHeldManualCommands();
            Assert.Contains(noted, n => n.EndsWith("Manual: Valve V1 = True"));
            Assert.Contains(noted, n => n.EndsWith("Manual: MFC Ar = 0"));
            Assert.Contains(noted, n => n.EndsWith("Manual: Pump SP1 = True"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>RULING P. Isolate is the gas-side kill switch and stays UNGATED, which is exactly
    /// why a plain valve close was not carved out of the gate. But the evidence half must follow:
    /// a panic button pressed during a hold closes every valve and zeroes every MFC — strictly
    /// more than the single valve command the gate refuses — and must appear in the record.
    /// </summary>
    [Fact]
    public void Isolate_is_never_gated_but_is_recorded_when_held()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.LoadEmbeddedOperations();
            var valve = new RecordingValve("V1") { Connected = true };
            Register(thread.Core, DeviceType.Valve, valve);

            // Running, NOT held: the plain valve command is refused, Isolate is not.
            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", false);
            Assert.Empty(valve.Commands);
            DispatchWorker(thread, "Isolate", DeviceType.Valve, "V1");
            Assert.Equal([false], valve.Commands);
            Assert.Empty(thread.Core.TakeHeldManualCommands());   // not held: nothing to record

            thread.Core.SetHeldForOperator(true);
            DispatchWorker(thread, "Isolate", DeviceType.Valve, "V1");

            Assert.Equal([false, false], valve.Commands);         // still ungated
            Assert.Contains(thread.Core.TakeHeldManualCommands(), n => n.Contains("Isolate"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The record must never ASSERT a hand action that did not happen — that is worse
    /// than omitting one, because a signed sidecar is read as evidence. A command an interlock
    /// refused never touched the hardware, so it must not appear.</summary>
    [Fact]
    public void A_command_an_interlock_refused_is_not_recorded()
    {
        var (thread, _) = NewThread();
        string xml = Path.Combine(Path.GetTempPath(), $"aps-interlocks-{Guid.NewGuid():N}.xml");
        try
        {
            thread.Core.LoadEmbeddedOperations();
            var valve = new RecordingValve("V1") { Connected = true };
            var gauge = new StubSensor("PM") { Connected = true, Reading = 900f };
            Register(thread.Core, DeviceType.Valve, valve);
            Register(thread.Core, DeviceType.Sensor, gauge);

            // "V1 may only open below 750" — the gauge reads 900, so opening V1 is blocked.
            File.WriteAllText(xml,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<interlocks>\n  <interlock id=\"Valve_V1_open\">\n" +
                "    <l>Sensor PM less 750</l>\n  </interlock>\n</interlocks>\n");
            Assert.True(thread.Core.LoadInterlocks(xml), thread.Core.LastErrorMessage ?? "(no message)");

            thread.Core.SetHeldForOperator(true);
            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);

            Assert.Empty(valve.Commands);                      // the interlock stopped it
            Assert.Empty(thread.Core.TakeHeldManualCommands()); // …so the record must not claim it

            // …and the same operation, once the interlock is satisfied, IS recorded — otherwise
            // this test would pass with the recording removed altogether.
            gauge.Reading = 100f;
            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);
            Assert.Equal([true], valve.Commands);
            Assert.Contains(thread.Core.TakeHeldManualCommands(), n => n.EndsWith("Manual: Valve V1 = True"));
        }
        finally
        {
            thread.Core.EndRecipeSlot();
            if (File.Exists(xml)) File.Delete(xml);
        }
    }

    /// <summary>RULING Q. A failed VERIFIED write is the case with no clean answer: the command
    /// went out — a wire was touched and the valve may well have moved — but the read-back did
    /// not confirm it. Recording it as an ordinary success would assert a hand action that may
    /// not have happened; omitting it would leave the record silent about a valve that may have
    /// moved. It is recorded MARKED, and the marker must be unmistakable to a human reading the
    /// run record and distinguishable from a clean success.</summary>
    [Fact]
    public void A_command_whose_read_back_did_not_verify_is_recorded_as_unverified()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.LoadEmbeddedOperations();
            var valve = new RecordingValve("V1") { Connected = true, FailVerification = true };
            Register(thread.Core, DeviceType.Valve, valve);
            thread.Core.SetHeldForOperator(true);

            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);

            Assert.Equal([true], valve.Commands);              // the wire WAS touched…
            string unverified = Assert.Single(thread.Core.TakeHeldManualCommands());
            Assert.Contains("Valve V1 = True", unverified);    // …the command itself is named…
            Assert.Contains("UNVERIFIED", unverified);         // …and it is unmistakably marked
            Assert.Contains("read-back did not confirm it", unverified);

            // The same command, verified, is recorded WITHOUT the marker — so the two are
            // distinguishable, and this test cannot pass by marking everything.
            valve.FailVerification = false;
            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);
            string clean = Assert.Single(thread.Core.TakeHeldManualCommands());
            Assert.EndsWith("Manual: Valve V1 = True", clean);
            Assert.DoesNotContain("UNVERIFIED", clean);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The other null: an operation name the worker does not know. Nothing ran and
    /// nothing was touched, so — unlike the unverified case above — there is nothing to record.
    /// The two are distinguishable at the recording point because Core.GetOperation is a pure
    /// lookup.</summary>
    [Fact]
    public void An_unknown_operation_touched_nothing_and_is_not_recorded()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.LoadEmbeddedOperations();
            var valve = new RecordingValve("V1") { Connected = true };
            Register(thread.Core, DeviceType.Valve, valve);
            thread.Core.SetHeldForOperator(true);

            DispatchWorker(thread, "NoSuchOperation", DeviceType.Valve, "V1", true);

            Assert.Empty(valve.Commands);                       // nothing ran
            Assert.Empty(thread.Core.TakeHeldManualCommands()); // …so nothing to record
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    private sealed class RejectingPsu(string id) : BasePowerSupply(new BaseDeviceParams(id, 0, "psu"))
    {
        public int Reconnects;
        public override bool Reconnect() { Reconnects++; return true; }
        public override void ApplySetpoint(string parameter, float value)
            => throw new ArgumentException($"unknown setpoint '{parameter}'");
    }

    /// <summary>Same rule on the PSU path, for both of its refusal shapes: a command name the
    /// dispatcher does not know, and a setpoint the driver rejects. Neither reached the supply.
    /// </summary>
    [Fact]
    public void A_psu_command_the_supply_rejected_is_not_recorded()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new RejectingPsu("PSU_A") { Connected = true };
            Register(thread.Core, DeviceType.Power, psu);
            thread.Core.SetHeldForOperator(true);

            DispatchPower(thread, "PSU_A", "NoSuchCommand");   // unknown command name
            DispatchPower(thread, "PSU_A", "SetPower", 120f);  // ApplySetpoint throws

            Assert.Empty(thread.Core.TakeHeldManualCommands());
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Reconnect is handled before the gate — and before the Connected guard, because a
    /// dropped supply is exactly when it is needed. "Always allowed" is not the same as "not an
    /// operator action": re-establishing the link is part of the hand repair.</summary>
    [Fact]
    public void Reconnect_during_a_hold_reaches_the_supply_and_is_recorded()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new RejectingPsu("PSU_A") { Connected = false };   // dropped
            Register(thread.Core, DeviceType.Power, psu);
            thread.Core.SetHeldForOperator(true);

            DispatchPower(thread, "PSU_A", "Reconnect");

            Assert.Equal(1, psu.Reconnects);
            Assert.Contains(thread.Core.TakeHeldManualCommands(), n => n.EndsWith("Power: PSU_A Reconnect"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>A jog the Connected guard swallowed never moved anything, so it must not be
    /// recorded as though the operator had moved the holder.</summary>
    [Fact]
    public void A_jog_that_never_reached_a_disconnected_drive_is_not_recorded()
    {
        var (thread, _) = NewThread();
        try
        {
            var drive = new RecordingDrive("Holder") { Connected = false };
            Register(thread.Core, DeviceType.Drive, drive);
            thread.Core.SetHeldForOperator(true);

            DispatchDrive(thread, "Holder", "JogTo", 10f);

            Assert.Empty(drive.Commands);
            Assert.Empty(thread.Core.TakeHeldManualCommands());
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The list is written verbatim into the signed .depo.json and a hold has no
    /// deadline, so it must be bounded. A SILENTLY truncated evidence list is worse than a short
    /// one — nothing downstream could tell "they did four things" from "they did four hundred" —
    /// so the truncation names how many were dropped.</summary>
    [Fact]
    public void The_recorded_list_is_capped_and_says_how_many_were_dropped()
    {
        var core = NewThread().Thread.Core;
        try
        {
            int cap = Core.MaxHeldManualCommands;
            for (int i = 0; i < cap + 7; i++)
                core.NoteHeldManualCommand("Power", $"PSU_A SetPower {i}");

            var taken = core.TakeHeldManualCommands();
            Assert.Equal(cap + 1, taken.Count);                 // cap entries + one truncation line
            Assert.EndsWith("SetPower 0", taken[0]);            // the EARLIEST are kept
            Assert.Contains("TRUNCATED", taken[^1]);
            Assert.Contains("7 further manual command(s)", taken[^1]);

            // Draining resets the dropped count too: the next hold starts clean.
            core.NoteHeldManualCommand("Power", "PSU_A OutputOff");
            var second = core.TakeHeldManualCommands();
            Assert.Single(second);
            Assert.DoesNotContain(second, l => l.Contains("TRUNCATED"));
        }
        finally { core.EndRecipeSlot(); }
    }

    /// <summary>The agent's device path gates on the manual gate too, or remote
    /// diagnosis-and-recovery — an agent re-igniting an RF source that dropped out — cannot
    /// happen inside the hold the agent itself may have begun. The SECOND guard
    /// (<c>IsDepositionActive</c>) deliberately stays up: a held operator at the machine may jog,
    /// a remote caller may not move the holder out from under them.</summary>
    [Fact]
    public void The_agent_device_path_is_gated_by_the_manual_gate_not_the_run_flag()
    {
        var (thread, pool) = NewThread();
        try
        {
            var agent = new CoreAgentSystemAccess(thread, pool, new EventLogRingBuffer());
            var psu = new RecordingPsu("PSU_A") { Connected = true };
            Register(thread.Core, DeviceType.Power, psu);
            Register(thread.Core, DeviceType.Drive, new RecordingDrive("Holder") { Connected = true });
            Register(thread.Core, DeviceType.Valve, new RecordingValve("V3") { Connected = true });

            // Running, not held: the FIRST gate refuses everything that could energise the
            // machine or move it. Reconnect is in this list deliberately — the PSU PANEL lets it
            // through during a run, and RULING R carved out OutputOff ONLY, so the wire must keep
            // refusing it until that is decided on its own terms rather than as a side effect.
            object[][] refusedWhileRunning =
            [
                ["Power", "PSU_A", new object[] { "OutputOn" }],
                ["Power", "PSU_A", new object[] { "SetPower", 100f }],
                ["Power", "PSU_A", new object[] { "Reconnect" }],
                ["Drive", "Holder", new object[] { "JogTo", 10f }],
                ["Valve", "V3", new object[] { true }],
            ];
            foreach (var probe in refusedWhileRunning)
            {
                var running = Assert.Throws<AgentOpException>(
                    () => agent.SendDeviceCommand((string)probe[0], (string)probe[1], (object[])probe[2]));
                Assert.Equal(AgentErrors.Busy, running.Code);
                Assert.Contains("recipe is running", running.Message);
            }

            // …but NOT the kill switch. RULING R: a PSU OutputOff goes through on a running,
            // un-held recipe, exactly as ProcessPowerCommand has always let the panel's press
            // through — a kill switch must never sit behind a dialog, and the same caller already
            // holds recipe.abort, which is strictly more destructive. This is also what gives the
            // operator-cut pause an entry point at all: past this gate it lands in
            // ProcessPowerCommand's OutputOff case, which calls RequestOperatorCut.
            agent.SendDeviceCommand("Power", "PSU_A", ["OutputOff"]);
            // Channel-qualified form too (BIAS_1), split the same way ProcessPowerCommand splits
            // it — the two gates must not disagree about what a kill switch is.
            agent.SendDeviceCommand("Power", "PSU_A", ["OutputOff:2"]);

            // Both reached the HW channel rather than being silently dropped somewhere past the
            // gate: "not refused" is not the same as "dispatched".
            var dispatched = new List<string>();
            while (pool.HWReader!.TryRead(out var msg))
                if (msg is ParamMsg { Target: DeviceType.Power, Args.Length: > 0 } p)
                    dispatched.Add($"{p.Operation} {p.Args[0]}");
            Assert.Equal(["PSU_A OutputOff", "PSU_A OutputOff:2"], dispatched);

            thread.Core.SetHeldForOperator(true);

            // Held: the first gate opens, so remote recovery — re-igniting a source that dropped
            // out — is possible at all. This is the whole point of moving that gate.
            agent.SendDeviceCommand("Power", "PSU_A", ["OutputOn"]);

            // …but the SECOND guard (IsDepositionActive, which a hold does NOT clear) still
            // stands: a held operator standing at the machine may jog, a remote caller may not
            // move the holder out from under them. That asymmetry is deliberate.
            Assert.True(thread.Core.IsDepositionActive);
            var drive = Assert.Throws<AgentOpException>(
                () => agent.SendDeviceCommand("Drive", "Holder", ["JogTo", 10f]));
            Assert.Equal(AgentErrors.Busy, drive.Code);
            // No supply is drawing watts here, so ActivePsuSummary falls back — and during a hold
            // "a deposition is active" would send a remote caller looking for film growth that is
            // not happening.
            Assert.Contains("the run is held for the operator", drive.Message);
            Assert.DoesNotContain("a deposition is active", drive.Message);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>CARRY-1. The operator holds, hand-drives the supply, decides it is dead and
    /// presses STOP rather than Abort in the dialog. That leaves through
    /// <c>WaitForFaultDecisionAsync</c>'s cancellation path, whose PauseEvent must still carry
    /// <c>ManualCommands</c>, or everything the operator did by hand vanishes from the signed
    /// run record.</summary>
    [Fact]
    public async Task An_operator_stop_during_a_hold_keeps_what_was_hand_driven()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;         // a lost decision fails fast, not hangs
            thread.Core.StartRecipe();                        // a real RecipeCancellationToken
            var psu = new RecordingPsu("PSU_A") { Connected = true };
            Register(thread.Core, DeviceType.Power, psu);
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                DispatchPower(thread, "PSU_A", "SetPower", 0f);   // hand-driven during the hold
                DispatchPower(thread, "PSU_A", "ClearFault");
                thread.Core.AbortRecipe();                          // the toolbar's Stop
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(),
                    thread.Core.RecipeCancellationToken).WaitAsync(TimeSpan.FromSeconds(30)));
            await decide;

            var stop = Assert.Single(thread._pauseEvents, e => e.By == "operator-stop");
            Assert.NotNull(stop.ManualCommands);
            Assert.Contains(stop.ManualCommands!, c => c.EndsWith("Power: PSU_A SetPower"));
            Assert.Contains(stop.ManualCommands!, c => c.EndsWith("Power: PSU_A ClearFault"));
            // It must DRAIN, not peek: a leftover list would follow this hold into the next
            // pause of the next run and attribute one operator's hand repair to another's.
            Assert.Empty(thread.Core.TakeHeldManualCommands());
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>CARRY-2. A Hold re-enters the decision loop without the step having failed again.
    /// Announcing on every pass would make ONE hold read as two failures in the run log, and re-send a
    /// Paused progress frame — which resets RecipeProgressTracker's timestamp and restarts the
    /// agent's stepElapsedSeconds at zero. A hold never auto-aborts, so hold AGE is the only
    /// signal that a tool is parked; repeated holds must not keep hiding it. TWO holds, not one:
    /// one hold would pass with the flag merely being re-armed each pass, which is precisely the
    /// regression that hides hold age across repeated Holds.</summary>
    [Fact]
    public async Task A_hold_announces_the_pause_once_not_twice()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread._stepExecutor = new ScriptedExecutor().FailTimes("power PSU_A true", 99).Execute;

            var decide = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                await HoldAgain(thread, expectedEventsAfter: 2);   // a SECOND hold, same pause
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await decide;
            Assert.True(ok);

            // The banner goes to BOTH sinks (SendRecipeLog → the step log, SendLogMesssage → the
            // event log). Count them apart: a regression that re-announced would double each.
            int stepLog = 0, eventLog = 0, pausedFrames = 0;
            while (pool.GUIReader!.TryRead(out var msg))
            {
                if (msg is ValMsg<string> { Value: not null } v
                    && v.Value.StartsWith("STEP FAILED (paused)"))
                {
                    if (v.Target == DeviceType.Recipe) stepLog++; else eventLog++;
                }
                if (msg is ParamMsg { Device: "RecipeProgress" } p
                    && p.Args.Length > 4 && (int)p.Args[4] == (int)RecipeState.Paused) pausedFrames++;
            }
            Assert.Equal(1, stepLog);
            Assert.Equal(1, eventLog);
            Assert.Equal(1, pausedFrames);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void Held_warning_default_is_900_seconds()
    {
        var core = NewThread().Thread.Core;
        try { Assert.Equal(900, core.HeldWarningSeconds); }
        finally { core.EndRecipeSlot(); }
    }

    [Fact]
    public void An_absurd_hold_warning_is_clamped_to_24_hours_and_logged()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.ApplyHeldWarningTimeout(int.MaxValue);

            Assert.Equal(86_400, thread.Core.HeldWarningSeconds);
            Assert.Contains(DrainAll(pool).Log, l => l.Contains("out of range") && l.Contains("clamped to 86400 s"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void A_negative_hold_warning_is_clamped_to_zero_and_logged()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.ApplyHeldWarningTimeout(-5);

            Assert.Equal(0, thread.Core.HeldWarningSeconds);
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("out of range") && l.Contains("clamped to 0 s"));
            Assert.Contains(log, l => l.Contains("Hold warnings disabled"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void An_in_range_hold_warning_is_applied_without_a_clamp_line()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.ApplyHeldWarningTimeout(300);

            Assert.Equal(300, thread.Core.HeldWarningSeconds);
            var log = DrainAll(pool).Log;
            Assert.DoesNotContain(log, l => l.Contains("clamped"));
            Assert.Contains(log, l => l.Contains("Hold warning interval: 300 s"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }
}
