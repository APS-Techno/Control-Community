using System.Reflection;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Recipe;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Operator PSU cut: the panel's power-OFF stays an unconditional kill switch during a run
/// (<c>CoreThread.Execute.ProcessPowerCommand</c> lets <c>OutputOff</c> through while every other
/// manual command is rejected), but the recipe must NOTICE, or it sits out the rest of the dwell
/// growing nothing while the evidence record still claims a layer.
/// <para>A cut cancels the in-flight step and enters the existing fault pause. Resume re-energises
/// first — <c>deposit</c> itself touches no supply (Operations.Deposit moves the holder and drives
/// the shutter only), so a bare retry would re-open the shutter over a dead target.</para>
/// </summary>
public class OperatorCutPauseTests
{
    /// <summary>Claims the single recipe slot before returning, modelling what the real entry
    /// point (<c>ExecuteRecipe</c>) does before it ever calls <c>ExecuteRecipeWithInterpreter</c>
    /// — the method every test here calls directly. Without this, <c>Core.IsRecipeRunning</c>
    /// stays false for the lifetime of the test and <c>Core.RequestOperatorCut</c> refuses every
    /// cut. Callers MUST release the slot (<c>core.EndRecipeSlot()</c>) when done, or it leaks
    /// into the next test.</summary>
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

    /// <summary>A supply that records what was commanded and lets a test drive the responding
    /// flag the re-energise confirmation reads.</summary>
    private sealed class TestPsu(string id) : BasePowerSupply(new BaseDeviceParams(id, 0, id))
    {
        public readonly List<(string Param, float Value)> Setpoints = [];
        public int OutputOnCalls;
        public int OutputOffCalls;
        /// <summary>Set true to model a driver that ACCEPTS a setpoint name and never applies
        /// it — the `set PSU_A Pulse 1` failure mode Operations.Set was hardened against. The
        /// call is recorded, SetPower does not move.</summary>
        public bool IgnoreSetpointWrites;
        /// <summary>Models the drivers that reject "Power" BY DESIGN: BIAS_1 (bias analyser) and
        /// IonSourcePSU (IONPSU, ion source). As in those drivers the capability goes false AND the
        /// name throws, so a test cannot pass on the flag alone; SetPower stays 0 forever.</summary>
        public bool NoPowerSetpoint;
        public override bool SupportsPowerSetpoint => !NoPowerSetpoint;
        /// <summary>Makes the ZERO write fail the way a comm timeout does, while a real setpoint
        /// still lands. The shape that matters on the refusal path: the undo's
        /// ApplySetpoint("Power", 0) throws and the OutputOff must happen anyway.</summary>
        public bool FailZeroSetpointWrite;
        /// <summary>Makes EVERY setpoint write fail — the link has gone away entirely. Set at
        /// pause time it models the adapter dropping between the hold and the Retry, which is
        /// how the restore's own setpoint write comes to throw BEFORE OutputOn is ever
        /// reached.</summary>
        public bool FailSetpointWrites;
        public override void ApplySetpoint(string parameter, float value)
        {
            // A name the driver rejects never reaches the hardware and is never recorded: both
            // real drivers throw out of their validation switch before touching channel state.
            if (NoPowerSetpoint && parameter == "Power")
                throw new ArgumentException(
                    $"TestPsu[{Params.ID}]: unknown setpoint parameter '{parameter}'.", nameof(parameter));
            Setpoints.Add((parameter, value));
            if (FailSetpointWrites || (FailZeroSetpointWrite && parameter == "Power" && value == 0f))
                throw new TimeoutException("setpoint write timed out");
            if (parameter == "Power" && !IgnoreSetpointWrites) SetPower = value;
        }
        /// <summary>Set true to make the relay write fail the way a comm timeout does: the call
        /// throws and IsOutputOn stays latched on. Models the PARTIAL hold zeroing (the setpoint
        /// write lands, the output-off does not).</summary>
        public bool FailOutputOff;
        /// <summary>Fires at the end of OutputOn. The seam for an operator action that lands
        /// INSIDE a restore — ReEnergiseAfterCut commands OutputOn and then settles 500 ms.</summary>
        public Action? AfterOutputOn;
        /// <summary>Makes OutputOn throw the way RfPsuA alone can: ForcePowerOn's "0 G" goes
        /// through BaseSerialPort.SendTextAsync, which WRITES the command and only then reads the
        /// reply, and that read throws InvalidOperationException or IOException on a dropped USB-serial
        /// adapter. The supply may be PHYSICALLY ENERGISED while IsOutputOn stays FALSE. Modelled
        /// exactly: the call is counted, the throw happens, IsOutputOn does not move.</summary>
        public bool FailOutputOnAfterTheWire;
        public override void OutputOn()
        {
            OutputOnCalls++;
            if (FailOutputOnAfterTheWire)
                throw new InvalidOperationException("The port is closed.");
            IsOutputOn = true;
            AfterOutputOn?.Invoke();
        }
        public override void OutputOff()
        {
            OutputOffCalls++;
            if (FailOutputOff) throw new InvalidOperationException("relay write timed out");
            IsOutputOn = false;
        }
        public void MarkResponding() => IsResponding = true;
        /// <summary>Drives IsResponding false — ReadFeedback is a no-op override, so nothing
        /// else in this double ever flips it back without another explicit MarkResponding().</summary>
        public void MarkUnresponding() => IsResponding = false;
        /// <summary>Energised at the setpoint a recipe would have left, without recording it as
        /// one of the calls under test.</summary>
        public TestPsu Energised(float watts)
        {
            SetPower = watts; IsOutputOn = true; Connected = true; MarkResponding();
            return this;
        }

        /// <summary>Connected and answering, but OFF and at 0 W — a supply the operator finds
        /// already dark. An OFF press on this snapshots WasOn=false and has nothing to
        /// restore.</summary>
        public TestPsu Dark()
        {
            SetPower = 0; IsOutputOn = false; Connected = true; MarkResponding();
            return this;
        }

        /// <summary>Live, but with no Power setpoint to be live AT — the ion-source /
        /// bias-analyser shape. SetPower stays 0 because those drivers never write it.</summary>
        public TestPsu EnergisedWithNoPowerSetpoint()
        {
            NoPowerSetpoint = true;
            IsOutputOn = true; Connected = true; MarkResponding();
            return this;
        }
    }

    /// <summary>
    /// A drive whose abort runs an arbitrary action. <c>AbortDrivesForHold</c> is called from
    /// INSIDE <c>RunFailureHold</c>, after it has snapshotted every supply and before it reports,
    /// which makes this the one deterministic hook a unit test has into the hold's own window.
    /// <para>It stands in for a <c>FAILURE_HOLD</c> script line such as <c>power X off</c>: the
    /// DSL <c>power</c> verb resolves through <c>Core.GetOperation</c> and needs a loaded worker
    /// DLL, which these tests do not have, so the script's own lines cannot reach a supply here.
    /// Substituting the mechanism is sound because the thing under test is a STATE DIFF — the
    /// hold arms every supply that lost power while it ran, deliberately without caring which of
    /// its own actions took it. A separate suite covers the real DSL path.</para>
    /// </summary>
    private sealed class HoldHookDrive(string id, Action onAbort) : BaseStepperDrive(new BaseDeviceParams(id, 0, id))
    {
        public override void AbortMotion() => onAbort();
    }

    /// <summary>Installs a blocking interlock rule directly into <c>Core</c>'s private registry
    /// — the same shape <c>ReEnergiseAfterCut</c>'s <c>CheckInterlocks("power", [psu, true])</c>
    /// resolves to (<c>ResolveInterlockKey</c>: "power" + '_' + the PSU's id + '_' + "True",
    /// lower-cased). A plain closed <see cref="BaseValve"/> (default <c>ReadHW</c> returns
    /// false) required open is the simplest condition that fails.</summary>
    private static void BlockPowerOn(Core core, string psuId, out BaseValve blocker)
    {
        blocker = new BaseValve(new BaseDeviceParams("IL1", 0, "IL1")) { Connected = true };
        var interlock = new Interlock();
        interlock.Conditions.Add(new InterlockCond { Target = blocker, Value = true });
        var field = typeof(Core).GetField("interlocks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registry = (Interlocks)field.GetValue(core)!;
        registry.Add($"power_{psuId.ToLowerInvariant()}_true", interlock);
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

    /// <summary>Drives the SAME private dispatch the physical PSU panel uses
    /// (<c>CoreThread.ProcessPowerCommand</c>).</summary>
    private static void DispatchOutputOff(CoreThread thread, string psuId) =>
        DispatchPower(thread, psuId, "OutputOff");

    /// <summary>The same private dispatch for any panel command — the hand repair a hold exists
    /// to permit goes through it exactly as the operator's own presses do.</summary>
    private static void DispatchPower(CoreThread thread, string psuId, params object[] args) =>
        typeof(CoreThread).GetMethod("ProcessPowerCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(thread, [new ParamMsg(MsgType.Command, DeviceType.Power, psuId, args)]);

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

    private static async Task DecideWhenPaused(Core core, FaultDecision d, int times = 1)
    {
        for (int t = 0; t < times; t++)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(core.IsFaultPaused, "handler never paused");
            core.ResolveFault(d);
            await Task.Delay(20);
        }
    }

    /// <summary>Signals when the test's <c>_stepExecutor</c> has actually been entered for the
    /// step under test. <see cref="NewThread"/> claims the recipe slot up front, so
    /// <c>Core.IsRecipeRunning</c> is true from the moment the thread is constructed — long
    /// before the step itself starts running. Waiting on that flag would let a cut land before
    /// <c>ExecuteWithFaultPause</c> has installed the step's own cancellation source, so tests
    /// signal this explicitly from inside the executor instead.</summary>
    private sealed class StepStarted
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void MarkStarted() => _tcs.TrySetResult();
        public Task Wait() => _tcs.Task;
    }

    /// <summary>Cuts the supply the way the panel does once the step is actually running: snapshot,
    /// OutputOff, then arm the pause.</summary>
    private static async Task CutWhenRunning(Core core, TestPsu psu, StepStarted started)
    {
        await started.Wait();
        var cut = OperatorCut.From(psu);
        psu.OutputOff();
        core.RequestOperatorCut(cut);
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
    public void A_cut_snapshots_the_setpoint_and_output_state_before_the_power_drops()
    {
        var psu = new TestPsu("PSU_A").Energised(450);
        var cut = OperatorCut.From(psu);
        psu.OutputOff();

        Assert.Equal("PSU_A", cut.PsuId);
        Assert.Equal(450f, cut.SetPower);
        Assert.True(cut.WasOn);
        Assert.False(psu.IsOutputOn);              // the snapshot did not keep the supply alive
    }

    [Fact]
    public void A_cut_with_no_recipe_running_arms_nothing()
    {
        var core = NewThread().Thread.Core;
        core.EndRecipeSlot();                        // NewThread claims it; this test wants none running
        Assert.False(core.RequestOperatorCut(OperatorCut.From(new TestPsu("PSU_A").Energised(450))));
        Assert.Null(core.TakeOperatorCut());
    }

    [Fact]
    public void Taking_a_cut_consumes_it()
    {
        var core = NewThread().Thread.Core;           // NewThread already claimed the slot
        try
        {
            Assert.True(core.RequestOperatorCut(OperatorCut.From(new TestPsu("PSU_A").Energised(450))));
            Assert.Equal("PSU_A", core.TakeOperatorCut()!.PsuId);
            Assert.Null(core.TakeOperatorCut());
        }
        finally { core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_cut_mid_step_pauses_the_recipe_and_names_the_operator_and_the_supply()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Abort);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            var ev = Assert.Single(thread._pauseEvents);
            Assert.Equal("abort", ev.Decision);
            Assert.Contains("de-energised", ev.Reason);
            Assert.Contains("PSU_A", ev.Reason);
            var (log, pauses) = DrainAll(pool);
            Assert.Contains("PSU_A", Assert.Single(pauses).Reason);
            Assert.Contains(log, l => l.StartsWith("STEP FAILED (paused): deposit Si 100 Home"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task An_operator_stop_during_a_step_still_cancels_the_run_and_never_pauses()
    {
        var (thread, _) = NewThread();
        try
        {
            using var run = new CancellationTokenSource();
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var stop = Task.Run(async () =>
            {
                await started.Wait();
                run.Cancel();
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                thread.ExecuteRecipeWithInterpreter("deposit Si 100 Home", new Interpreter(), run.Token));
            await stop;

            Assert.Empty(thread._pauseEvents);
            Assert.False(thread.Core.IsFaultPaused);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task The_hold_de_energises_the_cut_supply_even_though_the_deposit_line_names_none()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
            var psu = new TestPsu("PSU_A").Energised(450);
            var other = new TestPsu("RF601").Energised(300);
            Register(thread.Core, DeviceType.Power, psu);
            Register(thread.Core, DeviceType.Power, other);
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Abort);
            await thread.ExecuteRecipeWithInterpreter("deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.Contains(("Power", 0f), psu.Setpoints);
            Assert.Empty(other.Setpoints);
            Assert.Equal(0, other.OutputOffCalls);

            var hold = Assert.Single(DrainAll(pool).Pauses).Hold;
            Assert.Contains(hold.Done, d => d.Contains("PSU_A") && d.Contains("output off"));
            Assert.DoesNotContain(hold.Done, d => d.Contains("nothing was de-energised"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_re_energises_at_the_cut_setpoint_before_the_line_re_runs()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<(string Step, int OnCalls, float SetPower)>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                seen.Add((string.Join(' ', parts), psu.OutputOnCalls, psu.SetPower));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.Equal(0, seen[0].OnCalls);                 // first attempt: nobody re-energised
            Assert.Equal(1, seen[1].OnCalls);                 // resume energised BEFORE the re-run
            Assert.Equal(450f, seen[1].SetPower);             // at the setpoint the cut took away
            Assert.Contains(("Power", 450f), psu.Setpoints);
            Assert.True(psu.IsOutputOn);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_of_an_interrupted_deposit_carries_only_the_remaining_dwell()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<string>();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0)
                {
                    thread.Core.NoteDwellStarted("Deposit Si", 100);
                    await Task.Delay(30_000, ct);
                }
                return true;
            };

            var cut = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (thread.Core.LastDwell is null && DateTime.UtcNow < deadline) await Task.Delay(5);
                await Task.Delay(2000);                        // 2 s of the 100 s dwell actually ran
                var c = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(c);
            });
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, calls.Count);
            Assert.Equal("deposit Si 100 Home", calls[0]);
            string[] retried = calls[1].Split(' ');
            double remaining = double.Parse(retried[2], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(remaining, 96, 98.5);               // 100 − ~2 s, measured to the CUT
            Assert.Equal("Home", retried[3]);
            Assert.Contains(DrainAll(pool).Log, l => l.StartsWith("Deposit resumed with"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_that_cannot_re_energise_pauses_again_instead_of_re_running_the_line()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<string>();
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                started.MarkStarted();
                await Task.Delay(30_000, ct);
                return true;
            };

            var cut = Task.Run(async () =>
            {
                await started.Wait();
                var c = OperatorCut.From(psu);
                psu.OutputOff();
                psu.Connected = false;                         // the link went with it
                thread.Core.RequestOperatorCut(c);
            });
            // Resume is refused (nothing to re-energise through), so the handler pauses again;
            // the second answer aborts.
            var decide = Task.Run(async () =>
            {
                await DecideWhenPaused(thread.Core, FaultDecision.Retry);
                await DecideWhenPaused(thread.Core, FaultDecision.Abort);
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.Single(calls);                              // the line NEVER re-ran over a dead target
            Assert.Equal(2, thread._pauseEvents.Count);
            Assert.Equal(0, psu.OutputOnCalls);
            Assert.Contains(DrainAll(pool).Pauses,
                p => p.Reason.Contains("re-energise") && p.Reason.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_refused_by_an_interlock_pauses_again_instead_of_re_running_the_line()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            BlockPowerOn(thread.Core, "PSU_A", out _);

            var calls = new List<string>();
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                started.MarkStarted();
                await Task.Delay(30_000, ct);
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            // Resume is refused by the interlock, so the handler pauses again; the second
            // answer aborts.
            var decide = Task.Run(async () =>
            {
                await DecideWhenPaused(thread.Core, FaultDecision.Retry);
                await DecideWhenPaused(thread.Core, FaultDecision.Abort);
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.Single(calls);                              // the line NEVER re-ran over a dead target
            Assert.Equal(2, thread._pauseEvents.Count);
            Assert.Equal(0, psu.OutputOnCalls);                // the interlock refused BEFORE OutputOn
            Assert.Contains(DrainAll(pool).Pauses,
                p => p.Reason.Contains("blocked by interlock") && p.Reason.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_refused_when_the_supply_never_confirms_pauses_again()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            psu.MarkUnresponding();                            // OutputOn() below will not fix this —
            Register(thread.Core, DeviceType.Power, psu);      // ReadFeedback is a no-op in this double

            var calls = new List<string>();
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                started.MarkStarted();
                await Task.Delay(30_000, ct);
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                await DecideWhenPaused(thread.Core, FaultDecision.Retry);
                await DecideWhenPaused(thread.Core, FaultDecision.Abort);
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.Single(calls);                              // the line NEVER re-ran over a dead target
            Assert.Equal(2, thread._pauseEvents.Count);
            Assert.Equal(1, psu.OutputOnCalls);                // it DID try to command the supply on...
            Assert.False(psu.IsResponding);                    // ...but nothing ever confirmed it
            Assert.Contains(DrainAll(pool).Pauses,
                p => p.Reason.Contains("output not confirmed") && p.Reason.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_after_a_hand_repair_that_never_reached_the_supply_is_still_refused()
    {
        // C1: IsOutputOn is a pure command echo (BasePowerSupply.OutputOn/OutputOff, written
        // from nowhere else). A hand power-ON during the hold that never reached a dead link or
        // an open breaker still latches IsOutputOn = true; resume must not be permitted on that
        // flag alone, without the interlock check and a responding confirmation.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                psu.MarkUnresponding();
                psu.OutputOn();                     // hand repair — latches IsOutputOn, nothing else
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);

                // Refused (unconfirmed) — the handler must pause AGAIN, not carry the run forward.
                await DecideWhenPaused(thread.Core, FaultDecision.Abort);
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.Equal(2, thread._pauseEvents.Count);
            // Ruling N: the operator pressed ON and nothing else over a hold-zeroed setpoint, so this
            // is NOT a hand repair and the resume re-commands the recipe's 450 W: two OutputOn calls,
            // the operator's, then ours. The resume is still REFUSED, and the output we commanded on
            // must be taken back rather than left latched.
            Assert.Equal(2, psu.OutputOnCalls);
            Assert.False(psu.IsOutputOn);
            Assert.Contains(("Power", 0f), psu.Setpoints);
            Assert.Contains(DrainAll(pool).Pauses,
                p => p.Reason.Contains("output not confirmed") && p.Reason.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_after_a_confirmed_hand_repair_keeps_the_operators_setpoint_and_does_not_recommand_output()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<(int OutputOnCalls, float SetPower)>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                seen.Add((psu.OutputOnCalls, psu.SetPower));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // psu.IsResponding is still true (Energised() marked it, and nothing clears it
                // here) — a genuine hand repair, confirmed. The operator chose a DIFFERENT
                // setpoint than the recipe's 450 W; the resume must keep it, not overwrite it.
                psu.OutputOn();
                psu.SetPower = 400f;
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.Equal(1, seen[1].OutputOnCalls);            // only the operator's OutputOn — no re-command
            Assert.Equal(400f, seen[1].SetPower);              // the operator's value, unchanged
            Assert.Equal(400f, psu.SetPower);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_refused_resume_does_not_re_run_the_hold_and_re_zero_a_hand_repaired_supply()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var decide = Task.Run(async () =>
            {
                // Pause #1: the hold zeroes and de-energises PSU_A as usual.
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                int offCallsAfterHold = psu.OutputOffCalls;
                int setpointsAfterHold = psu.Setpoints.Count;
                Assert.True(offCallsAfterHold >= 1);            // the hold really did de-energise it once

                psu.Connected = false;                          // force the resume to be refused
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);

                // Pause #2 (the refusal re-pause): the hold must NOT have run a second time —
                // it would otherwise zero and de-energise a supply the operator was just about
                // to repair, with no indication in the dialog that it had happened again.
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused a second time");
                Assert.Equal(offCallsAfterHold, psu.OutputOffCalls);
                Assert.Equal(setpointsAfterHold, psu.Setpoints.Count);

                thread.Core.ResolveFault(FaultDecision.Abort);
                await Task.Delay(20);
            });

            var cut = CutWhenRunning(thread.Core, psu, started);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.Equal(2, thread._pauseEvents.Count);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_second_OFF_press_during_the_pause_does_not_leak_into_the_next_step()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<string>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // The operator presses OFF again while the dialog is still up. The panel snapshots
                // unconditionally (Execute.cs) and the hold already zeroed the supply, so this arms a
                // WasOn=false / SetPower=0 cut. N3: it MUST NOT displace the ORIGINAL WasOn=true/450 W
                // snapshot (insert-if-absent, Ruling K.2), or Retry treats the energised supply as already
                // restored and re-runs the line over a dead magnetron.
                thread.Core.RequestOperatorCut(new OperatorCut("PSU_A", 0f, false, DateTime.UtcNow));
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(50);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);                                    // did NOT pause again at the end
            Assert.Equal(2, calls.Count);                       // retried exactly once
            Assert.Single(thread._pauseEvents);                 // only the ORIGINAL cut's pause
            // N3: the ORIGINAL (WasOn=true, 450 W) snapshot must be what was restored.
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.Equal(450f, psu.SetPower);
            Assert.True(psu.IsOutputOn);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Skip_after_a_cut_absorbed_during_an_ordinary_pause_says_the_supply_is_left_dead()
    {
        // N4: the absorb step logs "armed for restoration on Retry", true only if Retry is what
        // happens next; Skip must correct that line.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            // Not power/set/ramp/warmup — the hold must not zero PSU_A on its own here; the
            // ONLY thing that touches it is the simulated operator action below.
            thread._stepExecutor = (parts, ct) => throw new InvalidOperationException("drive not responding");

            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                var extraCut = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(extraCut);
                thread.Core.ResolveFault(FaultDecision.Skip);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "holder Si", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.False(psu.IsOutputOn);                       // left dead — Skip does not restore
            Assert.Equal(0, psu.OutputOnCalls);
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("armed for restoration on Retry") && l.Contains("PSU_A"));
            // The correction: Skip must say, plainly, that it is NOT doing what the line above
            // announced.
            Assert.Contains(log, l => l.Contains("continuing WITHOUT restoring") && l.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Retry_after_an_ordinary_failure_restores_the_supply_the_hold_zeroed()
    {
        // Ruling L: `set PSU_A Power 450` fails on a LIVE supply with no operator cut, and
        // RunFailureHold's own generic zeroing turns PSU_A off. Retry re-runs `set`, which
        // energises nothing (Operations.Deposit is the only thing that opens a shutter), so the
        // hold's zeroing must be restored too or the supply stays dead for the next line.
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<(string Step, bool IsOutputOn)>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add((string.Join(' ', parts), psu.IsOutputOn));
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "set PSU_A Power 450", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Equal(2, calls.Count);
            Assert.True(calls[0].IsOutputOn);                   // still live when the first attempt ran
            Assert.True(calls[1].IsOutputOn);                   // the hold zeroed it; Retry restored it
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.Equal(450f, psu.SetPower);
            Assert.True(psu.IsOutputOn);
            Assert.Single(thread._pauseEvents);                 // exactly one pause — no phantom second
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_late_cut_that_is_refused_on_first_Retry_still_reports_step_already_completed_when_it_finally_succeeds()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<string>();
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                var cut = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(cut);
                return Task.FromResult(true);
            };

            var decide = Task.Run(async () =>
            {
                // First Retry: not connected -> refused. stepAlreadySucceeded must survive the
                // reloop, not just the first pass.
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                psu.Connected = false;
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);

                // Second Retry: reconnected, succeeds.
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused a second time");
                psu.Connected = true;
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Single(calls);                               // the completed line never re-ran
            Assert.Equal(2, thread._pauseEvents.Count);
            Assert.All(thread._pauseEvents, ev => Assert.True(ev.StepAlreadyCompleted));
            Assert.Equal("retry", thread._pauseEvents[0].Decision);
            Assert.Equal("retry", thread._pauseEvents[1].Decision);
            Assert.Equal(1, psu.OutputOnCalls);                 // re-energised exactly once, on success
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_cut_that_lands_during_an_ordinary_non_cut_pause_is_restored_before_the_line_re_runs()
    {
        // N1 Probe A: an ORDINARY step failure (no operator cut) pauses the recipe, and while the
        // dialog is open the operator kills a LIVE supply on the panel. Retry must restore it.
        // The failing line is deliberately NOT a power/set/ramp/warmup line (its arg[1] is not a
        // PSU id): RunFailureHold's generic "de-energise the failed line's named supply" fallback
        // would otherwise turn PSU_A off before the operator ever touches the panel.
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<(string Step, bool IsOutputOn)>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add((string.Join(' ', parts), psu.IsOutputOn));
                if (attempt++ == 0) throw new InvalidOperationException("drive not responding");
                return Task.FromResult(true);
            };

            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // NOT the failure that caused the pause — an independent panel OFF press while
                // the dialog is open.
                var extraCut = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(extraCut);
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "holder Si", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Equal(2, calls.Count);
            Assert.True(calls[0].IsOutputOn);                  // first attempt: the cut hasn't happened yet
            Assert.True(calls[1].IsOutputOn);                  // re-run sees the supply RESTORED, not dead
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.True(psu.IsOutputOn);
            Assert.Single(thread._pauseEvents);                // exactly the one pause — no phantom second
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_second_supply_cut_during_the_pause_is_restored_alongside_the_original()
    {
        // N1 Probe B: the pause IS a cut of PSU_A, but while it is open the operator also kills
        // RF301. Retry must restore BOTH before the line re-runs.
        var (thread, _) = NewThread();
        try
        {
            var dc = new TestPsu("PSU_A").Energised(450);
            var rf = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, dc);
            Register(thread.Core, DeviceType.Power, rf);

            var started = new StepStarted();
            var calls = new List<string>();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, dc, started);        // the ORIGINAL cut: PSU_A
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // While paused for PSU_A, the operator ALSO kills RF301 on the panel.
                var rfCut = OperatorCut.From(rf);
                rf.OutputOff();
                thread.Core.RequestOperatorCut(rfCut);
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, calls.Count);                       // the line re-ran exactly once
            Assert.Equal(1, dc.OutputOnCalls);
            Assert.True(dc.IsOutputOn);
            Assert.Equal(1, rf.OutputOnCalls);                  // RF301 restored too — not left dead
            Assert.True(rf.IsOutputOn);
            Assert.Single(thread._pauseEvents);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_refused_resume_leaves_the_supply_de_energised_not_commanded_on()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            psu.MarkUnresponding();                             // never confirms — Retry will refuse
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                await DecideWhenPaused(thread.Core, FaultDecision.Retry);   // refused (unconfirmed)
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);    // operator moves on anyway
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);                                    // Skip carried the run forward
            Assert.False(psu.IsOutputOn);                       // NOT left commanded on
            Assert.Contains(("Power", 0f), psu.Setpoints);       // re-zeroed after the refused command
            var (log, pauses) = DrainAll(pool);
            Assert.Equal(2, pauses.Count);
            Assert.Contains(log, l => l.Contains("de-energised again") && l.Contains("PSU_A"));
            // The re-shown (second) pause must not let the FROZEN, original hold report claim
            // "output off" uncontested — the resume attempt in between changed device state.
            Assert.Contains(pauses[1].Hold.Problems, p => p.Contains("ORIGINAL hold"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_cut_of_a_supply_that_was_already_off_resumes_without_energising_it()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A") { Connected = true };   // never energised
            psu.MarkResponding();
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "holder Si", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(0, psu.OutputOnCalls);                // WasOn was false — nothing to restore
            Assert.False(psu.IsOutputOn);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Pausing_disabled_turns_a_cut_into_an_ordinary_step_failure()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 0;
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut;

            Assert.False(ok);
            Assert.Empty(thread._pauseEvents);
            Assert.False(thread.Core.IsFaultPaused);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void The_real_panel_dispatch_arms_a_cut_and_cancels_the_running_step()
    {
        var (thread, _) = NewThread();
        using var stepCts = new CancellationTokenSource();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            // Model the attempt ExecuteWithFaultPause has in flight while the step runs.
            thread.Core.RegisterStepCancellation(stepCts);

            DispatchOutputOff(thread, "PSU_A");           // the real panel path, not RequestOperatorCut directly

            Assert.True(psu.OutputOffCalls >= 1);            // the kill switch itself still fired
            Assert.False(psu.IsOutputOn);
            Assert.True(stepCts.IsCancellationRequested);    // the in-flight step was interrupted
            var cut = thread.Core.TakeOperatorCut();
            Assert.NotNull(cut);
            Assert.Equal("PSU_A", cut!.PsuId);
            Assert.Equal(450f, cut.SetPower);                // snapshotted BEFORE OutputOff, not after
            Assert.True(cut.WasOn);
        }
        finally { thread.Core.RegisterStepCancellation(null); thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void The_real_panel_dispatch_arms_nothing_when_no_recipe_is_running()
    {
        var thread = new CoreThread(new ChannelPool());     // deliberately NOT NewThread(): no slot claimed
        var psu = new TestPsu("PSU_A").Energised(450);
        Register(thread.Core, DeviceType.Power, psu);

        DispatchOutputOff(thread, "PSU_A");

        Assert.True(psu.OutputOffCalls >= 1);                // the kill switch is unconditional either way
        Assert.False(psu.IsOutputOn);
        Assert.Null(thread.Core.TakeOperatorCut());           // but nothing was armed
    }

    [Fact]
    public async Task A_cut_that_lands_as_the_step_finishes_still_pauses_the_recipe()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            // Deterministically reproduces the race: the cut is armed from INSIDE the step,
            // synchronously, immediately before the step reports success — the exact window
            // where RequestOperatorCut's Cancel() lands on a source whose linked try is about
            // to return ok == true anyway, so the OperationCanceledException catch never fires.
            // No sleep, no thread hand-off: the executor itself is the race.
            thread._stepExecutor = (parts, ct) =>
            {
                var cut = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(cut);
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Abort);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.False(ok);                                 // paused, not silently carried forward
            var ev = Assert.Single(thread._pauseEvents);
            Assert.Contains("de-energised", ev.Reason);
            Assert.Contains("PSU_A", ev.Reason);
            Assert.Contains(DrainAll(pool).Log, l => l.StartsWith("STEP FAILED (paused): deposit Si 100 Home"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_late_cut_on_Retry_re_energises_and_continues_without_re_running_the_completed_line()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            // Same race as A_cut_that_lands_as_the_step_finishes_still_pauses_the_recipe: the
            // step reports success with the cut already armed, so ExecuteWithFaultPause's
            // `if (ok)` drain — not the OperationCanceledException catch — is what turns this
            // into a pause. The line itself never fails.
            var calls = new List<string>();
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                // The dwell fully elapsed by the time this line reported success — proves the
                // claim, not just "did not re-run": if Retry ever called AdjustForResume here it
                // would clear LastDwell and compute the 1 s floor, laying a sliver of extra film
                // on an already-finished layer (M9).
                thread.Core.NoteDwellStarted("Deposit Si", 100);
                var cut = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(cut);
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Single(calls);                              // the completed line never re-ran
            Assert.Equal("deposit Si 100 Home", calls[0]);
            Assert.Equal(1, psu.OutputOnCalls);                // Retry still re-energises
            Assert.Equal(450f, psu.SetPower);
            Assert.True(psu.IsOutputOn);
            Assert.Empty(thread._skippedSteps);
            var ev = Assert.Single(thread._pauseEvents);
            Assert.Equal("retry", ev.Decision);
            Assert.True(ev.StepAlreadyCompleted);              // the run record does not claim a failure
            Assert.Contains("already completed", ev.Reason);
            Assert.NotNull(thread.Core.LastDwell);             // AdjustForResume never touched it
            Assert.DoesNotContain(DrainAll(pool).Log, l => l.StartsWith("Deposit resumed with"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_late_cut_on_Skip_continues_without_re_energising_and_records_no_skipped_step()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            var calls = new List<string>();
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                var cut = OperatorCut.From(psu);
                psu.OutputOff();
                thread.Core.RequestOperatorCut(cut);
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Single(calls);                              // the completed line never re-ran
            Assert.Empty(thread._skippedSteps);                // it completed — recording a skip would lie
            Assert.Equal(0, psu.OutputOnCalls);                // Skip does not re-energise
            Assert.False(psu.IsOutputOn);
            var ev = Assert.Single(thread._pauseEvents);
            Assert.Equal("skip", ev.Decision);
            Assert.True(ev.StepAlreadyCompleted);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Theory]
    [InlineData("power PSU_A off")]        // ApplySubstitutions rewrites off -> false before dispatch
    [InlineData("set PSU_A Power 0")]
    [InlineData("ramp PSU_A 0")]
    [InlineData("bias PSU_A 0")]
    public async Task Retry_does_not_re_energise_a_supply_the_failed_line_was_itself_de_energising(string failedLine)
    {
        // Ruling M, the one carve-out on Ruling L. `power PSU_A off` fails on a live 450 W
        // supply and the hold zeroes it. That zeroing IS the recipe's own intent, arriving early:
        // arming it would make Retry re-energise the magnetron and then re-run the off line, a
        // loop on a supply that keeps failing its off command.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<(float SetPower, bool IsOutputOn, int OnCalls)>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                seen.Add((psu.SetPower, psu.IsOutputOn, psu.OutputOnCalls));
                if (attempt++ == 0) throw new InvalidOperationException("relay write timed out");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                failedLine, new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.True(seen[0].IsOutputOn);                    // live when the line first ran
            Assert.False(seen[1].IsOutputOn);                   // still off when it re-ran — as intended
            Assert.Equal(0f, seen[1].SetPower);
            Assert.Equal(0, psu.OutputOnCalls);                 // nothing re-energised it, ever
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("NOT armed for restoration") && l.Contains("PSU_A"));
            Assert.DoesNotContain(log, l => l.Contains("armed for restoration on Retry"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Retry_restores_a_setpoint_the_hold_zeroed_even_when_its_output_off_failed()
    {
        // A comm timeout on the relay write alone must not discard a snapshot whose setpoint write
        // already SUCCEEDED: nothing would be armed, Retry would restore nothing, and the line would
        // re-run at 0 W with IsOutputOn == true, a shutter open over a supply delivering nothing.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            psu.FailOutputOff = true;
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<(float SetPower, bool IsOutputOn)>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                seen.Add((psu.SetPower, psu.IsOutputOn));
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "set PSU_A Power 450", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.Equal(450f, seen[0].SetPower);
            Assert.Equal(450f, seen[1].SetPower);              // restored BEFORE the line re-ran
            Assert.True(seen[1].IsOutputOn);
            Assert.Equal(450f, psu.SetPower);
            Assert.Equal(1, psu.OutputOnCalls);                // and IsOutputOn was not mistaken for a hand repair
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("armed for restoration on Retry") && l.Contains("PSU_A"));
            Assert.Contains(log, l => l.Contains("output still latched ON"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_cut_that_lands_while_the_resume_is_restoring_power_stops_the_line_re_running()
    {
        // TakeOperatorCut runs ONCE, before the restore loop, and ReEnergiseAfterCut then spends
        // at least 500 ms per supply in its settle delay. An OFF press in that window must still
        // keep the line from re-running over the supply the operator just killed.
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            var psu2 = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, psu);
            Register(thread.Core, DeviceType.Power, psu2);

            // The press lands inside ReEnergiseAfterCut, between its OutputOn and its settle.
            psu.AfterOutputOn = () =>
            {
                if (!psu2.IsOutputOn) return;                  // exactly once
                var second = OperatorCut.From(psu2);
                psu2.OutputOff();
                thread.Core.RequestOperatorCut(second);
            };

            var seen = new List<(bool DcOn, bool RfOn)>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                seen.Add((psu.IsOutputOn, psu2.IsOutputOn));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry, times: 2);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.True(seen[1].DcOn);
            Assert.True(seen[1].RfOn);                         // the deposit never re-ran over a dead RF301
            Assert.Equal(2, thread._pauseEvents.Count);
            Assert.Contains("RF301", thread._pauseEvents[1].Reason);
            // Pause #2 is the abandoned resume, NOT a late cut on a completed step one dwell later.
            Assert.False(thread._pauseEvents[1].StepAlreadyCompleted);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Resume_after_an_output_on_at_a_zeroed_setpoint_restores_the_recipes_power()
    {
        // Ruling N. The most natural operator gesture: press the button that is off, and nothing
        // else. The hold already drove PSU_A's setpoint to 0, so the press latches IsOutputOn over
        // 0 W; the supply is responding, so the confirmation passes (responding is not powered).
        // Keeping 0 W as "the operator's setting" would re-run the deposit growing nothing into a
        // signed run record.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<(float SetPower, bool IsOutputOn)>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                seen.Add((psu.SetPower, psu.IsOutputOn));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                Assert.Equal(0f, psu.SetPower);              // the hold zeroed it, successfully
                Assert.False(psu.IsOutputOn);
                psu.OutputOn();                              // ...and the operator presses ON. Only that.
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.Equal(450f, seen[1].SetPower);            // restored BEFORE the deposit re-ran
            Assert.True(seen[1].IsOutputOn);
            Assert.Equal(450f, psu.SetPower);
            Assert.Equal(2, psu.OutputOnCalls);              // the operator's press, then ours
            Assert.Contains(("Power", 450f), psu.Setpoints);
            Assert.Single(thread._pauseEvents);
            Assert.Contains(DrainAll(pool).Log,
                l => l.Contains("latched on at a 0 W setpoint") && l.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task A_supply_that_answers_the_wire_but_never_took_the_setpoint_is_refused()
    {
        // ConfirmRespondingAsync proves the supply is ANSWERING, not that it has power: at a
        // zeroed setpoint it answers exactly as well as at 450 W. Ruling N keeps that state off
        // the hand-repair path structurally; this is the same statement about our OWN path,
        // where the setpoint we just wrote can be accepted by name and never applied.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<string>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                psu.IgnoreSetpointWrites = true;             // the driver takes the name, drops the value
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);

                // Refused — the handler must pause AGAIN, not deposit over a powerless supply.
                await DecideWhenPaused(thread.Core, FaultDecision.Abort);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.Single(calls);                            // the deposit never re-ran
            Assert.Equal(2, thread._pauseEvents.Count);
            Assert.False(psu.IsOutputOn);                    // our own OutputOn was taken back
            Assert.Contains(DrainAll(pool).Pauses,
                p => p.Reason.Contains("responding is not powered") && p.Reason.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task An_OFF_press_while_the_hold_is_still_running_is_not_reported_as_pausing_the_recipe()
    {
        // IsFaultPaused is true only while a decision is pending, so a press landing while
        // RunFailureHold is still aborting drives and running FAILURE_HOLD must not be reported as
        // "de-energised by the operator during a run — pausing the recipe" for a recipe that is
        // already being paused. It is armed and restored correctly either way.
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            thread.Core.embRecipes = LoadEmbedded(HoldXml("delay 1"));   // a hold window to press into

            thread._stepExecutor = (parts, ct) => throw new InvalidOperationException("drive not responding");

            var press = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!(thread.Core.IsHandlingStepFailure && !thread.Core.IsFaultPaused)
                       && DateTime.UtcNow < deadline)
                    await Task.Delay(5);
                Assert.True(thread.Core.IsHandlingStepFailure, "the hold window never opened");
                Assert.False(thread.Core.IsFaultPaused, "the dialog was already up — window missed");
                DispatchOutputOff(thread, "PSU_A");           // the REAL panel dispatch
            });

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Skip);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "holder Si", new Interpreter(), CancellationToken.None);
            await press; await decide;

            Assert.True(ok);
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("PSU_A de-energised by the operator while the recipe is " +
                "already handling a step failure"));
            Assert.DoesNotContain(log, l => l.Contains("pausing the recipe"));
            // ...and it is still armed and still honestly reported as un-restored by the Skip.
            Assert.Contains(log, l => l.Contains("armed for restoration on Retry") && l.Contains("PSU_A"));
            Assert.Contains(log, l => l.Contains("continuing WITHOUT restoring") && l.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public async Task Abort_after_a_cut_leaves_the_top_level_failure_message_naming_it()
    {
        var (thread, _) = NewThread();
        try
        {
            // A stale reason left by an earlier, unrelated command: the top-level "Recipe failed — ..."
            // message (RecipeFailedMessage, CoreThread.Execute.cs) must not name it as the cause.
            thread.LastCommandErrorForTests = "STALE: an earlier unrelated failure";
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Abort);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.False(ok);
            Assert.NotNull(thread.LastCommandErrorForTests);
            Assert.DoesNotContain("STALE", thread.LastCommandErrorForTests);
            Assert.Contains("de-energised", thread.LastCommandErrorForTests);
            Assert.Contains("PSU_A", thread.LastCommandErrorForTests);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    // F-1: BIAS_1 (a bias analyser) and IonSourcePSU (IONPSU, the ion source, a routine
    // deposition participant) have no scalar Power setpoint, and ApplySetpoint("Power", …) throws
    // on them. Restore must not ask for it, or every Retry is refused and only Skip or Abort can
    // leave the pause.
    //
    // Each of these sets a short FaultPauseTimeoutSeconds: if the resume is refused the handler
    // pauses AGAIN, which the test's decision script never answers — the watchdog turns what
    // would be a hang into a failure.

    [Fact]
    public async Task Retry_re_energises_a_supply_that_has_no_Power_setpoint_ion_source()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("IONPSU").EnergisedWithNoPowerSetpoint();
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<bool>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                seen.Add(psu.IsOutputOn);
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);                                    // the resume was NOT refused
            Assert.Equal(2, seen.Count);                        // and the line actually re-ran
            Assert.True(seen[1]);                               // over a LIVE supply
            Assert.Equal(1, psu.OutputOnCalls);                 // the output is the restored half
            Assert.DoesNotContain(psu.Setpoints, sp => sp.Param == "Power");   // never even asked
            var (log, pauses) = DrainAll(pool);
            Assert.Single(pauses);                              // one pause, not a refusal loop
            Assert.Contains(log, l => l.Contains("IONPSU has no Power setpoint")
                                   && l.Contains("restored its OUTPUT only"));
            Assert.Contains(log, l => l.Contains("Resume: IONPSU re-energised (no Power setpoint to restore)"));
            Assert.DoesNotContain(log, l => l.Contains("Resume refused"));
            Assert.DoesNotContain(log, l => l.Contains("unknown setpoint parameter"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// Wider than an operator cut: Ruling L arms the restoration set from the HOLD's own zeroing,
    /// for ANY pausable failure on a line naming an energised supply, so a plain probe line
    /// `set BIAS_1 Voltage 20` reaches the same restore on an ordinary Retry.
    /// <para>The hold must also not record a HOLD INCOMPLETE problem for a supply that simply has
    /// no such parameter. That report is what the operator is meant to act on, and a fault that is
    /// not one erodes it.</para>
    /// </summary>
    [Fact]
    public async Task An_ordinary_retry_works_on_a_bias_analyser_and_the_hold_reports_no_false_problem()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("BIAS_1").EnergisedWithNoPowerSetpoint();
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<bool>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                seen.Add(psu.IsOutputOn);
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "set BIAS_1 Voltage 20", new Interpreter(), CancellationToken.None);
            await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.True(seen[1]);                               // restored before the line re-ran
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.DoesNotContain(psu.Setpoints, sp => sp.Param == "Power");
            var (log, pauses) = DrainAll(pool);
            var hold = Assert.Single(pauses).Hold;
            // The hold de-energised what it could, and said so honestly…
            Assert.Contains(hold.Done, d => d.Contains("BIAS_1 output off (no Power setpoint on this supply)"));
            // …without inventing a "could not zero BIAS_1's setpoint" HOLD INCOMPLETE problem.
            Assert.DoesNotContain(hold.Problems, p => p.Contains("setpoint"));
            Assert.Contains(log, l => l.Contains("armed for restoration on Retry") && l.Contains("BIAS_1"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// TryUndoOurOwnCommand is reachable for a Power-rejecting supply: OutputOn() has just latched
    /// it ON, and a throwing ApplySetpoint("Power", 0) must not skip the OutputOff(), or the resume
    /// is refused with the supply LEFT COMMANDED ON behind a closed shutter while the re-shown
    /// (frozen, original) hold report tells the operator it is off.
    /// </summary>
    [Fact]
    public async Task A_refused_resume_on_a_supply_with_no_Power_setpoint_leaves_it_de_energised()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("IONPSU").EnergisedWithNoPowerSetpoint();
            psu.MarkUnresponding();                             // never confirms — Retry will refuse
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                await DecideWhenPaused(thread.Core, FaultDecision.Retry);   // refused (unconfirmed)
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);    // operator moves on anyway
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);                                    // Skip carried the run forward
            Assert.Equal(1, psu.OutputOnCalls);                 // we DID command it on…
            Assert.False(psu.IsOutputOn);                       // …and took it back on the refusal
            var (log, pauses) = DrainAll(pool);
            Assert.Equal(2, pauses.Count);                      // refused, then paused again
            Assert.Contains(log, l => l.Contains("de-energised again") && l.Contains("IONPSU"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// Ruling N's "latched on over a zeroed setpoint is not a repair" is a statement about a
    /// POWER setpoint, and these two drivers never write SetPower at all — it reads 0 forever. On a
    /// supply with no Power setpoint the output latch IS the whole of what a hand repair can change
    /// from the panel, so it is the whole of the test. The confirmation gate still runs.
    /// </summary>
    [Fact]
    public async Task A_hand_repair_of_a_supply_with_no_Power_setpoint_is_honoured_not_overwritten()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("IONPSU").EnergisedWithNoPowerSetpoint();
            Register(thread.Core, DeviceType.Power, psu);

            var seen = new List<int>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                seen.Add(psu.OutputOnCalls);
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                psu.OutputOn();                 // the operator's hand repair — the only one possible here
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(2, seen.Count);
            Assert.Equal(1, seen[1]);                           // only the operator's OutputOn — no re-command
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("Resume: IONPSU was re-energised by hand and is confirmed responding"));
            Assert.DoesNotContain(log, l => l.Contains("not a repair"));
            Assert.DoesNotContain(log, l => l.Contains("restoring the recipe's 0 W"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// The same split, on a supply that DOES have a Power setpoint: the undo's zero write throws
    /// (a comm timeout on the wire) and the de-energise must still be attempted. Follows the
    /// precedent RunFailureHold already sets — the output is the load-bearing half, and a failed
    /// setpoint is no reason to leave a supply commanded on.
    /// </summary>
    [Fact]
    public async Task A_refused_resume_de_energises_even_when_the_undo_setpoint_write_throws()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("PSU_A").Energised(450);
            psu.FailZeroSetpointWrite = true;                   // every ApplySetpoint("Power", 0) throws
            psu.MarkUnresponding();                             // never confirms — Retry will refuse
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                await DecideWhenPaused(thread.Core, FaultDecision.Retry);
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);
            });
            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.False(psu.IsOutputOn);                       // the throw did not skip the OutputOff
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("could not zero the setpoint after the refused resume")
                                   && l.Contains("turning the output off anyway"));
            Assert.Contains(log, l => l.Contains("de-energised again") && l.Contains("PSU_A"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    // RfPsuA.ForcePowerOn sends "0 G" through BaseSerialPort.SendTextAsync, which WRITES the
    // command and only then reads the reply; that ReadLine can throw InvalidOperationException
    // (port closed) or IOException on a dropped USB-serial adapter, and SendCommandText catches
    // TimeoutException alone. BasePowerSupply.OutputOn then never reaches `IsOutputOn = true`: the
    // flag reads FALSE about an RF supply that may be live behind a closed shutter. Of the five
    // drivers only RfPsuA can throw there; IonSourcePSU, BIAS_1, PulsePSU and PSU_A all swallow.
    //
    // IsOutputOn is FALSE either way, which is precisely the lie. What these assertions check is
    // that the de-energise was ATTEMPTED (OutputOffCalls) and that the refusal tells the truth
    // about the energy state.

    [Fact]
    public async Task A_restore_whose_OutputOn_threw_after_the_wire_de_energises_before_refusing()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("RF301").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);   // OutputOff #1 — the operator's kill
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                psu.FailOutputOnAfterTheWire = true;               // the adapter drops at this instant
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);                                       // Skip carried the run forward
            Assert.Equal(1, psu.OutputOnCalls);                    // the on-command reached the wire…
            Assert.False(psu.IsOutputOn);                          // …but the flag never latched — the lie
            // Three: the operator's own kill, the hold's zeroing (the cut names the supply, so
            // RunFailureHold de-energises it), and — the one under test — our undo.
            Assert.Equal(3, psu.OutputOffCalls);                   // the output was taken BACK
            var (log, pauses) = DrainAll(pool);
            Assert.Equal(2, pauses.Count);
            Assert.Contains("InvalidOperationException", pauses[1].Reason);
            Assert.Contains(log, l => l.Contains("de-energised again") && l.Contains("RF301"));
            // The de-energise landed, so the refusal must NOT cry wolf about a live supply.
            Assert.DoesNotContain("ENERGISED until it is verified", pauses[1].Reason);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// The same drop, but the link is gone in both directions: the de-energise we owe throws too.
    /// Nothing can prove the supply is off, so the refusal the operator reads must say exactly
    /// that instead of swallowing the failure and leaving "output not confirmed" to imply a dead
    /// supply. If we cannot prove it is off, they are told to treat it as live.
    /// </summary>
    [Fact]
    public async Task A_refusal_that_could_not_de_energise_tells_the_operator_to_treat_it_as_live()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("RF301").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // Set AFTER the cut: CutWhenRunning drives the panel's own OutputOff, which is
                // what the operator successfully pressed before the adapter went away.
                psu.FailOutputOnAfterTheWire = true;
                psu.FailOutputOff = true;
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(1, psu.OutputOnCalls);
            // Operator's kill, the hold's zeroing, then our undo — which is the one that threw.
            Assert.Equal(3, psu.OutputOffCalls);                   // attempted, and it threw
            var (log, pauses) = DrainAll(pool);
            Assert.Equal(2, pauses.Count);
            Assert.Contains("treat RF301 as ENERGISED until it is verified by hand", pauses[1].Reason);
            Assert.Contains(log, l => l.Contains("also failed to de-energise after the refused resume")
                                   && l.Contains("RF301"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// The OTHER side of <c>outputOnAttempted</c>. A throw from the restore's SETPOINT write
    /// lands before <c>OutputOn</c> is ever reached: nothing was commanded on, so nothing is owed,
    /// and the refusal must be a plain return, with no OutputOff and no "de-energised again" log.
    /// </summary>
    [Fact]
    public async Task A_restore_that_threw_BEFORE_OutputOn_refuses_without_de_energising()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("RF301").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);   // OutputOff #1 — the operator's kill
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // The link goes away AFTER the hold has done its zeroing (OutputOff #2), so the
                // restore's own ApplySetpoint("Power", 450) is the first thing to throw.
                psu.FailSetpointWrites = true;
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(0, psu.OutputOnCalls);                    // never commanded on…
            Assert.Equal(2, psu.OutputOffCalls);                   // …so no THIRD OutputOff: the
                                                                   // operator's kill and the hold's
                                                                   // zeroing, and nothing more
            var (log, pauses) = DrainAll(pool);
            Assert.Equal(2, pauses.Count);
            Assert.Contains("TimeoutException", pauses[1].Reason);
            // The UNDO's own line specifically — "de-energised again" alone also appears in the
            // restoration set's absorb note ("… (the hold's own zeroing) — absorbed"), which is
            // a different statement about a different action.
            Assert.DoesNotContain(log, l => l.Contains("de-energised again rather than leave it commanded on"));
            Assert.DoesNotContain("ENERGISED until it is verified", pauses[1].Reason);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// The honesty half at the CONFIRMATION-failure site: the supply latched on, the confirmation
    /// failed, and the de-energise we owe threw as well. The operator must be told to treat it as
    /// live, not handed a bare "output not confirmed" that reads like a dead supply.
    /// </summary>
    [Fact]
    public async Task An_unconfirmed_resume_whose_de_energise_failed_warns_that_it_is_still_live()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("RF301").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                psu.MarkUnresponding();                            // the confirmation will fail…
                psu.FailOutputOff = true;                          // …and so will taking it back
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.True(psu.IsOutputOn);                           // still latched on — the real state
            var pauses = DrainAll(pool).Pauses;
            Assert.Equal(2, pauses.Count);
            Assert.Contains("output not confirmed", pauses[1].Reason);
            Assert.Contains("treat RF301 as ENERGISED until it is verified by hand", pauses[1].Reason);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// The honesty half at the "responding but its setpoint is 0 W" site. The driver takes the
    /// setpoint name and drops the value, so the supply answers perfectly at 0 W; the de-energise
    /// we owe then fails too.
    /// </summary>
    [Fact]
    public async Task A_powerless_but_responding_resume_whose_de_energise_failed_warns_that_it_is_still_live()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 5;
            var psu = new TestPsu("RF301").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var started = new StepStarted();
            thread._stepExecutor = async (parts, ct) => { started.MarkStarted(); await Task.Delay(30_000, ct); return true; };

            var cut = CutWhenRunning(thread.Core, psu, started);
            var decide = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!thread.Core.IsFaultPaused && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(thread.Core.IsFaultPaused, "handler never paused");
                // Set AFTER the hold, so its zeroing lands and SetPower really is 0.
                psu.IgnoreSetpointWrites = true;                   // the name is taken, the value dropped
                psu.FailOutputOff = true;                          // and taking it back fails
                thread.Core.ResolveFault(FaultDecision.Retry);
                await Task.Delay(20);
                await DecideWhenPaused(thread.Core, FaultDecision.Skip);
            });

            bool ok = await thread.ExecuteRecipeWithInterpreter(
                "deposit Si 100 Home", new Interpreter(), CancellationToken.None);
            await cut; await decide;

            Assert.True(ok);
            Assert.Equal(1, psu.OutputOnCalls);
            Assert.True(psu.IsOutputOn);                           // latched on at 0 W and not taken back
            var pauses = DrainAll(pool).Pauses;
            Assert.Equal(2, pauses.Count);
            Assert.Contains("responding is not powered", pauses[1].Reason);
            Assert.Contains("treat RF301 as ENERGISED until it is verified by hand", pauses[1].Reason);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Waits for a condition or fails; a lost decision must surface as a failure, not a
    /// hung suite.</summary>
    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), what);
    }

    /// <summary>Answers Hold and waits until the HELD wait is listening again. Mirrors
    /// <c>OperatorHoldTests.HoldWhenPaused</c>: SetHeldForOperator(true) runs BEFORE the loop
    /// re-enters the wait and installs the hold's own decision source, so resolving on the flag
    /// alone races that window and drops the next decision into a no-op.</summary>
    private static async Task HoldWhenPaused(Core core)
    {
        await WaitUntil(() => core.IsFaultPaused, "handler never paused");
        core.ResolveFault(FaultDecision.Hold);
        await WaitUntil(() => core.IsHeldForOperator, "the hold was never entered");
        await WaitUntil(() => core.IsFaultPaused, "the held wait never installed its decision");
    }

    /// <summary>A held pause has no watchdog, so a lost decision would hang the run rather than
    /// fail it.</summary>
    private static Task<bool> WithCeiling(Task<bool> run) => run.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>
    /// PROBE 1. Kill one supply to cause the pause, then two more while the dialog is up. Every
    /// press must reach the restoration set: a single last-write-wins slot between drains keeps
    /// only the most recent, and on a co-sputter that re-opens a shutter over a dead target for
    /// the rest of the dwell, with a run record claiming a layer half of which never grew.
    /// <para>Driven through the REAL panel dispatch (<c>ProcessPowerCommand</c>'s OutputOff case),
    /// not hand-built OperatorCut values: a test that arms the set directly cannot see the
    /// transport.</para>
    /// </summary>
    [Fact]
    public async Task Three_supplies_killed_between_two_drains_are_all_restored()
    {
        var (thread, pool) = NewThread();
        try
        {
            var dc  = new TestPsu("PSU_A").Energised(450);
            var rf  = new TestPsu("RF301").Energised(300);
            var ion = new TestPsu("IONX").Energised(120);
            Register(thread.Core, DeviceType.Power, dc);
            Register(thread.Core, DeviceType.Power, rf);
            Register(thread.Core, DeviceType.Power, ion);

            var calls = new List<string>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var press = Task.Run(async () =>
            {
                await started.Wait();
                DispatchOutputOff(thread, "PSU_A");          // causes the pause
                await WaitUntil(() => thread.Core.IsFaultPaused, "the cut never paused the run");
                DispatchOutputOff(thread, "RF301");
                DispatchOutputOff(thread, "IONX");
                await Task.Delay(50);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await press;

            Assert.True(ok);
            Assert.Equal(1, dc.OutputOnCalls);
            Assert.Equal(1, rf.OutputOnCalls);
            Assert.Equal(1, ion.OutputOnCalls);
            Assert.Equal(450f, dc.SetPower);
            Assert.Equal(300f, rf.SetPower);
            Assert.Equal(120f, ion.SetPower);
            Assert.Equal(2, calls.Count);                      // the line re-ran once, with power back

            var log = DrainAll(pool).Log;
            foreach (string id in new[] { "PSU_A", "RF301", "IONX" })
                Assert.Contains(log, l => l.Contains(id) && l.Contains("armed for restoration on Retry"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// PROBE 3. Two presses on the SAME supply. The second snapshot (WasOn=false) must not
    /// overwrite the first (WasOn=true, 300 W) before it reaches the consumer, where Ruling K's
    /// "first snapshot wins" lives; ReEnergiseAfterCut would drop it on
    /// <c>if (!cut.WasOn) return null;</c> and the line would re-run over a dead target.
    /// </summary>
    [Fact]
    public async Task A_second_press_on_the_same_supply_cannot_displace_the_live_snapshot()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, rf);

            var calls = new List<string>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var press = Task.Run(async () =>
            {
                await WaitUntil(() => thread.Core.IsFaultPaused, "the step failure never paused");
                DispatchOutputOff(thread, "RF301");            // WasOn=true, 300 W
                DispatchOutputOff(thread, "RF301");            // WasOn=false — must not displace it
                await Task.Delay(50);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await press;

            Assert.True(ok);
            Assert.Equal(1, rf.OutputOnCalls);
            Assert.Equal(300f, rf.SetPower);
            Assert.True(rf.IsOutputOn);

            var log = DrainAll(pool).Log;
            // Twice, not once: every Arm line goes to BOTH sinks this channel carries
            // (SendRecipeLog and SendLogMesssage). What matters is that it was said for ONE
            // press, not two — the second was absorbed, below.
            Assert.Equal(2, log.Count(l => l.Contains("RF301") && l.Contains("armed for restoration on Retry")));
            Assert.Contains(log, l => l.Contains("RF301 de-energised again (the operator, while paused) — absorbed"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// Besides newly-armed and absorbed-as-redundant (Ruling K) there is a THIRD outcome: a press
    /// on a supply that was ALREADY OFF snapshots WasOn=false, and ReEnergiseAfterCut discards
    /// exactly those, so the run record must not call it "armed for restoration on Retry".
    /// </summary>
    [Fact]
    public async Task A_press_on_a_supply_that_was_already_off_is_not_logged_as_armed()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            rf.OutputOff();                                    // already dark when the operator presses
            Register(thread.Core, DeviceType.Power, rf);

            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var press = Task.Run(async () =>
            {
                await WaitUntil(() => thread.Core.IsFaultPaused, "the step failure never paused");
                DispatchOutputOff(thread, "RF301");
                await Task.Delay(50);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await press;

            Assert.True(ok);
            Assert.Equal(0, rf.OutputOnCalls);                 // there was nothing to restore
            var log = DrainAll(pool).Log;
            Assert.DoesNotContain(log, l => l.Contains("RF301") && l.Contains("armed for restoration on Retry"));
            Assert.Contains(log, l => l.Contains("RF301") && l.Contains("ALREADY OFF when the press landed"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// PROBE 2, the Hold variant. <c>WaitHeldWithWarnings</c> parks the recipe thread indefinitely
    /// with no drain at all, while the hold has just unlocked the very panel the operator presses
    /// OFF on. Two supplies killed during one hold must both be restored.
    /// </summary>
    [Fact]
    public async Task Supplies_killed_during_a_hold_are_all_restored_on_resume()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf  = new TestPsu("RF301").Energised(300);
            var ion = new TestPsu("IONX").Energised(120);
            Register(thread.Core, DeviceType.Power, rf);
            Register(thread.Core, DeviceType.Power, ion);

            var calls = new List<string>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                // Manual control is the operator's during the hold.
                Assert.False(thread.Core.IsManualControlGated);
                DispatchOutputOff(thread, "RF301");
                DispatchOutputOff(thread, "IONX");
                await Task.Delay(50);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.True(ok);
            Assert.Equal(1, rf.OutputOnCalls);
            Assert.Equal(1, ion.OutputOnCalls);
            Assert.Equal(300f, rf.SetPower);
            Assert.Equal(120f, ion.SetPower);
            Assert.Equal(2, calls.Count);
            Assert.False(thread.Core.IsHeldForOperator);       // the resume ended the hold
            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("RF301") && l.Contains("armed for restoration on Retry"));
            Assert.Contains(log, l => l.Contains("IONX") && l.Contains("armed for restoration on Retry"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// I2. A resume that ReEnergiseAfterCut REFUSES returns to the held wait and must not clear the
    /// hold. Otherwise the banner goes, the toolbar's Resume button (shown only while
    /// IsHeldForOperator — frmMain.UpdateHeldUi) disappears with it, the tools the operator was
    /// handed are locked again mid-repair, and a watchdog they had deliberately disarmed
    /// auto-aborts the run into FAILURE_PROTOCOL. A hold lasts until a human resumes or aborts; a
    /// refused resume is neither.
    /// </summary>
    [Fact]
    public async Task A_refused_resume_keeps_the_hold_and_does_not_re_arm_the_watchdog()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread.Core.FaultPauseTimeoutSeconds = 2;          // would abort the run if re-armed
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var calls = new List<string>();
            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            bool heldAfterRefusal = false, gatedAfterRefusal = true;
            var operatorAtTheTool = Task.Run(async () =>
            {
                await started.Wait();
                DispatchOutputOff(thread, "PSU_A");
                await HoldWhenPaused(thread.Core);

                psu.Connected = false;                         // the link drops during the repair
                int pauseEventsBefore = thread._pauseEvents.Count;
                thread.Core.ResolveFault(FaultDecision.Retry); // refused: cannot re-energise
                await WaitUntil(() => thread._pauseEvents.Count > pauseEventsBefore,
                    "the refused resume never took its decision");
                await WaitUntil(() => thread.Core.IsFaultPaused, "the refusal never re-entered a wait");

                heldAfterRefusal = thread.Core.IsHeldForOperator;
                gatedAfterRefusal = thread.Core.IsManualControlGated;

                // 3x the watchdog. A re-armed one would abort the run out from under them.
                await Task.Delay(6000);
                Assert.True(thread.Core.IsHeldForOperator, "the watchdog ended the hold");
                thread.Core.ResolveFault(FaultDecision.Abort);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.True(heldAfterRefusal, "the refused resume ended the hold");
            Assert.False(gatedAfterRefusal, "the refused resume locked the machine again");
            Assert.False(ok);                                  // ended on the operator's Abort
            Assert.Single(calls);                              // the line never re-ran over a dead target
            Assert.Equal(["hold", "retry", "abort"], thread._pauseEvents.Select(e => e.Decision));
            Assert.False(thread.Core.IsHeldForOperator);       // the Abort DID end it

            var (log, pauses) = DrainAll(pool);
            Assert.Single(pauses);                             // no dialog re-posted, hence no deadline
            Assert.Contains(log, l => l.StartsWith("Resume refused:"));
            Assert.Contains(log, l => l.StartsWith("The hold STANDS:"));
            Assert.Contains(log, l => l.StartsWith("Hold ended — the run is aborting"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>The other half of I2: a resume that is NOT refused still ends the hold, and says
    /// so. Without this the first assertion could be satisfied by never clearing the flag at
    /// all — which would hand manual control to an operator on a run that is depositing again.
    /// </summary>
    [Fact]
    public async Task A_successful_resume_ends_the_hold_and_locks_the_machine_again()
    {
        var (thread, pool) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);

            var gated = new List<bool>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                gated.Add(thread.Core.IsManualControlGated);   // the re-run must see a locked machine
                return Task.FromResult(true);
            };

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.True(ok);
            Assert.Equal([true], gated);
            Assert.False(thread.Core.IsHeldForOperator);
            Assert.Contains(DrainAll(pool).Log, l => l.StartsWith("Hold ended — the operator resumed the run"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary><c>IsHandlingStepFailure</c> is what <c>ProcessPowerCommand</c> consults to decide
    /// what a panel power-OFF is about to DO, so a depth leaked above zero would tell an operator
    /// pressing OFF during a normal run that their press was "recorded for restoration" when it was
    /// in fact starting a pause. Unreachable today (the scope is a <c>using var</c>), which is why
    /// the run-boundary reset must not quietly omit it.</summary>
    [Fact]
    public void Resetting_the_pause_state_closes_a_leaked_fault_handling_window()
    {
        var (thread, _) = NewThread();
        try
        {
            _ = thread.Core.EnterFaultHandling();              // deliberately never disposed
            Assert.True(thread.Core.IsHandlingStepFailure);

            typeof(CoreThread).GetMethod("ResetFaultPauseState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(thread, []);

            Assert.False(thread.Core.IsHandlingStepFailure);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// RULING S — "first ENERGISED snapshot wins", not merely "first snapshot wins": a later
    /// arrival carrying MORE truth must not be discarded by a rule written to stop an earlier one
    /// being overwritten.
    /// <para>The order that fails without it, all through the real panel dispatch: press OFF on a
    /// supply that is already dark (armed WasOn=false, nothing to restore) → hand-energise it
    /// during the hold, which is the whole point of a hold → press OFF again, which snapshots the
    /// real 300 W. Absorbed as "redundant", that second snapshot is thrown away, the restore hits
    /// <c>if (!cut.WasOn) return null;</c>, and the deposit re-runs over a dead target.</para>
    /// </summary>
    [Fact]
    public async Task An_energised_press_upgrades_a_snapshot_armed_when_the_supply_was_dark()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Dark();
            Register(thread.Core, DeviceType.Power, rf);

            var calls = new List<string>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);

                DispatchOutputOff(thread, "RF301");             // it was already off: arms an empty entry
                DispatchPower(thread, "RF301", "SetPower", 300f);
                DispatchPower(thread, "RF301", "OutputOn");     // the hand repair a hold exists for
                Assert.True(rf.IsOutputOn);
                DispatchOutputOff(thread, "RF301");             // NOW it was live at 300 W
                Assert.False(rf.IsOutputOn);

                await Task.Delay(50);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.True(ok);
            Assert.True(rf.IsOutputOn, "the line re-ran over a dead target");
            Assert.Equal(300f, rf.SetPower);
            Assert.Equal(2, rf.OutputOnCalls);                  // the hand repair, then the restore
            Assert.Equal(2, calls.Count);                       // the line re-ran once, with power back

            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("RF301") && l.Contains("ALREADY OFF when the press landed"));
            Assert.Contains(log, l => l.Contains("RF301") && l.Contains("the armed snapshot is UPGRADED"));
            Assert.DoesNotContain(log, l => l.Contains("RF301") && l.Contains("absorbed"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Ruling S's other half, unchanged and load-bearing: two ENERGISED snapshots keep
    /// the FIRST, because it is the one holding the setpoint the recipe was running at before the
    /// pause touched anything. The upgrade must not become "last write wins" by another
    /// route.</summary>
    [Fact]
    public async Task Two_energised_presses_still_keep_the_first_snapshot()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, rf);

            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                DispatchOutputOff(thread, "RF301");             // WasOn=true at 300 W
                DispatchPower(thread, "RF301", "SetPower", 50f);
                DispatchPower(thread, "RF301", "OutputOn");     // hand-energised, but at 50 W
                DispatchOutputOff(thread, "RF301");             // WasOn=true at 50 W — must NOT win
                await Task.Delay(50);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.True(ok);
            Assert.Equal(300f, rf.SetPower);                    // the recipe's own operating point
            Assert.True(rf.IsOutputOn);
            Assert.Contains(DrainAll(pool).Log,
                l => l.Contains("RF301") && l.Contains("first snapshot kept"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// The hold's own zeroing is recorded as a LIST, and it is populated — not merely typed.
    /// <para>The snapshot is the PRE-zero state, which is the whole point: read after the zeroing
    /// it would say 0 W and off, and a Retry restoring exactly that would re-open the shutter over
    /// a dead target.</para>
    /// </summary>
    [Fact]
    public async Task The_holds_own_zeroing_is_recorded_as_a_populated_list_of_pre_zero_snapshots()
    {
        var (thread, _) = NewThread();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));

            var hold = await thread.RunFailureHold(["power", "PSU_A", "true"]);

            var entry = Assert.Single(hold.Zeroed);
            Assert.Equal("PSU_A", entry.PsuId);
            Assert.Equal(450f, entry.SetPower);          // BEFORE the zero, not after
            Assert.True(entry.WasOn);
            Assert.Equal(0f, psu.SetPower);              // the hold really did zero it
            Assert.False(psu.IsOutputOn);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>A hold that de-energised NOTHING reports an empty list, never null: every consumer
    /// iterates it, and "the hold changed nothing" must not be a second shape to handle.</summary>
    [Fact]
    public async Task A_hold_that_de_energised_nothing_reports_an_empty_list()
    {
        var (thread, _) = NewThread();
        try
        {
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));

            // `deposit` names a SOURCE, not a supply, and no cut named one either.
            var hold = await thread.RunFailureHold(["deposit", "Si", "60", "Home"]);

            Assert.Empty(hold.Zeroed);
            Assert.Contains(hold.Done, d => d.Contains("no power supply resolved from the failed line"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// <c>EndHoldIfAny</c> sits on the decision paths, and an operator STOP landing inside
    /// <c>ReEnergiseAfterCut</c> — which awaits <c>Task.Delay(500, ct)</c> and
    /// <c>ConfirmRespondingAsync</c> on the RUN token — throws straight out of the decision loop
    /// past every one of them, and never through the wait's own catch either.
    /// <para>Not a stale display bit. <c>IsManualControlGated</c> is the mid-recipe gate on jog,
    /// PSU OutputOn, valve and MFC, and <c>ExecuteRecipe</c>'s cancellation catch then runs
    /// <c>RunFailureProtocol()</c> — an EMBEDDED recipe, which never calls
    /// <c>ResetFaultPauseState</c>. A leaked hold would make the machine safe with that gate OFF,
    /// with the HELD banner and the Resume button still on an ended run, and with the agent told
    /// the tool was held. The pause's own <c>using var</c> scope closes it on every exit.</para>
    /// </summary>
    [Fact]
    public async Task An_operator_stop_inside_the_restore_does_not_leak_the_hold_past_the_run()
    {
        var (thread, _) = NewThread();
        using var run = new CancellationTokenSource();
        try
        {
            var psu = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, psu);
            // Stop the run from INSIDE the restore: OutputOn has landed and ReEnergiseAfterCut is
            // about to await its 500 ms settle on the run token.
            psu.AfterOutputOn = () => run.Cancel();

            var started = new StepStarted();
            int attempt = 0;
            thread._stepExecutor = async (parts, ct) =>
            {
                if (attempt++ == 0) { started.MarkStarted(); await Task.Delay(30_000, ct); }
                return true;
            };

            var operatorAtTheTool = Task.Run(async () =>
            {
                await started.Wait();
                DispatchOutputOff(thread, "PSU_A");
                await HoldWhenPaused(thread.Core);
                thread.Core.ResolveFault(FaultDecision.Retry);
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                thread.ExecuteRecipeWithInterpreter("deposit Si 5 Home", new Interpreter(), run.Token)
                    .WaitAsync(TimeSpan.FromSeconds(30)));
            await operatorAtTheTool;

            Assert.False(thread.Core.IsHeldForOperator, "the hold outlived the run");
            Assert.False(thread.Core.IsHandlingStepFailure);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Exit 2 of 4: Skip ends the hold, says so, and takes the operator's tools back.</summary>
    [Fact]
    public async Task Skip_from_a_hold_ends_it_and_locks_the_machine_again()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread._stepExecutor = (parts, ct) => throw new InvalidOperationException("supply not responding");

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                Assert.False(thread.Core.IsManualControlGated);
                thread.Core.ResolveFault(FaultDecision.Skip);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.True(ok);
            Assert.False(thread.Core.IsHeldForOperator);
            Assert.True(thread.Core.IsManualControlGated);      // the run has the machine back
            Assert.Contains(DrainAll(pool).Log, l => l.StartsWith("Hold ended — the operator skipped"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Exit 3 of 4: Abort, asserted for the LOG line and the gate.</summary>
    [Fact]
    public async Task Abort_from_a_hold_ends_it_and_says_so()
    {
        var (thread, pool) = NewThread();
        try
        {
            thread._stepExecutor = (parts, ct) => throw new InvalidOperationException("supply not responding");

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                thread.Core.ResolveFault(FaultDecision.Abort);
            });

            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power PSU_A on", new Interpreter(), CancellationToken.None));
            await operatorAtTheTool;

            Assert.False(ok);
            Assert.False(thread.Core.IsHeldForOperator);
            Assert.Contains(DrainAll(pool).Log, l => l.StartsWith("Hold ended — the run is aborting"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Exit 4 of 4: an operator STOP taken while the machine is held — the wait's own
    /// cancellation path, which returns to no decision site at all.</summary>
    [Fact]
    public async Task An_operator_stop_during_a_hold_ends_it()
    {
        var (thread, _) = NewThread();
        using var run = new CancellationTokenSource();
        try
        {
            thread._stepExecutor = (parts, ct) => throw new InvalidOperationException("supply not responding");

            var operatorAtTheTool = Task.Run(async () =>
            {
                await HoldWhenPaused(thread.Core);
                Assert.False(thread.Core.IsManualControlGated);
                run.Cancel();                                   // the toolbar's Stop, not Abort
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                thread.ExecuteRecipeWithInterpreter("power PSU_A on", new Interpreter(), run.Token)
                    .WaitAsync(TimeSpan.FromSeconds(30)));
            await operatorAtTheTool;

            Assert.False(thread.Core.IsHeldForOperator, "the hold outlived the stopped run");
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// VARIANT A. The hold recipe kills a supply the failed line never names (a <c>deposit</c>
    /// line names a SOURCE); it must still be armed, or Retry re-opens the shutter over a dead
    /// target.
    /// <para>Ruling M does not reach this: that carve-out exists because a Retry RE-RUNS the
    /// failed recipe line, and a FAILURE_HOLD line is never re-run. It is the hold de-energising
    /// the machine, which is exactly what Ruling L says must be armed.</para>
    /// </summary>
    [Fact]
    public async Task A_supply_the_hold_recipe_killed_is_armed_even_when_the_failed_line_names_no_psu()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, rf);
            // The hold script NAMES the supply it kills, because that is where attribution is
            // read from: the sweep sees only a before/after diff, and a supply that trips on its
            // own during the hold window produces an identical one. HoldHookDrive still performs
            // the kill, deterministically, without depending on the real `power` path.
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home", "power RF301 off"));
            Register(thread.Core, DeviceType.Drive,
                new HoldHookDrive("Shutter", () => rf.OutputOff()) { Connected = true });

            var calls = new List<string>();
            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                calls.Add(string.Join(' ', parts));
                if (attempt++ == 0) throw new InvalidOperationException("shutter did not reach position");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);
            Assert.Equal(1, rf.OutputOffCalls);                 // the hold recipe killed it
            Assert.Equal(1, rf.OutputOnCalls);
            Assert.Equal(300f, rf.SetPower);
            Assert.True(rf.IsOutputOn);
            Assert.Equal(2, calls.Count);

            var log = DrainAll(pool).Log;
            Assert.Contains(log, l => l.Contains("RF301") && l.Contains("de-energised by the hold recipe"));
            Assert.Contains(log, l => l.Contains("RF301 de-energised (the hold's own zeroing) — armed for restoration on Retry"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>
    /// VARIANT B. The failed line DOES name the supply, but the hold script has already killed it
    /// when the explicit block resolves it; the snapshot must not read WasOn=false and let the
    /// restore walk past it. <c>set X Power 300</c> does not de-energise, so the Ruling M carve-out
    /// must not fire.
    /// </summary>
    [Fact]
    public async Task A_pre_hold_snapshot_survives_the_hold_recipe_killing_the_same_supply()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, rf);
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
            // Stands in for the hold script's own `power RF301 off` — see HoldHookDrive. It runs
            // BEFORE the explicit zeroing block resolves RF301 from the failed line, the ordering that
            // would hand that block a WasOn=false snapshot.
            Register(thread.Core, DeviceType.Drive,
                new HoldHookDrive("Shutter", () => rf.OutputOff()) { Connected = true });

            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "set RF301 Power 300", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);
            Assert.Equal(1, rf.OutputOnCalls);
            Assert.Equal(300f, rf.SetPower);                    // the PRE-hold operating point
            Assert.True(rf.IsOutputOn);
            Assert.Contains(DrainAll(pool).Log,
                l => l.Contains("RF301 de-energised (the hold's own zeroing) — armed for restoration on Retry"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>A hold that kills TWO supplies arms both: the producer must not be a single slot
    /// behind the list.</summary>
    [Fact]
    public async Task A_hold_recipe_that_kills_two_supplies_arms_and_restores_both()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            var dc = new TestPsu("PSU_A").Energised(450);
            Register(thread.Core, DeviceType.Power, rf);
            Register(thread.Core, DeviceType.Power, dc);
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
            // Stands in for a hold script that kills BOTH — see HoldHookDrive.
            Register(thread.Core, DeviceType.Drive,
                new HoldHookDrive("Shutter", () => { rf.OutputOff(); dc.OutputOff(); }) { Connected = true });

            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                if (attempt++ == 0) throw new InvalidOperationException("shutter did not reach position");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "deposit Si 5 Home", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);
            Assert.Equal(1, rf.OutputOnCalls);
            Assert.Equal(1, dc.OutputOnCalls);                  // the one a single slot lost
            Assert.Equal(300f, rf.SetPower);
            Assert.Equal(450f, dc.SetPower);

            var log = DrainAll(pool).Log;
            foreach (string id in new[] { "RF301", "PSU_A" })
                Assert.Contains(log, l => l.Contains($"{id} de-energised (the hold's own zeroing) — armed for restoration on Retry"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    /// <summary>Ruling M still applies to the failed RECIPE line, which a Retry re-runs: a hold
    /// that kills the supply a <c>power X off</c> line was itself turning off must stay carved
    /// out, or the Retry energises it only to run the line that kills it again.</summary>
    [Fact]
    public async Task The_ruling_M_carve_out_survives_the_hold_recipe_killing_the_supply()
    {
        var (thread, pool) = NewThread();
        try
        {
            var rf = new TestPsu("RF301").Energised(300);
            Register(thread.Core, DeviceType.Power, rf);
            thread.Core.embRecipes = LoadEmbedded(HoldXml("shutter Home"));
            // Stands in for the hold script's own `power RF301 off` — see HoldHookDrive.
            Register(thread.Core, DeviceType.Drive,
                new HoldHookDrive("Shutter", () => rf.OutputOff()) { Connected = true });

            int attempt = 0;
            thread._stepExecutor = (parts, ct) =>
            {
                if (attempt++ == 0) throw new InvalidOperationException("supply not responding");
                return Task.FromResult(true);
            };

            var decide = DecideWhenPaused(thread.Core, FaultDecision.Retry);
            bool ok = await WithCeiling(thread.ExecuteRecipeWithInterpreter(
                "power RF301 off", new Interpreter(), CancellationToken.None));
            await decide;

            Assert.True(ok);
            Assert.Equal(0, rf.OutputOnCalls);                  // never energised to be turned off again
            Assert.Contains(DrainAll(pool).Log,
                l => l.Contains("NOT armed for restoration"));
        }
        finally { thread.Core.EndRecipeSlot(); }
    }
}
