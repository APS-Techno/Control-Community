using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl.Core;

/// <summary>Operator's answer to a fault pause. <c>Hold</c> is not an answer so much as a
/// deferral: it closes the dialog, disarms the watchdog and unlocks manual hardware control so
/// the operator can actually fix the machine they were just asked about, then waits — with no
/// deadline — for Resume (Retry), Skip or Abort. The run still owns the machine
/// (<c>IsRecipeRunning</c> stays true), so Start remains refused and no second recipe can begin.</summary>
public enum FaultDecision { Retry, Skip, Abort, Hold }

/// <summary>
/// What the soft hold ACTUALLY did, and what it could not do. The dialog renders both lists
/// verbatim and asserts nothing of its own.
/// <para><paramref name="Done"/>: the actions that ran, plus the honest "there was nothing to
/// do" statements. <paramref name="Problems"/>: what failed or was missing — rendered red as
/// HOLD INCOMPLETE, because each one means part of the machine was not held.</para>
/// <para><paramref name="Zeroed"/>: the pre-zero snapshot of EVERY supply this hold actually
/// de-energised — captured exactly like an operator cut (<see cref="OperatorCut.From"/>) BEFORE
/// the zero, so a Retry can restore each through the same restoration set a cut populates.
/// Empty when the hold changed NOTHING (no PSU named by the failed line, none registered, or
/// both the setpoint write and the output-off threw). A PARTIAL zeroing still fills it: the
/// setpoint write landing is a proven change to the operating point whatever the relay did, and
/// a resume that does not put it back leaves the run depositing at 0 W.</para>
/// <para>A list although <c>RunFailureHold</c> zeroes at most one supply today: a hold that
/// zeroes several supplies (a co-sputter tool) must deliver every snapshot to <c>Arm</c>, and a
/// single slot would lose the rest with no line anywhere.</para>
/// <para>Whether the hold's own <c>OutputOff</c> landed is deliberately NOT recorded: a supply
/// at a zeroed setpoint is not repaired whatever its output latch says, so
/// <c>ReEnergiseAfterCut</c> decides on the supply's OWN setpoint.</para>
/// </summary>
public sealed record HoldReport(IReadOnlyList<string> Done, IReadOnlyList<string> Problems,
    IReadOnlyList<OperatorCut>? Zeroed = null)
{
    /// <summary>Never null for a consumer: an omitted list reads as "this hold de-energised
    /// nothing", which is what every caller that does not pass one means.</summary>
    public IReadOnlyList<OperatorCut> Zeroed { get; init; } = Zeroed ?? [];
}

/// <summary>What the pause dialog shows. <paramref name="Hold"/> is the soft hold's own report;
/// the dialog lists its actions and, in red, its problems.
/// <para><paramref name="DeadlineUtc"/> is when the watchdog will abort, or NULL for a wait that
/// cannot time out (a held pause). Nullable on purpose: <c>started + timeout</c> with
/// <see cref="Timeout.InfiniteTimeSpan"/> yields a deadline one millisecond in the PAST, which
/// the dialog would render as "Watchdog expired — aborting." on a wait that can never
/// expire.</para></summary>
public sealed record FaultPauseInfo(string Step, string Reason, DateTime? DeadlineUtc, HoldReport Hold);

/// <summary>When a worker dwell (deposit / warmup / pwait) started and how long it was meant to
/// run. Set by the StepProgressReporter hook in Core.LoadWorker; read by the fault-pause handler
/// to resume a deposit's remaining dwell on Retry.</summary>
internal sealed record DwellMark(string Description, DateTime StartedUtc, double Seconds);

/// <summary>
/// A manual power-OFF the operator pressed on a PSU panel while a recipe was running. The panel's
/// OFF is deliberately never gated (<c>CoreThread.Execute.ProcessPowerCommand</c> rejects every
/// other manual command during a run but lets <c>OutputOff</c> and <c>Reconnect</c> through —
/// the kill switch must not be behind a dialog), so this record is what tells the RECIPE the
/// plasma just died under it.
/// <para><paramref name="SetPower"/> and <paramref name="WasOn"/> are the supply's state as it was
/// an instant BEFORE the output dropped; a resume restores exactly them. Snapshot with
/// <see cref="From"/> before calling <c>OutputOff</c> — afterwards both read as "off" and the
/// resume would silently energise nothing.</para>
/// </summary>
public sealed record OperatorCut(string PsuId, float SetPower, bool WasOn, DateTime AtUtc)
{
    /// <summary>Captures the supply's live state. Call this BEFORE the power-off.</summary>
    public static OperatorCut From(BasePowerSupply psu)
    {
        ArgumentNullException.ThrowIfNull(psu);
        return new OperatorCut(psu.Params.ID, psu.SetPower, psu.IsOutputOn, DateTime.UtcNow);
    }
}

public partial class Core
{
    // Same discipline as _pendingMessageAck: installed on the recipe thread, resolved on the UI
    // thread (or by the watchdog), all swaps through Interlocked.Exchange.
    private TaskCompletionSource<FaultDecision>? _pendingFaultDecision;

    /// <summary>Watchdog for a fault pause, seconds. 0 disables pausing entirely (a failed
    /// step aborts into FAILURE_PROTOCOL). Read from Worker.xml
    /// <c>faultPauseTimeoutSeconds</c>; default 600.</summary>
    public int FaultPauseTimeoutSeconds { get; set; } = 600;

    /// <summary>Largest watchdog accepted from config: 24 h. Beyond ~2,147,483 s
    /// <c>Task.Delay</c> throws <see cref="ArgumentOutOfRangeException"/> — AFTER the pause's
    /// <c>finally</c> has already ended the pause, aborting the run.</summary>
    public const int MaxFaultPauseTimeoutSeconds = 86_400;

    /// <summary>Applies the configured watchdog, clamped to [0, <see cref="MaxFaultPauseTimeoutSeconds"/>],
    /// and says so when the configured value was out of range.</summary>
    internal void ApplyFaultPauseTimeout(int configuredSeconds)
    {
        FaultPauseTimeoutSeconds = Math.Clamp(configuredSeconds, 0, MaxFaultPauseTimeoutSeconds);
        if (FaultPauseTimeoutSeconds != configuredSeconds)
            SendLogMesssage("Core",
                $"faultPauseTimeoutSeconds={configuredSeconds} is out of range (0..{MaxFaultPauseTimeoutSeconds}); " +
                $"clamped to {FaultPauseTimeoutSeconds} s");

        SendLogMesssage("Core", FaultPauseTimeoutSeconds == 0
            ? "Fault pause disabled (faultPauseTimeoutSeconds=0): step failures abort into FAILURE_PROTOCOL"
            : $"Fault pause watchdog: {FaultPauseTimeoutSeconds} s");
    }

    /// <summary>How long a hold may run before it starts warning, seconds. 0 disables the
    /// warning entirely. Read from Worker.xml <c>heldWarningSeconds</c>; default 900.
    /// <para>A hold NEVER auto-aborts — that is the whole point of it, and the watchdog is
    /// disarmed for its duration. This is therefore the ONLY thing that makes an unattended hold
    /// visible: whoever began it (operator, agent or MCP bridge) may walk away from a run that
    /// is parked mid-recipe with the machine hand-drivable, and nothing else in the system will
    /// ever say so. It changes no machine state and never ends the run.</para></summary>
    public int HeldWarningSeconds { get; set; } = 900;

    /// <summary>Applies the configured hold warning interval, clamped to
    /// [0, <see cref="MaxFaultPauseTimeoutSeconds"/>] for the same reason the watchdog is
    /// (<c>Task.Delay</c> throws beyond ~2,147,483 s). Called by <c>LoadWorker</c>.</summary>
    internal void ApplyHeldWarningTimeout(int configuredSeconds)
    {
        HeldWarningSeconds = Math.Clamp(configuredSeconds, 0, MaxFaultPauseTimeoutSeconds);
        if (HeldWarningSeconds != configuredSeconds)
            SendLogMesssage("Core",
                $"heldWarningSeconds={configuredSeconds} is out of range (0..{MaxFaultPauseTimeoutSeconds}); " +
                $"clamped to {HeldWarningSeconds} s");

        SendLogMesssage("Core", HeldWarningSeconds == 0
            ? "Hold warnings disabled (heldWarningSeconds=0): an unattended hold will be silent"
            : $"Hold warning interval: {HeldWarningSeconds} s");
    }

    /// <summary>True while a recipe is waiting for a fault decision.</summary>
    public bool IsFaultPaused => Volatile.Read(ref _pendingFaultDecision) is not null;

    private int _faultHandlingDepth;

    /// <summary>
    /// True from the moment a pausable step failure STARTS being handled — the soft hold is
    /// still aborting drives and running FAILURE_HOLD — until the operator's decision has been
    /// acted on.
    /// <para><see cref="IsFaultPaused"/> is a strict subset of this: it is true only while a
    /// decision is actually pending. The hold window in between belongs to neither "running
    /// normally" nor "waiting for the operator", and a panel power-OFF landing in it is armed
    /// into the restoration set exactly like one landing while the dialog is up — so anything
    /// that reports what a cut is about to DO must consult this, not just IsFaultPaused, or it
    /// tells the operator the press is "pausing the recipe" that is already being paused.</para>
    /// </summary>
    public bool IsHandlingStepFailure => Volatile.Read(ref _faultHandlingDepth) > 0;

    /// <summary>Recipe thread: opens the fault-handling window; disposing the returned scope
    /// closes it. COUNTED, not a bool: an overrunning hold can let a second failure start its
    /// own handling, and a bool cleared by whichever finished first would report "running
    /// normally" while the other is still holding the machine (the same hazard
    /// <c>_holdInFlight</c> documents).</summary>
    internal FaultHandlingScope EnterFaultHandling()
    {
        Interlocked.Increment(ref _faultHandlingDepth);
        return new FaultHandlingScope(this);
    }

    /// <summary>
    /// Closes the window, and with it the hold: the hold cannot outlive the pause that created
    /// it. The scope is a <c>using var</c>, so it runs on every exit <c>ExecuteWithFaultPause</c>
    /// has, including the ones no decision path can see — e.g. an operator STOP inside
    /// <c>ReEnergiseAfterCut</c> throwing <see cref="OperationCanceledException"/> past
    /// <c>EndHoldIfAny</c>. Otherwise manual control would stay ungated
    /// (<c>IsManualControlGated</c>) through FAILURE_PROTOCOL, an embedded recipe that never
    /// calls <c>ResetFaultPauseState</c>.
    /// <para>Only at depth 0: an overrunning hold can let a SECOND failure open its own window,
    /// and the inner one closing must not take the machine back from an operator the outer one is
    /// still holding it for.</para>
    /// <para>Silent, deliberately. <c>EndHoldIfAny</c> announces a hold that a HUMAN ended; this
    /// is the run itself ending underneath one, which the abort/stop lines already narrate.</para>
    /// </summary>
    private void ExitFaultHandling()
    {
        if (Interlocked.Decrement(ref _faultHandlingDepth) <= 0)
            SetHeldForOperator(false);
    }

    /// <summary>Forces the fault-handling window shut at a run boundary
    /// (<c>ResetFaultPauseState</c>), alongside <see cref="ClearOperatorCut"/> and
    /// <see cref="SetHeldForOperator"/>.
    /// <para>A backstop: <see cref="IsHandlingStepFailure"/> is what <c>ProcessPowerCommand</c>
    /// consults to decide what a panel power-OFF is about to do, so a depth stuck above zero
    /// would tell an operator pressing OFF during a normal run that their press was "recorded
    /// for restoration" when it was in fact starting a pause.</para></summary>
    internal void ResetFaultHandlingDepth() => Interlocked.Exchange(ref _faultHandlingDepth, 0);

    /// <summary>Scope returned by <see cref="EnterFaultHandling"/>; closes on every exit the
    /// block has — the <c>break</c> that re-runs the step, every <c>return</c>, and the
    /// rethrown original fault.</summary>
    internal readonly struct FaultHandlingScope(Core core) : IDisposable
    {
        public void Dispose() => core.ExitFaultHandling();
    }

    /// <summary>Installs the TCS the handler will await. Returns the previous one if a stale
    /// pause was still pending (caller cancels it).</summary>
    public TaskCompletionSource<FaultDecision>? SetPendingFaultDecision(TaskCompletionSource<FaultDecision> tcs)
    {
        ArgumentNullException.ThrowIfNull(tcs);
        return Interlocked.Exchange(ref _pendingFaultDecision, tcs);
    }

    /// <summary>UI thread: the operator pressed Retry / Skip / Abort. No-op when nothing is pending
    /// (the watchdog or the recipe end got there first).</summary>
    public void ResolveFault(FaultDecision decision)
    {
        var tcs = Interlocked.Exchange(ref _pendingFaultDecision, null);
        tcs?.TrySetResult(decision);
    }

    /// <summary>Cancels a pending decision so a dialog left open cannot act on the next run.
    /// Called from EndRecipeSlot.</summary>
    public void CancelPendingFaultDecision()
    {
        var tcs = Interlocked.Exchange(ref _pendingFaultDecision, null);
        tcs?.TrySetCanceled();
    }

    /// <summary>Raised with the pause info when a fault pause opens and with null when it ends;
    /// same moments as the GUI messages below. Handlers must not block.</summary>
    public event Action<FaultPauseInfo?>? FaultPauseChanged;

    public void SendFaultPauseToUI(FaultPauseInfo info)
    {
        guiwriter.TryWrite(new ValMsg<FaultPauseInfo>(MsgType.Command, DeviceType.System, "FaultPause", info));
        RaiseFaultPauseChanged(info);
    }

    /// <summary>Asks the GUI to close the fault-pause dialog (decision taken, watchdog fired,
    /// or the recipe ended).</summary>
    public void SendFaultPauseEndToUI()
    {
        guiwriter.TryWrite(new ValMsg<string>(MsgType.Command, DeviceType.System, "FaultPauseEnd", ""));
        RaiseFaultPauseChanged(null);
    }

    private void RaiseFaultPauseChanged(FaultPauseInfo? info)
    {
        try { FaultPauseChanged?.Invoke(info); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"FaultPauseChanged handler threw: {ex.Message}"); }
    }

    /// <summary>
    /// Every operator cut the recipe thread has not consumed yet — a QUEUE, not a slot.
    /// <para>A last-write-wins slot discards every press but the most recent between two
    /// drains, and Hold parks the recipe thread with no drain at all while the PSU panel is
    /// unlocked: several supplies killed during one pause, or OFF pressed twice on one supply
    /// (the second snapshot, WasOn=false, overwriting the first), would lose presses and
    /// <c>ReEnergiseAfterCut</c> would re-run the line over a dead target.</para>
    /// <para><b>A queue rather than a keyed map, deliberately.</b> This runs on the HW thread,
    /// which has no pause context and cannot write to the recipe log, so a press deduplicated
    /// here would vanish with no line anywhere. FIFO delivers every press, in arrival order, to
    /// <c>Arm</c>, which is the ONE place that applies "first snapshot wins" and the one place
    /// that can say so in the run log.</para>
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<OperatorCut> _operatorCuts = new();

    /// <summary>The step the recipe thread is running right now, cancellable on its own so an
    /// operator cut can interrupt a 30-minute dwell without cancelling the RUN (which is what
    /// Stop does). Installed and cleared per attempt by <c>ExecuteWithFaultPause</c>.</summary>
    private CancellationTokenSource? _stepCts;

    /// <summary>Recipe thread: publishes the token source for the attempt about to run, or clears
    /// it (null) once the attempt is over.</summary>
    internal void RegisterStepCancellation(CancellationTokenSource? cts)
        => Volatile.Write(ref _stepCts, cts);

    /// <summary>
    /// HW thread: the operator de-energised a supply while a recipe was running. Records the cut
    /// and cancels the in-flight step so the handler can hold the machine and ask what to do.
    /// <para>Returns false — recording nothing — when no recipe is running, so a routine panel
    /// power-off between runs cannot arm a pause in the next one.</para>
    /// </summary>
    public bool RequestOperatorCut(OperatorCut cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        if (!IsRecipeRunning) return false;
        _operatorCuts.Enqueue(cut);
        // May be null: no attempt is in flight right now (between two steps, or this one just
        // finished and unregistered). Either way the cut is not lost: ExecuteWithFaultPause
        // takes it in the OperationCanceledException handler when Cancel() below actually
        // interrupts the step, OR — if this races the step's own successful completion, or the
        // cut simply arrives between attempts — the very next check ExecuteWithFaultPause makes
        // after a step reports success takes it and turns that success into a pause instead. It
        // is dropped only by ResetFaultPauseState, at the NEXT run's start, if nothing in this
        // one ever consumed it (e.g. the recipe ended on this same step).
        Volatile.Read(ref _stepCts)?.Cancel();
        return true;
    }

    /// <summary>Recipe thread: consumes the OLDEST pending cut, if any. Reading it removes it.
    /// Used where the handler needs the single cut that CAUSED this pause — the press that
    /// cancelled the step, or the one that arrived just as it succeeded. Everything that arrived
    /// behind it stays queued for <see cref="TakeAllOperatorCuts"/>.</summary>
    internal OperatorCut? TakeOperatorCut() => _operatorCuts.TryDequeue(out var cut) ? cut : null;

    /// <summary>Recipe thread: drains EVERY pending cut, oldest first, for the pause handler's
    /// restoration set.</summary>
    internal List<OperatorCut> TakeAllOperatorCuts()
    {
        var all = new List<OperatorCut>();
        while (_operatorCuts.TryDequeue(out var cut)) all.Add(cut);
        return all;
    }

    /// <summary>Drops every cut nobody consumed, so none can leak into the next run.</summary>
    internal void ClearOperatorCut() { while (_operatorCuts.TryDequeue(out _)) { } }

    private int _heldForOperator;

    /// <summary>True while a fault pause has been handed to the operator: the dialog is closed, the
    /// watchdog is disarmed, and manual hardware control is unlocked. The RUN still owns the machine
    /// (<see cref="IsRecipeRunning"/> stays true), so Start is still refused.</summary>
    public bool IsHeldForOperator => Volatile.Read(ref _heldForOperator) == 1;

    internal void SetHeldForOperator(bool held) => Volatile.Write(ref _heldForOperator, held ? 1 : 0);

    /// <summary>
    /// What the manual command paths check — NOT <see cref="IsRecipeRunning"/>.
    /// <para>A running recipe gates manual jog / PSU / valve / MFC / pump commands. A HELD recipe
    /// does not: the operator was just asked to fix something, and refusing them every tool for
    /// fixing it is what this flag exists to stop.</para>
    /// </summary>
    public bool IsManualControlGated => IsRecipeRunning && !IsHeldForOperator;

    private readonly List<string> _heldManualCommands = [];
    private int _heldManualCommandsDropped;

    /// <summary>Ceiling on the recorded list. It is written verbatim into the run's
    /// <c>.run.json</c> sidecar, and a hold has no deadline, so an unattended one left overnight
    /// with a stuck panel could otherwise grow the sidecar without bound. 200 lines covers any
    /// real hand repair by a wide margin (~8 KB) while keeping the record a bounded
    /// artefact.</summary>
    internal const int MaxHeldManualCommands = 200;

    /// <summary>Records a manual command that was let through BECAUSE the machine is held, so
    /// the run record does not show an uninterrupted deposition that was in fact hand-driven.
    /// <para>These land in the run's <c>PauseEvent</c> list, which <c>Core.WriteRunRecord</c>
    /// writes into the <c>&lt;stem&gt;.run.json</c> sidecar in the run's dated data folder. That
    /// file is NOT the <c>.depo.json</c> and is not signed, fingerprinted or sealed — it is a
    /// best-effort JSON sidecar whose write failure is logged and swallowed; the snapshot names
    /// it in <c>outcome.runRecord</c>. It is written whether or not recipe logging is on.</para>
    /// <para>Callers must record only what ACTUALLY reached the hardware — after an interlock has
    /// had its say, after the Connected guard, after the command has run. A record that asserts a
    /// hand action which never happened is worse than one that omits it.</para>
    /// <para>Past <see cref="MaxHeldManualCommands"/> the EARLIEST entries are kept and later ones
    /// counted, not stored: the start of a repair is what explains the rest of it, and the count is
    /// surfaced by <see cref="TakeHeldManualCommands"/> so the truncation is never silent.</para>
    /// </summary>
    internal void NoteHeldManualCommand(string source, string text)
    {
        lock (_heldManualCommands)
        {
            if (_heldManualCommands.Count >= MaxHeldManualCommands)
            {
                _heldManualCommandsDropped++;
                return;
            }
            _heldManualCommands.Add($"{DateTime.UtcNow:HH:mm:ss} {source}: {text}");
        }
    }

    /// <summary>Drains what the operator did during this hold. When the cap was hit, the drained
    /// list ends with an explicit truncation line naming how many commands were dropped — a
    /// silently truncated evidence list is worse than a short one, because nothing downstream
    /// could tell the difference between "they did four things" and "they did four hundred".
    /// </summary>
    internal IReadOnlyList<string> TakeHeldManualCommands()
    {
        lock (_heldManualCommands)
        {
            var copy = new List<string>(_heldManualCommands);
            if (_heldManualCommandsDropped > 0)
                copy.Add($"… TRUNCATED: {_heldManualCommandsDropped} further manual command(s) " +
                         $"were performed but not recorded (cap {MaxHeldManualCommands})");
            _heldManualCommands.Clear();
            _heldManualCommandsDropped = 0;
            return copy;
        }
    }

    /// <summary>Most recent worker dwell window; null until the first one. Cleared by the
    /// fault-pause handler once consumed.</summary>
    internal DwellMark? LastDwell { get; set; }

    internal void NoteDwellStarted(string description, double seconds)
        => LastDwell = new DwellMark(description, DateTime.UtcNow, seconds);
}
