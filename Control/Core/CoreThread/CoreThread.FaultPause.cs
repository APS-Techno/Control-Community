using System.Globalization;
using System.Runtime.ExceptionServices;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Core.Recipe;

namespace APS.SystemControl.Core;

/// <summary>
/// Fault pause. A pausable step failure in a USER recipe (see <see cref="FaultClassifier"/>)
/// soft-holds the machine — FAILURE_HOLD (shutters closed), failed supply zeroed — reports
/// <see cref="RecipeState.Paused"/>, and waits for Retry / Skip / Abort / Hold from the operator
/// or the watchdog. Retry re-runs the line (a deposit resumes its remaining dwell), Skip records
/// the line and continues, Abort surfaces the original failure so the existing catch runs
/// FAILURE_PROTOCOL.
/// <para>Hold is the fourth, and the odd one out: it decides nothing. The pause holds
/// <c>IsRecipeRunning</c> true for its whole duration, which refuses the operator every manual
/// tool for fixing the supply. Hold closes the dialog,
/// disarms the watchdog, unlocks manual control and PARKS the recipe thread exactly where it
/// stands — nothing unwound, no step re-run, the soft hold's actions left in place — then waits,
/// without a deadline, for one of the other three. The run still owns the machine, so Start stays
/// refused; <c>heldWarningSeconds</c> is what keeps an unattended hold from being silent.</para> Spec: docs/superpowers/specs/2026-09-04-fault-pause-design.md.
/// </summary>
partial class CoreThread
{
    /// <summary>Every pause in the run in progress; written into the run record.</summary>
    internal readonly List<PauseEvent> _pauseEvents = [];

    internal readonly List<string> _skippedSteps = [];

    /// <summary>1-based step number of the line being executed (set by the step loop) so the
    /// Paused progress message points at the right line.</summary>
    internal int _currentStepNumber;

    /// <summary>Test seam: replaces <see cref="ExecuteResolvedParts"/> for the ATTEMPT only.
    /// The hold recipe always runs through the real executor.</summary>
    internal Func<string[], CancellationToken, Task<bool>>? _stepExecutor;

    /// <summary>Test seam. Settable so a test can stage the driver reason a real
    /// <see cref="ExecuteRecipeLine"/> would have recorded before returning false — the
    /// classifier's interlock probe reads it.</summary>
    internal string? LastCommandErrorForTests
    {
        get => _lastCommandError;
        set => _lastCommandError = value;
    }
    internal int RecipeTotalStepsForTests => _recipeTotalSteps;

    private const string HoldRecipeName = "FAILURE_HOLD";

    /// <summary>How long the handler WAITS for the hold before giving up on it and pausing
    /// anyway. The hold itself keeps running; only the wait is bounded. Test seam.</summary>
    internal TimeSpan _holdBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long the handler then waits for an overrunning hold to FINISH, once the
    /// operator has decided, before resuming the recipe anyway. Test seam.</summary>
    internal TimeSpan _holdDrainBudget = TimeSpan.FromSeconds(120);

    /// <summary>
    /// True inside the FAILURE_HOLD's own async flow, and nowhere else.
    /// <para>Not <c>_continueOnError</c>: when the hold outruns its wait budget the handler
    /// restores the outer recipe's bookkeeping while the hold is STILL inside its line loop. A
    /// hold line routed on <c>_continueOnError</c> would then enter
    /// <see cref="ExecuteWithFaultPause"/> and start a NESTED pause, whose
    /// <c>SetPendingFaultDecision</c> cancels the operator's pending decision and aborts the run
    /// as "operator-stop"; a non-pausable hold failure would ESCAPE the hold and skip the
    /// remaining shutter closes on a multi-line hold.</para>
    /// <para><b>Flow-scoped, not thread-scoped, and deliberately not a single <c>bool</c>.</b>
    /// After a drain timeout a second failure starts hold #2 while hold #1 is still in its line
    /// loop, so each hold needs its own value. An <see cref="AsyncLocal{T}"/> gives
    /// that: <c>AsyncMethodBuilderCore.Start</c> snapshots the caller's
    /// ExecutionContext and restores it when the state machine's first <c>MoveNext</c> returns,
    /// so a write inside an <c>async</c> method never escapes to its caller, while every
    /// continuation and nested call inherits it. <c>ConfigureAwait(false)</c> changes
    /// SynchronizationContext capture only; ExecutionContext always flows. The outer recipe and
    /// a still-running hold can therefore sit on the SAME pool thread with opposite values.</para>
    /// <para>Job 1: <c>ExecuteResolvedPartsGuarded</c> treats it exactly like
    /// <c>_continueOnError</c> — record the failure, continue with the next line, never pause.
    /// Job 2: <see cref="SendRecipeProgress"/> drops the hold's Running updates, which are
    /// emitted under the OUTER recipe's name and made the Process panel look like the run had
    /// restarted (and <c>recipe_status</c> report Running on <c>shutter Home</c>) at exactly
    /// the moment the operator is judging machine state.</para>
    /// </summary>
    private readonly AsyncLocal<bool> _holdInFlight = new();

    /// <summary>The hold's own failure sink. Separate from <c>_failureProtocolFailures</c>
    /// because the hold runs INSIDE a user recipe and, on an overrun, outlives the handler's
    /// bookkeeping restore, so a late hold line would append to the outer recipe's list.
    /// Written from the hold's flow, read from the handler's: every access is locked.</summary>
    private readonly List<string> _holdFailures = [];

    /// <summary>An overrunning hold, parked for <see cref="DrainUnfinishedHold"/> to wait on
    /// once the operator has decided. Null whenever the hold finished in time.</summary>
    private Task? _unfinishedHold;

    /// <summary>
    /// Runs one resolved line with fault-pause semantics. Returns true when the line succeeded
    /// (possibly after a Retry) or was skipped; returns false / rethrows when the operator or
    /// the watchdog chose Abort, or when the failure is not pausable — in which case the
    /// caller's existing failure path (FAILURE_PROTOCOL) runs unchanged.
    /// </summary>
    internal async Task<bool> ExecuteWithFaultPause(string[] parts, CancellationToken cancellationToken)
    {
        while (true)
        {
            var attemptStartedUtc = DateTime.UtcNow;
            Exception? fault = null;
            OperatorCut? cut = null;
            // True only when the cut arrived AFTER the step it interrupted had already
            // succeeded (the late-cut drain below). The step did not fail: Retry must
            // re-energise and move on rather than re-run the line, Skip must not be recorded
            // as a skip, and the pause reason and PauseEvent must say so.
            bool stepAlreadySucceeded = false;
            bool ok;
            // Per-attempt source so an operator cut can interrupt THIS step without cancelling the run.
            // Registered with Core only for the duration of the attempt: a cut that lands between two
            // steps must not cancel the next one.
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            core!.RegisterStepCancellation(stepCts);
            try
            {
                ok = _stepExecutor is not null
                    ? await _stepExecutor(parts, stepCts.Token).ConfigureAwait(false)
                    : await ExecuteResolvedParts(parts, stepCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // An operator STOP cancels the run token and must still abort the run.
                // Only a cut — which cancels the step token alone — becomes a pausable failure.
                cut = core.TakeOperatorCut();
                if (cancellationToken.IsCancellationRequested || cut is null) throw;
                ok = false;
            }
            catch (Exception ex) { fault = ex; ok = false; }
            finally { core.RegisterStepCancellation(null); }

            if (ok)
            {
                // RequestOperatorCut races the step's own completion: it can read a still-live
                // _stepCts and Cancel() a source whose linked try has ALREADY produced
                // ok == true, in which case the OperationCanceledException catch above never
                // runs and the cut would sit unconsumed. The recipe would then carry on into the
                // NEXT step with the supply dead and no pause. Check for that late arrival
                // before declaring success, and fall through into the same pause path a
                // mid-step cut takes.
                var lateCut = core.TakeOperatorCut();
                if (lateCut is null) return true;
                cut = lateCut;
                ok = false;
                stepAlreadySucceeded = true;
            }

            // The instant the step failed — NOT "now". Everything below this line (the hold,
            // and an interactive pause that may run to the 600 s watchdog) happens with the
            // shutters closed, so none of it is deposition time and none of it may be
            // subtracted from a retried deposit's remaining dwell.
            var failedAtUtc = DateTime.UtcNow;

            // Classification. _lastCommandError carries the driver's reason for the
            // null-result case (ExecuteRecipeLine sets it); the interlock case sets it too but
            // its result is not available here, so the interlock marker is read from the message.
            object? result = fault is null && _lastCommandError is { } m && m.Contains("blocked by interlock")
                ? new Classes.InterlockCond() : null;
            var policy = FaultClassifier.Classify(fault, result, _continueOnError);
            bool pausable = cut is not null
                ? core!.IsUserRecipe && core.FaultPauseTimeoutSeconds > 0
                : policy == FaultPolicy.Pause
                  && core!.IsUserRecipe
                  && core.FaultPauseTimeoutSeconds > 0;
            if (!pausable)
            {
                if (fault is not null) ExceptionDispatchInfo.Capture(fault).Throw();
                return false;
            }

            string step = string.Join(' ', parts);
            // Trim before FirstLine: operation messages are authored with a leading '\n' (they
            // read better in the error dialog) and FirstLine cuts at the first newline BEFORE
            // trimming, so it would return "" for every one of them and the pause dialog would
            // name no cause at all. OneLine flattens the no-exception reason for the same
            // audience — the dialog and the step trace, not just the event log.
            string reason = cut is not null
                ? stepAlreadySucceeded
                    ? $"operator de-energised {cut.PsuId} (the step had already completed)"
                    : $"operator de-energised {cut.PsuId}"
                : fault is not null
                    ? $"{fault.GetType().Name}: {RecipeEventFormat.FirstLine(fault.Message.Trim())}"
                    : OneLine(_lastCommandError ?? "step reported failure");

            // A fault thrown by the step executor bypasses ExecuteRecipeLine's own bookkeeping,
            // so the driver's reason would never reach the recipe-level "Recipe failed — ..."
            // line on Abort. Record it here. A cut needs the same treatment: there is no fault
            // and ExecuteRecipeLine never ran for it, so without this the top-level failure
            // message on Abort would come out blank or carry an unrelated earlier error.
            if (fault is not null || cut is not null) _lastCommandError = reason;

            // Soft hold — never throws. Runs ONCE per pause, not once per iteration below: a
            // refused resume must not re-abort every drive and re-zero a supply the operator may
            // have just repaired by hand, and the Hold decision `continue`s this
            // loop without re-holding the machine it just handed the operator. Each iteration
            // re-reports this SAME report rather than re-running the hold — and, once a refusal
            // has re-shown it, with a note that it is no longer fresh (below).
            // The fault-handling window opens HERE, not when the dialog goes up: everything
            // from the first AbortDrivesForHold to the decision being acted on is a recipe that
            // is already being paused, and a panel power-OFF landing anywhere in it is armed
            // into the restoration set below rather than starting anything. Scoped to the rest
            // of this attempt, so the break that re-runs the step closes it too.
            using var faultHandling = core.EnterFaultHandling();

            HoldReport hold = await RunFailureHold(parts, cut?.PsuId).ConfigureAwait(false);
            bool holdIsStale = false;

            // The restoration set (Ruling K): every supply de-energised under THIS pause and
            // that must be live again before the recipe proceeds — one concept, three sources:
            // the cut that caused the pause, any cut arriving while the dialog is open, and the
            // hold's own zeroing (Ruling L). Keyed by PsuId.
            // FIRST **ENERGISED** SNAPSHOT WINS (Ruling S): the snapshot that matters is the
            // first one for a given supply that actually took power away. Both directions are
            // load-bearing:
            //   * a later WasOn=false snapshot must never displace an earlier WasOn=true one, or
            //     a genuinely energised supply is "restored" as a no-op and the line re-runs over
            //     a dead target;
            //   * a later WasOn=true snapshot MUST replace an earlier WasOn=false one, or an OFF
            //     press on an already-dark supply arms an empty entry that absorbs the press that
            //     follows a hand re-energising, with the same ending.
            // Any new path into this set must keep every arrival and log a line for each.
            var pendingCuts = new Dictionary<string, OperatorCut>(StringComparer.OrdinalIgnoreCase);
            void Arm(OperatorCut candidate, string origin)
            {
                string note;
                if (pendingCuts.TryGetValue(candidate.PsuId, out var armed))
                {
                    // RULING S. Reachable: press OFF on a supply that is already dark (armed
                    // WasOn=false), hand-energise it during the hold, then press OFF again. That
                    // second press snapshots the real watts; absorbed as "redundant", the restore
                    // hits `if (!cut.WasOn) return null;` and the line re-runs over it dead.
                    if (!armed.WasOn && candidate.WasOn)
                    {
                        pendingCuts[candidate.PsuId] = candidate;
                        note = $"{candidate.PsuId} de-energised again ({origin}) and THIS time it was " +
                            $"live at {candidate.SetPower:0.##} W — the armed snapshot is UPGRADED; the " +
                            "earlier press found it already off and had nothing to restore, a Retry now " +
                            "re-energises it";
                    }
                    else
                    {
                        // Two energised snapshots: the FIRST wins, and must, because it is the one
                        // holding the setpoint the recipe was running at before this pause touched
                        // anything (Ruling K).
                        note = $"{candidate.PsuId} de-energised again ({origin}) — absorbed " +
                            "(already armed for restoration, first snapshot kept)";
                    }
                }
                else
                {
                    pendingCuts[candidate.PsuId] = candidate;
                    // A supply that was already off when the press landed snapshots WasOn=false,
                    // and ReEnergiseAfterCut restores nothing for it, so the note must not claim
                    // it is armed for restoration. The entry is still recorded, still absorbs
                    // later presses, and Skip still names it.
                    note = candidate.WasOn
                        ? $"{candidate.PsuId} de-energised ({origin}) — armed for restoration on Retry"
                        : $"{candidate.PsuId} de-energised ({origin}), but it was ALREADY OFF when the " +
                          "press landed — recorded, with nothing for a Retry to re-energise";
                }
                SendRecipeLog(note);
                core.SendLogMesssage("Recipe", note);
            }
            if (cut is not null) Arm(cut, "the failure that caused this pause");
            // Everything that arrived BEHIND the cause: a second supply killed before the step's
            // cancellation had even landed, or any press made while RunFailureHold above was
            // closing the shutters (which takes real time on real hardware). The producer is a
            // FIFO queue so all of these survive (a co-sputter can lose two supplies). Arm keeps
            // the FIRST snapshot per supply, so the arrival order the queue preserves is what
            // makes "first snapshot wins" mean the first press.
            foreach (var early in core.TakeAllOperatorCuts())
                Arm(early, "the operator, while the pause was being set up");
            // EVERY supply the hold de-energised, narrated one way or the other: armed, or
            // explicitly carved out below. Nothing in this list may be dropped in silence.
            foreach (var zeroedByHold in hold.Zeroed)
            {
                // Ruling M — the one carve-out on Ruling L. When the failed line's OWN purpose
                // was to de-energise this supply, the hold's zeroing is not something done TO
                // the recipe, it is the recipe's own intent arriving early. Restoring it on
                // Retry would re-run `power X off` with the magnetron back on, and on a supply
                // that keeps failing its off command every Retry cycle would energise it and
                // then zero it again. An operator CUT of the same supply still arms (above):
                // that is the operator's action, not the line's, and Ruling K owns it.
                if (LineDeEnergises(parts, zeroedByHold.PsuId))
                {
                    string note = $"{zeroedByHold.PsuId} was zeroed by the hold, but the failed line " +
                        $"was itself de-energising it ({step}) — NOT armed for restoration; a Retry " +
                        "must not energise a supply only to re-run the line that turns it off";
                    SendRecipeLog(note);
                    core.SendLogMesssage("Recipe", note);
                }
                else
                {
                    Arm(zeroedByHold, "the hold's own zeroing");
                }
            }

            // Report, and wait for a decision. The inner loop exists so a resume that cannot
            // re-energise pauses AGAIN on the same step instead of re-running it over a dead
            // target — and, for a late cut (stepAlreadySucceeded), so a REPEATED refusal keeps
            // pausing without ever claiming the step failed.
            // ONE banner per pause, not one per decision. A Hold re-enters this loop without the
            // step having failed again; announcing would read as a second failure in the run log
            // and re-send a Paused progress frame, which resets RecipeProgressTracker's _updatedAt
            // and restarts the agent's stepElapsedSeconds at zero. A hold never auto-aborts, so
            // hold AGE is the only signal that a tool is parked.
            // Re-entries that ARE a genuine new pause (a refused resume, a cut during a resume)
            // re-arm this below and still announce.
            bool announcePause = true;

            // The hold ends when a decision is ACTED ON, not when the wait that carried it
            // returns. A resume that ReEnergiseAfterCut REFUSES (an interlock, a dropped link,
            // the documented polled-SetPower false refusal) is neither a resume nor an abort, so
            // the hold stands: ending it would hide the toolbar's Resume button (the operator's
            // only way out of a hold), re-lock their tools mid-repair, and re-arm a 600 s
            // watchdog that auto-aborts the run into FAILURE_PROTOCOL. Every real end point below
            // calls EndHoldIfAny; the two refusal `continue`s deliberately do not.
            // The structural guarantee that a hold never outlives the pause at all lives in
            // Core.ExitFaultHandling, on the `using var faultHandling` scope above, because a
            // Stop can unwind out of ReEnergiseAfterCut without passing any decision site.
            void EndHoldIfAny(string what)
            {
                if (!core!.IsHeldForOperator) return;
                core.SetHeldForOperator(false);
                string ended = $"Hold ended — {what}. Manual control is locked again and the run "
                             + "has the machine back";
                SendRecipeLog(ended);
                core.SendLogMesssage("Recipe", ended);
            }

            // Said after every refusal, so the operator is never left guessing whether the tools
            // they are holding are still theirs. Silent when the pause was never held.
            void NoteHoldStands()
            {
                if (!core!.IsHeldForOperator) return;
                string stands = "The hold STANDS: manual control stays unlocked, the watchdog stays "
                              + "disarmed, and the run waits for another Resume, Skip or Abort";
                SendRecipeLog(stands);
                core.SendLogMesssage("Recipe", stands);
            }

            while (true)
            {
                // The label stays "STEP FAILED (paused)" even for a late cut
                // (stepAlreadySucceeded); the reason text says the step had already
                // completed, and the PauseEvent below carries the flag so the run record does
                // not have to be READ to know the difference.
                if (announcePause)
                {
                    SendRecipeLog($"STEP FAILED (paused): {step} — {reason}");
                    core.SendLogMesssage("Recipe", $"STEP FAILED (paused): {step} — {OneLine(reason)}");
                    SendRecipeProgress(_currentStepNumber, $"PAUSED: {step} — {reason}", RecipeState.Paused);
                }
                announcePause = true;   // only the Hold branch below opts the NEXT pass out

                // A refusal re-shows the SAME HoldReport from the original hold (it is not
                // re-run — see above), which by now may say things about device state that a
                // resume attempt already changed (e.g. "output off" after ReEnergiseAfterCut
                // just commanded it on). Say so, rather than let the dialog assert a machine
                // state it can no longer vouch for.
                HoldReport shownHold = holdIsStale
                    ? hold with
                    {
                        Problems =
                        [
                            .. hold.Problems,
                            "this report is from the ORIGINAL hold and was not re-run — the machine's " +
                            "actual state may have changed since (see the log for what the refused resume did)",
                        ],
                    }
                    : hold;

                var (decision, by, waited) = await WaitForFaultDecisionAsync(step, reason, shownHold, cancellationToken)
                    .ConfigureAwait(false);

                _pauseEvents.Add(new PauseEvent(DateTime.UtcNow, step, reason, decision.ToString().ToLowerInvariant(), by,
                    core.TakeHeldManualCommands(), stepAlreadySucceeded));
                string line = $"FAULT DECISION: {decision.ToString().ToLowerInvariant()} ({by}, after {waited.TotalSeconds:0} s)";
                SendRecipeLog(line);
                core.SendLogMesssage("Recipe", line);

                // A hold that outran its wait budget is still commanding the machine. Let it finish
                // before Retry / Skip / Abort act, so the hold and the resumed recipe never drive the
                // same devices at once. (An operator Stop leaves through the OperationCanceledException
                // above and never reaches here; the hold then ends on its own while the run aborts.)
                await DrainUnfinishedHold().ConfigureAwait(false);

                // The panel's power-OFF stays ungated even while held, so a press while THIS
                // pause's dialog is open arms a cut that nothing above ever asked about —
                // including, for an ordinary (non-cut) step failure, the FIRST cut this attempt
                // has seen. Fold it into the restoration set exactly like any other arrival.
                // EVERY press, not just the most recent one: a held pause parks this thread
                // for as long as the operator needs, with manual control unlocked, so several
                // supplies may have been killed since the last drain.
                foreach (var absorbed in core.TakeAllOperatorCuts())
                    Arm(absorbed, "the operator, while paused");

                if (decision == FaultDecision.Hold)
                {
                    // Placed HERE — after DrainUnfinishedHold above, and after the absorption of
                    // a cut pressed while the dialog was open — deliberately. A hold that outran
                    // its budget is still commanding the machine, and handing the operator a
                    // machine another flow is still driving is the same collision the drain
                    // exists to prevent; and a power-OFF pressed while the dialog was up is still
                    // owed restoration, so it is armed before the operator takes over.
                    core.SetHeldForOperator(true);

                    // The operator is about to drive the hardware BY HAND. Whatever the original
                    // hold reported about machine state, it cannot vouch for it after that, so
                    // any later re-show of this pause carries the "not re-run" note rather than
                    // asserting something the code can no longer know.
                    holdIsStale = true;

                    // The recipe thread stays parked exactly where it is: nothing is unwound, the
                    // step is not re-run, and the soft hold's actions are NOT undone — the
                    // shutters stay closed and the restoration set stays armed for the eventual
                    // Retry. The run still owns the machine, so Start remains refused.
                    string note = $"HELD on: {step} — manual control unlocked, watchdog disarmed; "
                                + "the run is parked and still owns the machine";
                    SendRecipeLog(note);
                    core.SendLogMesssage("Recipe", note);

                    // Re-enter the wait with the watchdog off and the dialog closed (Ruling C).
                    // The decision seam is unchanged, so Resume / Skip / Abort still arrive from
                    // the toolbar, the agent or the MCP bridge.
                    //
                    // Silently: this is the SAME pause, not a new one. Re-announcing "STEP
                    // FAILED (paused)" would say the step failed twice, and re-sending the
                    // Paused progress frame would restart the agent's stepElapsedSeconds — the
                    // one number that reveals how long a tool has been parked.
                    announcePause = false;
                    continue;
                }

                if (decision == FaultDecision.Retry)
                {
                    if (pendingCuts.Count > 0)
                    {
                        string? refusal = null;
                        foreach (string psuId in pendingCuts.Keys.ToArray())
                        {
                            refusal = await ReEnergiseAfterCut(pendingCuts[psuId], cancellationToken).ConfigureAwait(false);
                            if (refusal is not null) break;
                            pendingCuts.Remove(psuId);
                        }
                        if (refusal is not null)
                        {
                            // Do NOT re-run (or, for a late cut, re-conclude) the line. Pause again
                            // with the refusal as the reason so the operator can see why and either
                            // fix it or abort. Any supply not yet reached (or itself refused) stays
                            // in pendingCuts, still armed, for the next Retry.
                            reason = refusal;
                            holdIsStale = true;
                            SendRecipeLog($"Resume refused: {refusal}");
                            core.SendLogMesssage("Recipe", $"Resume refused: {OneLine(refusal)}");
                            NoteHoldStands();
                            continue;
                        }
                    }

                    // The drain above ran BEFORE the restore loop, and the restore loop is not
                    // instantaneous: ReEnergiseAfterCut spends at least its 500 ms settle delay
                    // per supply (Operations.Power's own settle, mirrored). An OFF press inside
                    // that window is in no set anybody consulted, so without this re-check the
                    // line would re-run a full dwell over the supply the operator just killed.
                    // If one arrived, fold it in and pause again instead of proceeding.
                    var duringResume = core.TakeAllOperatorCuts();
                    if (duringResume.Count > 0)
                    {
                        foreach (var late in duringResume) Arm(late, "the operator, during the resume");
                        string names = string.Join(", ", duringResume
                            .Select(c => c.PsuId).Distinct(StringComparer.OrdinalIgnoreCase));
                        reason = $"operator de-energised {names} while the resume was " +
                            "restoring power — the line was NOT re-run";
                        holdIsStale = true;
                        SendRecipeLog($"Resume abandoned: {reason}");
                        core.SendLogMesssage("Recipe", $"Resume abandoned: {OneLine(reason)}");
                        NoteHoldStands();
                        continue;
                    }

                    EndHoldIfAny("the operator resumed the run");

                    if (stepAlreadySucceeded)
                    {
                        // The line already ran to completion; re-energising is all a resume
                        // needs. Re-running it — or trimming its dwell via AdjustForResume,
                        // which would compute the 1 s floor against a dwell that fully
                        // elapsed — would lay extra film on an already-finished layer.
                        return true;
                    }

                    parts = AdjustForResume(parts, attemptStartedUtc, failedAtUtc);
                    SendRecipeProgress(_currentStepNumber, string.Join(' ', parts), RecipeState.Running);
                    break;                                     // leave the pause loop, re-run the step
                }

                if (decision == FaultDecision.Skip)
                {
                    EndHoldIfAny("the operator skipped the step and let the run continue");

                    // Skip does not restore — but must not lie about it (Ruling K.5). Anything
                    // still in the restoration set is left exactly as this pause found it: say
                    // so, plainly, naming every supply.
                    if (pendingCuts.Count > 0)
                    {
                        string names = string.Join(", ", pendingCuts.Keys);
                        string note = $"Skip: continuing WITHOUT restoring power to {names} — " +
                            "the operator chose not to resume them";
                        SendRecipeLog(note);
                        core.SendLogMesssage("Recipe", note);
                    }

                    if (stepAlreadySucceeded)
                    {
                        // The line already completed; there is nothing to skip and recording it
                        // as skipped would put a falsehood in the signed run record. The operator
                        // is choosing to move on without re-energising.
                        SendRecipeLog($"Resume continued (step already completed): {step}");
                        core.SendLogMesssage("Recipe", $"Resume continued (step already completed): {step}");
                    }
                    else
                    {
                        _skippedSteps.Add(step);
                        SendRecipeLog($"STEP SKIPPED by operator: {step}");
                        core.SendLogMesssage("Recipe", $"STEP SKIPPED by operator: {step}");
                    }
                    return true;
                }

                EndHoldIfAny("the run is aborting");
                if (fault is not null) ExceptionDispatchInfo.Capture(fault).Throw();
                return false;
            }
        }
    }

    /// <summary>
    /// True when the failed line's OWN purpose was to de-energise <paramref name="psuId"/> —
    /// the carve-out on Ruling L (see the call site). Ruling M.
    /// <para>The verbs are not free-form config aliases: <c>Core.LoadWorker</c> registers every
    /// operation under <c>op.Method.ToLower()</c>, so a DSL verb IS the worker method name in
    /// <c>Workers/*/Operations.cs</c>, lower-cased, and the argument order is that method's
    /// signature. The shapes below are read off those signatures:</para>
    /// <list type="bullet">
    /// <item><c>power ID off</c> — <c>Operations.Power(psu, on: false)</c> calls
    /// <c>OutputOff()</c>. The token is <c>false</c> by the time it gets here
    /// (<c>ApplySubstitutions</c> rewrites off→false before dispatch, and
    /// <c>ConvertStringToType</c> accepts nothing else for a bool), but the authored spellings
    /// are accepted too so this does not depend on where in the pipeline it is called.</item>
    /// <item><c>set ID Power 0</c> — <c>Operations.Set</c> forwards to
    /// <c>ApplySetpoint("Power", 0)</c>. Channel forms (<c>Power:2</c>) count.</item>
    /// <item><c>ramp ID 0</c> — <c>Operations.Ramp</c> is <c>ApplySetpoint("Power", power)</c>;
    /// a ramp whose target is 0 is a ramp to dead.</item>
    /// <item><c>bias ID 0</c> — <c>Operations.Bias</c>'s bare-numeric form calls
    /// <c>OutputOff()</c> for any value that is not &gt; 0.</item>
    /// </list>
    /// <para>EVERYTHING ELSE IS TREATED AS NOT DE-ENERGISING, deliberately, including shapes
    /// that arguably are: <c>set ID Current 0</c> / <c>Voltage 0</c> (the zeroed parameter may
    /// not be the supply's active regulation mode), <c>bias ID out CH Arb 0</c> (one channel of
    /// a multi-channel supply, which is not what the whole-supply Power snapshot restores), and
    /// <c>warmup</c> (whose parts[1] is a TIME — <c>ExecuteResolvedParts</c> intercepts the verb
    /// before the worker's Warmup op is ever reached — so it names no supply at all). Restoring
    /// wrongly is recoverable and visible in the log; failing to restore is the dead-target
    /// failure this whole feature exists to prevent, so ambiguity arms.</para>
    /// </summary>
    internal static bool LineDeEnergises(string[] parts, string psuId)
    {
        if (parts.Length < 3 || !parts[1].Equals(psuId, StringComparison.OrdinalIgnoreCase))
            return false;

        static bool IsZero(string token) =>
            float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            && Math.Abs(v) < 0.0001f;

        string cmd = parts[0];

        if (cmd.Equals("power", StringComparison.OrdinalIgnoreCase))
            return parts[2].Equals("false", StringComparison.OrdinalIgnoreCase)
                || parts[2].Equals("off", StringComparison.OrdinalIgnoreCase);

        if (cmd.Equals("ramp", StringComparison.OrdinalIgnoreCase))
            return IsZero(parts[2]);

        // the bare-numeric form only: `bias ID set ...` / `bias ID out ...` are not
        if (cmd.Equals("bias", StringComparison.OrdinalIgnoreCase))
            return parts.Length == 3 && IsZero(parts[2]);

        if (cmd.Equals("set", StringComparison.OrdinalIgnoreCase))
            return parts.Length >= 4
                && (parts[2].Equals("Power", StringComparison.OrdinalIgnoreCase)
                    || parts[2].StartsWith("Power:", StringComparison.OrdinalIgnoreCase))
                && IsZero(parts[3]);

        return false;
    }

    /// <summary>
    /// Restores the supply an operator cut, before the interrupted line re-runs (or, for a cut
    /// that landed after the line had already succeeded, before the recipe moves on). Returns
    /// null when the resume may proceed, or an operator-facing reason why it may not.
    /// <para>This exists because <c>deposit</c> energises NOTHING — Operations.Deposit moves the
    /// holder and drives the shutter only, and power comes from earlier set / power on / warmup
    /// lines. A bare retry would therefore re-open the shutter over a dead target: the progress bar
    /// counts down, DepositReporter books the erosion, and the .depo.json claims a layer that never
    /// grew.</para>
    /// <para><b>KNOWN ISSUE — do not soften: this method's <c>OutputOn()</c> call can WEDGE the
    /// HW thread.</b> On a serial RF supply that is <c>ForcePowerOn</c> →
    /// <c>SendCommandText</c> → <c>BaseSerialPort.SendText</c> — sync-over-async
    /// (<c>SendTextAsync(...).GetAwaiter().GetResult()</c>) over <c>AcquirePortAsync</c>'s
    /// semaphore, which has no deadline. A poller wedged in <c>ReadLine</c> on the same virtual
    /// COM port holds that semaphore forever and blocks the HW thread with it: the Agent thread
    /// keeps answering (it still logs <c>recipe.abort</c>) while this thread never logs again and
    /// never processes the abort. The <c>OperatorCutMidDeposit</c> harness case
    /// reproduces it intermittently. The operator panel's power-OFF-then-Retry reaches this identical synchronous
    /// path, so this is not a test-only hazard. See
    /// docs/superpowers/specs/2026-09-04-operator-hold-design.md §10 and
    /// .superpowers/sdd/2026-09-04-operator-hold/task-10-report.md §10 for the full evidence and
    /// the sibling stall in <see cref="AbortDrivesForHold"/>. The fix is in the cut path's
    /// synchronous device I/O, not here.</para>
    /// </summary>
    private async Task<string?> ReEnergiseAfterCut(OperatorCut cut, CancellationToken ct)
    {
        if (core!.FindElement(DeviceType.Power, cut.PsuId) is not BasePowerSupply psu)
            return $"cannot re-energise {cut.PsuId}: no longer registered";
        if (!psu.Connected)
            return $"cannot re-energise {cut.PsuId}: not connected";

        // The cut took nothing away — the supply was already off when the operator pressed OFF.
        if (!cut.WasOn) return null;

        // The operator may have fixed it by hand during a hold. Their SETTING wins — they had a
        // reason, and silently overwriting it would undo the repair they were asked to make —
        // but IsOutputOn is a pure command echo (BasePowerSupply.OutputOn/OutputOff, written
        // from nowhere else): a hand power-ON that never reached a dead link or an open breaker
        // still latches it true. It must therefore clear the exact same interlock check and
        // responding confirmation as OUR OWN OutputOn() call below before a resume is
        // permitted — the value is honoured, the power is still verified.
        // RULING N. "Hand repair" means the operator restored POWER, not that the output latch
        // reads on. A supply at a zeroed setpoint is not repaired whatever the latch says, and
        // the single most natural gesture there is — press the button that is off, and nothing
        // else — lands exactly there: the hold has already driven the setpoint to 0, so
        // IsOutputOn goes true over 0 W, the supply answers the confirmation perfectly well
        // (responding is not powered), and the deposit re-runs growing nothing into a signed run
        // record. ONE condition, on the supply's own setpoint, rather than on where the zero
        // came from: it covers the hold's successful zeroing, the hold's PARTIAL zeroing (the
        // output-off that threw), and any other route to a latched-on supply delivering nothing.
        // SetPower is a real setpoint, not a command echo: every driver writes it in
        // ApplySetpoint and some read it back off the registers.
        // Not every supply HAS a "Power" setpoint: a bias analyser and an ion source
        // (a routine deposition participant) reject the name by design.
        // Read the capability ONCE, here, so every branch below (Ruling N's test, the restore,
        // and TryUndoOurOwnCommand) agrees about it.
        bool hasPowerSetpoint = psu.SupportsPowerSetpoint;

        // Ruling N's "latched on over a zeroed setpoint is not a repair" is a statement about a
        // POWER setpoint, and asking it of a supply that has none is a category error: SetPower
        // is never written by those drivers and reads 0 forever, so the rule would call every
        // hand power-ON on the ion source a fake repair, print "restoring the recipe's 0 W" at
        // it, and then force-reverse the operator's own action on a refusal. On such a supply the
        // output latch IS the whole of what a hand repair can change from the panel, so it is the
        // whole of the test.
        //
        // KNOWN ISSUE: on supplies that read the setpoint back from the device, SetPower is a
        // POLLED READBACK, not a local echo (one driver's ReadSettings reads an integer-watt
        // register while in Power mode; another int-parses the setpoint
        // field of every "Q" status reply). A poll landing inside this method's 500 ms settle
        // (below) can hand this line a stale 0 — a sub-0.5 W setpoint rounding to register 0, or
        // a status reply still carrying the pre-write setpoint — and cause a FALSE REFUSAL of an
        // otherwise-good resume. The direction is safe: it de-energises and re-pauses with an
        // explicit reason, recoverable by pressing Retry again. Not yet reproduced against real
        // hardware; watch for it rather than chase a phantom "resume just refuses sometimes" report.
        bool handRepaired = psu.IsOutputOn && (!hasPowerSetpoint || psu.SetPower > 0.01f);
        if (psu.IsOutputOn && !handRepaired)
        {
            string zeroNote = $"{cut.PsuId}'s output is latched on at a 0 W setpoint — not a repair; " +
                $"restoring the recipe's {cut.SetPower:0.##} W";
            SendRecipeLog(zeroNote);
            core.SendLogMesssage("Recipe", zeroNote);
        }

        // Undoes OUR OWN command before a refusal. A hand repair is the operator's own action
        // and is never force-reversed here. Best-effort: a failure to de-energise is logged, not
        // thrown; the refusal pauses the recipe either way.
        //
        // SEPARATE try/catch blocks, and the setpoint zero is SKIPPED outright on a supply with
        // no Power setpoint — the same discipline RunFailureHold keeps. With one shared try, a
        // throwing ApplySetpoint("Power", 0) would skip the OutputOff() and refuse the resume
        // with the supply LEFT COMMANDED ON behind a closed shutter, while the re-shown (stale)
        // hold report tells the operator it is off. The de-energise is the load-bearing half:
        // it is attempted whatever the setpoint write did.
        // Returns TRUE when NOTHING IS OUTSTANDING: either no undo was owed, or the one we owed
        // landed. Note what that is NOT — it is not "the supply is off". A hand repair returns
        // true while the supply is deliberately left ENERGISED, because reversing the operator's
        // own action is the one thing this must never do. Hence the Try-prefixed name and the
        // wording of the refusals composed from it, which never assert "off".
        // Returns FALSE only when the OutputOff we did attempt threw: we tried, we cannot prove
        // the supply is off, and every refusal built on that must SAY so rather than let the
        // dialog assert "off" about hardware nobody has checked.
        bool TryUndoOurOwnCommand()
        {
            if (handRepaired) return true;
            if (hasPowerSetpoint)
            {
                try
                {
                    psu.ApplySetpoint("Power", 0);
                }
                catch (Exception ex)
                {
                    string zeroFail = $"{cut.PsuId}: could not zero the setpoint after the refused resume " +
                        $"({ex.GetType().Name}: {ex.Message}) — turning the output off anyway";
                    SendRecipeLog(zeroFail);
                    core.SendLogMesssage("Recipe", zeroFail);
                }
            }
            try
            {
                psu.OutputOff();
                string undoLine = $"Resume refused for {cut.PsuId} — de-energised again " +
                    "rather than leave it commanded on with the output unconfirmed";
                SendRecipeLog(undoLine);
                core.SendLogMesssage("Recipe", undoLine);
                return true;
            }
            catch (Exception ex)
            {
                string undoFail = $"{cut.PsuId}: also failed to de-energise after the refused resume: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                SendRecipeLog(undoFail);
                core.SendLogMesssage("Recipe", undoFail);
                return false;
            }
        }

        // Composes a refusal that tells the truth about the supply's ENERGY STATE, not just about
        // why the resume failed. Every refusal below reaches the operator through the re-shown
        // pause, and the hold report it sits beside is the ORIGINAL one — frozen, and quite
        // possibly asserting "output off" about a supply this attempt has since commanded on. If
        // the de-energise we owe cannot be proven to have landed, the refusal says so in the
        // words the operator has to act on: treat it as live.
        string RefuseAndDeEnergise(string why) =>
            TryUndoOurOwnCommand()
                ? $"cannot re-energise {cut.PsuId}: {why}"
                : $"cannot re-energise {cut.PsuId}: {why} — AND the de-energise that followed FAILED: " +
                  $"treat {cut.PsuId} as ENERGISED until it is verified by hand";

        if (handRepaired && Math.Abs(psu.SetPower - cut.SetPower) > 0.01f)
        {
            string note = $"{cut.PsuId} was re-energised by hand at {psu.SetPower:0.##} W "
                        + $"(the recipe had {cut.SetPower:0.##} W) — keeping the operator's value";
            SendRecipeLog(note);
            core.SendLogMesssage("Recipe", note);
        }

        var blocked = core.CheckInterlocks("power", [psu, true]);
        if (blocked is not null)
            return $"cannot re-energise {cut.PsuId}: blocked by interlock on {Core.InterLockToString(blocked.Value)}";

        if (!handRepaired)
        {
            // Which side of OutputOn() a throw came from decides whether we owe a de-energise.
            // Before it, nothing was commanded on and taking the output "back" would be a command
            // we never issued; from OutputOn() onwards the on-command may ALREADY be on the wire.
            bool outputOnAttempted = false;
            try
            {
                // The setpoint only when the supply HAS one. When it does not, the OUTPUT is
                // still restored — that is the half that matters, and the half a `deposit` needs
                // — and the log says plainly that there was no setpoint to put back rather than
                // claiming a restoration to a watts figure the supply never had.
                if (hasPowerSetpoint) psu.ApplySetpoint("Power", cut.SetPower);

                outputOnAttempted = true;
                psu.OutputOn();

                // Logged AFTER the command, so a throwing OutputOn cannot leave the log claiming
                // a restoration it did not perform.
                if (!hasPowerSetpoint)
                {
                    string noSp = $"{cut.PsuId} has no Power setpoint (this supply regulates by other " +
                        "parameters) — restored its OUTPUT only; nothing else it was set to is changed";
                    SendRecipeLog(noSp);
                    core.SendLogMesssage("Recipe", noSp);
                }
            }
            catch (Exception ex)
            {
                string why = $"{ex.GetType().Name}: {ex.Message}";
                if (!outputOnAttempted)
                    return $"cannot re-energise {cut.PsuId}: {why}";

                // A supply may be left physically energised while the dialog says it is off.
                // A serial RF driver's ForcePowerOn sends its command
                // through BaseSerialPort.SendTextAsync, which WRITES the command and only then
                // reads the reply — and that ReadLine can throw InvalidOperationException (port
                // closed) or IOException on a dropped USB-serial adapter, neither of which
                // SendCommandText catches (it catches TimeoutException alone). The throw unwinds
                // through WriteOutputState, so BasePowerSupply.OutputOn never reaches its
                // `IsOutputOn = true` and the flag stays FALSE about an RF supply that may be
                // live behind a closed shutter — with the re-shown, frozen hold report telling
                // the operator it is off. Of the stock drivers only one can do this; the others
                // swallow on that path. Take the output back before refusing, and if that
                // cannot be proven to have landed, say so in the refusal itself.
                return RefuseAndDeEnergise(why);
            }
        }

        // Same confirmation the `power on` operation uses (Operations.Power): OutputOn sets
        // IsOutputOn locally even if no bytes reached the supply, so the flag alone would let the
        // shutter re-open over a supply that is not there — whether OutputOn() was ours, just
        // above, or the operator's own hand-repair. Settle before reading, mirroring
        // Operations.Power exactly: some drivers only transmit the relay command on their next poll, so
        // reading immediately can see "not responding" before the command has even gone out.
        // Skipped while ramping, where ReadFeedback is paused on purpose.
        if (!psu.RampEnabled)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (!psu.IsResponding && !await psu.ConfirmRespondingAsync(ct: ct).ConfigureAwait(false))
            {
                string why = string.IsNullOrWhiteSpace(psu.LastCommFault)
                    ? "no reason recorded by the driver"
                    : psu.LastCommFault!;

                // Undo OUR OWN command before refusing: ConfirmRespondingAsync failing does not
                // prove the wire command failed (IsResponding can flip false on a single late
                // poll — Operations.Power's own comment), so the plausible real state is a
                // genuinely energised supply behind a shutter, while the hold is not
                // re-run to zero it a second time and the re-shown dialog is
                // about to tell the operator, via the ORIGINAL hold report, that it is off.
                // The refusal carries the outcome of that undo: if the de-energise itself
                // threw, "output not confirmed" alone would understate the machine's state.
                return RefuseAndDeEnergise($"output not confirmed ({why})");
            }
        }

        // Responding is not powered, and the confirmation above cannot tell the two apart: a
        // supply answering on the wire at a zeroed setpoint answers exactly as well as one at
        // 450 W. Ruling N's handRepaired rule keeps that state off the hand-repair path
        // structurally; this is the same statement made about OUR OWN path, where the setpoint
        // we just wrote may have been accepted by name and never applied (the failure mode
        // Operations.Set guards against). The supply is not energising anything, so the safe
        // answer is to refuse and let the operator see it, having first taken back the output
        // we commanded on.
        // Only meaningful where "Power" is a real setpoint: on a supply that has none,
        // cut.SetPower is a snapshot of a field the driver never writes (it reads 0 forever).
        if (hasPowerSetpoint && cut.SetPower > 0.01f && psu.SetPower <= 0.01f)
        {
            return RefuseAndDeEnergise($"it is responding but its setpoint is 0 W " +
                $"(the recipe had {cut.SetPower:0.##} W) — responding is not powered");
        }

        string line = handRepaired
            ? hasPowerSetpoint
                ? $"Resume: {cut.PsuId} confirmed responding at the operator's {psu.SetPower:0.##} W"
                : $"Resume: {cut.PsuId} was re-energised by hand and is confirmed responding"
            : hasPowerSetpoint
                ? $"Resume: {cut.PsuId} restored to {cut.SetPower:0.##} W and re-energised"
                : $"Resume: {cut.PsuId} re-energised (no Power setpoint to restore) and confirmed responding";
        SendRecipeLog(line);
        core.SendLogMesssage("Recipe", line);
        return null;
    }

    /// <summary>
    /// Soft hold. Stops every connected drive, runs FAILURE_HOLD with continue-on-error (every
    /// line attempted), then zeroes the failed supply if the failed line named one. Logs and
    /// swallows everything — it never throws.
    /// <para>Returns what the hold ACTUALLY did and what it could not do. The dialog renders
    /// both lists and asserts nothing of its own: a canned "shutters closed and the failed
    /// supply de-energised" would be false for a profile with no FAILURE_HOLD
    /// and for every failed line that names no power supply — most pausable failures.</para>
    /// </summary>
    internal async Task<HoldReport> RunFailureHold(string[] failedParts, string? cutPsuId = null)
    {
        var done = new List<string>();
        var problems = new List<string>();

        // EVERY registered supply, snapshotted BEFORE anything this hold does can touch one:
        //   A. FAILURE_HOLD contains `power X off` and the failed line names no PSU (a `deposit`
        //      names a SOURCE): without this nothing is armed for X, and Retry re-opens the
        //      shutter over it dead.
        //   B. FAILURE_HOLD contains `power X off` and the failed line DOES name X: a snapshot
        //      taken after the hold script would read WasOn=false and the restore would walk
        //      past it.
        // Ruling M does NOT cover them — that carve-out
        // is about the failed RECIPE line, which a Retry RE-RUNS; a FAILURE_HOLD line is never
        // re-run, so a supply it de-energises is exactly what Ruling L says must be armed. The
        // carve-out still applies per supply, at the consumer, against the failed line.
        var beforeHold = new Dictionary<string, OperatorCut>(StringComparer.OrdinalIgnoreCase);
        if (core!.hwElements.Item.TryGetValue(DeviceType.Power, out var supplies) && supplies is not null)
            foreach (var item in supplies)
                if (item.Value is BasePowerSupply p)
                    beforeHold[p.Params.ID] = OperatorCut.From(p);

        AbortDrivesForHold(done, problems);

        string[] script = core!.embRecipes?.Recipe(HoldRecipeName) ?? [];
        if (script.Length == 0)
        {
            SendRecipeLog("Fault pause: no FAILURE_HOLD recipe defined, skipping hold");
            problems.Add("this profile defines no FAILURE_HOLD — nothing was closed");
        }
        else
        {
            SendRecipeLog("Running FAILURE_HOLD...");
            // ExecuteRecipeWithInterpreter rewrites the run-wide bookkeeping (total steps,
            // estimator, loop stacks). The outer recipe is still in flight, so save and restore.
            int savedTotal = _recipeTotalSteps;
            var savedEstimatorLines = _estimatorLines;
            var savedEstimator = _estimator;
            int savedStack = _currentStack, savedStacks = _totalStacks;
            int savedStep = _currentStepNumber;
            string? savedError = _lastCommandError;
            bool savedContinue = _continueOnError;
            var savedStepExecutor = _stepExecutor;

            _continueOnError = true;
            _stepExecutor = null;
            lock (_holdFailures) _holdFailures.Clear();
            using var budgetCts = new CancellationTokenSource();
            try
            {
                // The hold deliberately runs on CancellationToken.None: an operator Stop must
                // still close the shutters. But that puts it on the Stop and app-exit paths, so
                // a profile author who writes `swait pm 50mT 300` into FAILURE_HOLD would make
                // both unresponsive for five minutes on a product with a documented exit-hang
                // history. Bound the WAIT, not the hold — the task keeps running to completion,
                // we simply stop waiting on it and pause the operator in.
                //
                // _holdInFlight, NOT _continueOnError, is what keeps the hold's own lines out of
                // the pause path, because the finally below restores _continueOnError while an
                // overrunning hold is still inside its line loop. It is set INSIDE RunHoldScript
                // — see the comment there for why it must not be set here.
                var holdTask = RunHoldScript(script);
                var finished = await Task.WhenAny(holdTask, Task.Delay(_holdBudget, budgetCts.Token))
                    .ConfigureAwait(false);

                if (finished != holdTask)
                {
                    // The finally below restores the outer recipe's DISPLAY bookkeeping while the
                    // hold is still using it. That is the lesser evil: the alternative is an
                    // unbounded, uninterruptible block with the operator staring at an amber pill
                    // and no dialog. The flags that route failures are not restored here.
                    problems.Add($"hold did not finish within {_holdBudget.TotalSeconds:0.##} s — it is still running");
                    // Whatever the hold already recorded before it outran the budget still belongs
                    // in the dialog; the lines it fails AFTER this point are logged by
                    // RecordContinuedFailure but arrive too late to be shown.
                    lock (_holdFailures) problems.AddRange(_holdFailures);
                    // Nothing awaits it here; the handler drains it after the operator decides
                    // (DrainUnfinishedHold) so the hold and a resumed recipe never command the
                    // same devices at once. Task.WhenAny does not observe a fault and the drain
                    // may drop the task on its own timeout, so observe it once, here, for good.
                    _unfinishedHold = holdTask;
                    _ = holdTask.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                }
                else
                {
                    if (!await holdTask.ConfigureAwait(false))
                        problems.Add($"hold did not complete: {_lastCommandError ?? "interpreter returned false"}");
                    lock (_holdFailures) problems.AddRange(_holdFailures);
                    done.Add($"FAILURE_HOLD ran ({script.Length} line(s) attempted)");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"hold aborted: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                budgetCts.Cancel();        // do not leave the losing Task.Delay timer armed
                _stepExecutor = savedStepExecutor;
                _continueOnError = savedContinue;
                _recipeTotalSteps = savedTotal;
                _estimatorLines = savedEstimatorLines;
                _estimator = savedEstimator;
                _currentStack = savedStack; _totalStacks = savedStacks;
                _currentStepNumber = savedStep;
                _lastCommandError = savedError;
            }
        }

        // Zero the supply. An operator cut names the supply directly; otherwise the failed line
        // does — power / set / ramp / bias put the id second (they are Operations.Power / Set /
        // Ramp / Bias, whose first parameter is the BasePowerSupply). `warmup` does NOT:
        // ExecuteResolvedParts intercepts that verb and routes it
        // to Core.OpWarmup(seconds, sources), so parts[1] is a TIME and FindElement returns
        // null for it. See LineDeEnergises for
        // the full account of which shapes name a supply and which of them de-energise it.
        // A `deposit` line names a SOURCE, so without the cut's id the report would claim nothing
        // was de-energised while the operator is looking at the supply they just killed.
        string? psuId = cutPsuId
            ?? (failedParts.Length >= 2 ? failedParts[1] : null);
        // One entry per supply this hold de-energised. At most one is reachable today (psuId is
        // a single name); see HoldReport.Zeroed for why the RECORD is a list regardless.
        var zeroed = new List<OperatorCut>();
        // Set when the failed line named no supply; consumed after the sweep below.
        bool unresolvedFailedLine = false;
        if (psuId is not null
            && core.FindElement(DeviceType.Power, psuId) is BasePowerSupply psu)
        {
            // Snapshot BEFORE anything is attempted, exactly like an operator cut
            // (OperatorCut.From) — Ruling L: the hold silently de-energising a supply a mid-step
            // failure never named as a cut is otherwise indistinguishable, on Retry, from a
            // supply that was simply never touched, and the line re-runs over dead hardware just
            // the same. ExecuteWithFaultPause folds this into the SAME restoration set a cut
            // populates, insert-if-absent, so it never displaces a cut's own (earlier, correct)
            // snapshot of the same supply.
            //
            // The two commands are attempted and reported SEPARATELY, and the snapshot survives
            // if EITHER of them changed the machine. An all-or-nothing try/catch would discard
            // the snapshot when the setpoint write lands and the relay write times out, and the
            // run would resume with the setpoint at 0 and the output still latched ON, with
            // nothing armed to put the setpoint back. A successful ApplySetpoint("Power", 0) IS a
            // proven de-energisation of the operating point, whatever the relay did.
            //
            // The output is still attempted when the setpoint write throws: the hold's whole job
            // is to make the machine safe, and a failed setpoint is no reason to leave the
            // output on.
            //
            // A supply with NO Power setpoint (a bias analyser, an ion source) is not a supply whose
            // zeroing failed: skip the write, so an ordinary ion-source failure does not report
            // a false HOLD INCOMPLETE problem, and let the OUTPUT off (which is the whole of the
            // de-energisation available here) stand on its own.
            // The PRE-hold snapshot, not one taken now: by this point the hold script has run
            // and may already have de-energised this very supply (variant B above), in which case
            // reading it here records "it was off" about a supply that was live a moment ago.
            var snapshot = beforeHold.TryGetValue(psu.Params.ID, out var pre) ? pre : OperatorCut.From(psu);
            bool hasPowerSetpoint = psu.SupportsPowerSetpoint;
            bool setpointZeroed = false, outputOff = false;
            if (hasPowerSetpoint)
            {
                try
                {
                    psu.ApplySetpoint("Power", 0);
                    setpointZeroed = true;
                }
                catch (Exception ex)
                {
                    problems.Add($"could not zero {psuId}'s setpoint: {ex.Message}");
                }
            }
            try
            {
                psu.OutputOff();
                outputOff = true;
            }
            catch (Exception ex)
            {
                problems.Add($"could not turn {psuId}'s output off: {ex.Message}");
            }

            if (!hasPowerSetpoint)
            {
                // Nothing was skipped by accident and nothing failed — say what the machine
                // actually is, so the dialog does not imply a setpoint is still sitting somewhere
                // hot. The output-off failure, if any, is already in problems above.
                if (outputOff)
                {
                    SendRecipeLog($"Fault pause: {psu.Params.ID} output off " +
                        "(this supply has no Power setpoint to zero)");
                    done.Add($"{psu.Params.ID} output off (no Power setpoint on this supply)");
                }
            }
            else if (setpointZeroed && outputOff)
            {
                SendRecipeLog($"Fault pause: {psu.Params.ID} setpoint 0, output off");
                done.Add($"{psu.Params.ID} setpoint 0, output off");
            }
            else if (setpointZeroed)
            {
                SendRecipeLog($"Fault pause: {psu.Params.ID} setpoint 0 — OUTPUT STILL ON");
                done.Add($"{psu.Params.ID} setpoint 0");
                problems.Add($"{psu.Params.ID} is at 0 W with its output still latched ON — " +
                    "it is delivering nothing but is not de-energised");
            }
            else if (outputOff)
            {
                SendRecipeLog($"Fault pause: {psu.Params.ID} output off (setpoint unchanged)");
                done.Add($"{psu.Params.ID} output off");
            }

            // Restoration is owed for whatever actually changed. Nothing changed → nothing to
            // restore, and claiming otherwise would put a "restored" line in the log for a
            // supply this hold never touched.
            if (setpointZeroed || outputOff) zeroed.Add(snapshot);
            else problems.Add($"could not de-energise {psuId} at all — its state is unchanged");
        }
        else
        {
            // The line is NOT written here: "nothing was de-energised" is a claim about the whole
            // hold, and the sweep below still has to ask what the FAILURE_HOLD script itself
            // killed (a failed `deposit` names a SOURCE, so psuId never resolves even when the
            // hold ran `power X off`). It is written after the sweep, where it is knowable.
            unresolvedFailedLine = true;
        }

        // Everything ELSE the hold took power away from — the FAILURE_HOLD script's own
        // `power X off` / `set X Power 0` lines, on any number of supplies. The block above
        // resolves exactly ONE psuId (the cut's, else the failed line's second word), and a
        // co-sputter hold can kill two. Diffed against the pre-hold snapshots, so it fires only
        // where the hold really did de-energise something, and mirrors the explicit block's own
        // condition: the output dropped, or a real Power setpoint went to zero.
        foreach (var (id, before) in beforeHold)
        {
            if (zeroed.Any(z => string.Equals(z.PsuId, id, StringComparison.OrdinalIgnoreCase))) continue;
            if (core.FindElement(DeviceType.Power, id) is not BasePowerSupply now) continue;
            bool lostOutput = before.WasOn && !now.IsOutputOn;
            bool lostSetpoint = now.SupportsPowerSetpoint && before.SetPower > 0.01f && now.SetPower <= 0.01f;
            if (!lostOutput && !lostSetpoint) continue;

            zeroed.Add(before);
            string what = lostOutput && lostSetpoint ? "setpoint 0, output off"
                : lostOutput ? "output off"
                : "setpoint 0";
            // WHO did it. The sweep sees only a before/after difference, and a difference is not
            // evidence of a cause: on a supply whose IsOutputOn is written from the polled status
            // bit and SetPower from the polled setpoint field, so an arc or reflected-power trip
            // DURING the hold window is byte-for-byte what a hold kill looks like. Attribution
            // therefore comes from the one thing actually known — the hold script's own text —
            // and never from the diff. A FAILURE_HOLD that is a single
            // `shutter Home` de-energises nothing at all.
            bool byTheHold = script.Any(l =>
                LineDeEnergises(l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), id));

            if (byTheHold)
            {
                SendRecipeLog($"Fault pause: {id} {what} — de-energised by the hold recipe");
                done.Add($"{id} {what} (de-energised by FAILURE_HOLD)");
            }
            else
            {
                // Still armed: the restoration set is deliberately NOT re-litigated here. Whether
                // a self-tripped supply should be re-energised on Retry has a physical consequence
                // either way — re-commanding a faulted supply on, versus silently depositing with
                // a dead source — and that call belongs to the operator, not to a guess made from
                // a diff. So the arming stands, and the report says plainly what is unknown and
                // what a Retry will do about it.
                SendRecipeLog($"Fault pause: {id} {what} during the hold — cause unknown, " +
                    $"no {HoldRecipeName} line names it");
                done.Add($"{id} {what} (went off during the hold)");
                problems.Add($"{id} went to {what} during the hold, but no {HoldRecipeName} line " +
                    "names it — it may have tripped on its own (arc, interlock, lost link). It is " +
                    "armed for restoration, so a Retry WILL command it back on: clear the fault " +
                    "first, or Abort.");
            }
        }

        // Deferred from the unresolved-psuId branch above, now that BOTH producers have run and
        // `zeroed` is the whole of what this hold de-energised. Empty → the reassuring line,
        // which most pausable failures (deposit, pwait, holder, delay, state) still earn. Not
        // empty → say only the part that is true, and name what the hold did kill, so the
        // operator is never told the plasma is untouched next to a line saying it was cut.
        if (unresolvedFailedLine)
        {
            string line = zeroed.Count == 0
                ? "no power supply resolved from the failed line — nothing was de-energised"
                : "no power supply resolved from the failed line — but "
                  + string.Join(", ", zeroed.Select(z => z.PsuId))
                  + " went off during the hold";
            SendRecipeLog($"Fault pause: {line}");
            done.Add(line);
        }

        foreach (var p in problems)
        {
            SendRecipeLog($"  hold: {p}");
            core.SendLogMesssage("Recipe", $"HOLD INCOMPLETE: {OneLine(p)}");
        }
        return new HoldReport(done, problems, zeroed);
    }

    /// <summary>
    /// Runs the hold script inside its own <see cref="_holdInFlight"/> flow.
    /// <para>The assignment MUST be the first statement of this async method and MUST NOT be
    /// moved into the caller: there it would reach the handler's own flow, and with it the
    /// nested-pause chain. Written here,
    /// the value reaches every continuation and nested call of the hold (ExecutionContext
    /// flows) and reaches nothing else: <c>AsyncMethodBuilderCore.Start</c> restores the
    /// caller's ExecutionContext when this state machine's first <c>MoveNext</c> returns, so
    /// <see cref="RunFailureHold"/> and the resumed recipe keep seeing <c>false</c>.</para>
    /// <para>Nothing clears it, and nothing needs to: the value lives in this flow, which ends
    /// with the hold. A second, concurrent hold gets its own.</para>
    /// </summary>
    private async Task<bool> RunHoldScript(string[] script)
    {
        _holdInFlight.Value = true;
        return await ExecuteRecipeWithInterpreter(
            string.Join('\n', script), new Interpreter(), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for an overrunning hold to finish before the operator's decision is ACTED on.
    /// Without this a still-running hold and a resumed recipe drive the machine at the same
    /// time — the hold closing the shutter while a retried <c>deposit</c> reopens it. The wait
    /// is itself bounded (generously); on timeout we resume anyway and say so, because the
    /// alternative is an unbounded block after the operator has already decided.
    /// </summary>
    private async Task DrainUnfinishedHold()
    {
        var hold = Interlocked.Exchange(ref _unfinishedHold, null);
        if (hold is null || hold.IsCompleted) return;

        string line = "Fault pause: waiting for the unfinished hold to end before resuming";
        SendRecipeLog(line);
        core!.SendLogMesssage("Recipe", line);

        using var drainCts = new CancellationTokenSource();
        try
        {
            if (await Task.WhenAny(hold, Task.Delay(_holdDrainBudget, drainCts.Token)).ConfigureAwait(false) != hold)
            {
                line = $"Fault pause: the hold is STILL running after {_holdDrainBudget.TotalSeconds:0} s — "
                     + "resuming anyway; it and the recipe may command the same devices";
                SendRecipeLog(line);
                core.SendLogMesssage("Recipe", line);
            }
        }
        finally
        {
            drainCts.Cancel();
        }
    }

    /// <summary>
    /// <para>Why this is first: <c>OpenShutterForDeposition</c> queues a self-running
    /// <c>QL…WT/QR25…QE</c> program on the drive AND sets its <c>_lastFlangePosition</c> to the
    /// end flange at queue-LOAD time, so <c>MoveToFlangeQueued</c>'s "already there" guard makes
    /// a later <c>shutter Home</c> send nothing at all and the shutter stays open over the
    /// substrate. <see cref="BaseStepperDrive.AbortMotion"/> sends QK, ST, RX1 0, RX3 0 and
    /// zeroes that flange record, which is exactly what defeats the guard.</para>
    /// <para>FLAGGED ASSUMPTIONS, not claims: QK mapping to Kill (stop, clear buffer, clear
    /// queue index, clear remaining WT, clear the waiting flag) is proven in the EMULATOR only —
    /// real hardware is untested. There is no settle between ST and the hold's immediate
    /// <c>shutter Home</c>, and none in the Delphi original either; commands serialise through
    /// the port semaphore. RE is deliberately omitted because it restarts the drive and loses
    /// the position counter, forcing a re-home after every recoverable fault — it is the
    /// escalation if a real drive ignores QK mid-WT, and that is a human's call.</para>
    /// <para>Retry consequence: AbortMotion also aborts the Holder and zeroes ITS flange record,
    /// so a retried <c>deposit</c> re-issues its holder traverse before reopening the shutter.
    /// That is correct — the holder must actually be over the source — but it means the resumed
    /// dwell starts after a traverse. Deliberately not compensated for.</para>
    /// <para>Deliberately NOT <c>AbortDriveMotion()</c>: that sweep swallows every per-drive
    /// failure into a log line and returns void, which is right for a best-effort pass before
    /// the failure protocol and wrong here, where the dialog must report a drive that would
    /// not stop.</para>
    /// <para><b>KNOWN ISSUE — do not soften: an operator PSU cut mid-deposit can WEDGE the HW
    /// thread here.</b> <see cref="BaseStepperDrive.AbortMotion"/>'s
    /// <c>SendCommand</c> calls are synchronous serial I/O (sync-over-async over the port's
    /// acquire semaphore, no deadline, no cancellation observed) reached ONLY via the cut path,
    /// with the shutter mid-queue. The <c>OperatorCutMidDeposit</c> harness case
    /// wedges here intermittently: the Agent thread keeps answering
    /// while the HW thread never logs again and never processes <c>recipe.abort</c>. The
    /// operator panel's power-OFF reaches this
    /// same synchronous path, so a real operator at the real tool can hit this too — see
    /// docs/superpowers/specs/2026-09-04-operator-hold-design.md §10 and
    /// .superpowers/sdd/2026-09-04-operator-hold/task-10-report.md §10 for the full evidence and
    /// the sibling stall in <see cref="ReEnergiseAfterCut"/>'s <c>OutputOn()</c> call. The fix
    /// belongs in the cut path's synchronous device I/O, not here.</para>
    /// </summary>
    private void AbortDrivesForHold(List<string> done, List<string> problems)
    {
        if (!core!.hwElements.Item.TryGetValue(DeviceType.Drive, out var drives) || drives is null)
            return;

        var stopped = new List<string>();
        foreach (var item in drives)
        {
            if (item.Value is not BaseStepperDrive d || !d.Connected) continue;
            try
            {
                d.AbortMotion();
                stopped.Add(item.Key);
            }
            catch (Exception ex)
            {
                problems.Add($"could not stop drive {item.Key}: {ex.Message}");
            }
        }

        if (stopped.Count == 0) return;
        string line = $"Fault pause: drives stopped — {string.Join(", ", stopped)}";
        SendRecipeLog(line);
        core.SendLogMesssage("Recipe", line);
        done.Add($"drive motion aborted: {string.Join(", ", stopped)}");
    }

    /// <summary>
    /// Posts the dialog request and waits. Returns the decision, who made it
    /// ("operator" / "watchdog" / "operator-stop") and how long the wait took. An operator Stop
    /// rethrows <see cref="OperationCanceledException"/> after recording the event's fields.
    /// </summary>
    private async Task<(FaultDecision Decision, string By, TimeSpan Waited)> WaitForFaultDecisionAsync(
        string step, string reason, HoldReport hold, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        // Read ONCE, at entry: everything below — the deadline, whether the dialog is posted at
        // all, and which wait is used — must agree about what kind of wait this is.
        bool held = core!.IsHeldForOperator;
        // A held pause has NO deadline. The operator was handed the machine to fix; a watchdog
        // that aborted the run out from under them mid-repair is exactly the failure Hold exists
        // to prevent.
        var timeout = held ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(core.FaultPauseTimeoutSeconds);
        var tcs = new TaskCompletionSource<FaultDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.SetPendingFaultDecision(tcs)?.TrySetCanceled();
        // RULING C. A held wait must NOT re-post the pause. Hold's whole purpose is to CLOSE the
        // dialog and let the operator at the machine; re-entering this method with the pause
        // re-posted would re-open the dialog it just closed and defeat the feature entirely. The
        // finally's SendFaultPauseEndToUI already closed it on the way out of the previous wait,
        // so the operator sees it close once and stay closed. The decision seam is unchanged: a
        // fresh TCS is installed above, so ResolveFault — the dialog's buttons, the toolbar, the
        // agent and the MCP bridge alike — still reaches this wait.
        //
        // Derived from the timeout rather than from `held`, so it stays right for any future
        // caller: an infinite wait has NO deadline, and `started + timeout` on
        // Timeout.InfiniteTimeSpan would put one 1 ms in the past.
        DateTime? deadlineUtc = timeout == Timeout.InfiniteTimeSpan ? null : started + timeout;
        if (!held)
            core.SendFaultPauseToUI(new FaultPauseInfo(step, reason, deadlineUtc, hold));

        // Own linked source so the losing Task.Delay's timer and its registration on the
        // recipe's token do not survive an answered pause and accumulate across a retry loop.
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            // Held: an infinite delay, so this task can only ever complete by CANCELLATION —
            // i.e. an operator Stop / AbortRecipe, which must still end the run.
            var watchdog = Task.Delay(timeout, watchdogCts.Token);
            var winner = held
                ? await WaitHeldWithWarnings(tcs.Task, watchdog, step, started, watchdogCts.Token).ConfigureAwait(false)
                : await Task.WhenAny(tcs.Task, watchdog).ConfigureAwait(false);
            if (winner == tcs.Task && tcs.Task.IsCompletedSuccessfully)
                return (tcs.Task.Result, "operator", DateTime.UtcNow - started);
            if (winner == tcs.Task && tcs.Task.IsCanceled)
                throw new OperationCanceledException("fault pause cancelled (recipe ended)");
            // Watchdog: `await watchdog` throws when the token was cancelled, else it elapsed.
            await watchdog.ConfigureAwait(false);
            return (FaultDecision.Abort, "watchdog", DateTime.UtcNow - started);
        }
        catch (OperationCanceledException)
        {
            // Drain what the operator hand-drove, exactly as the decision loop's own event does.
            // This path is NOT a corner case: hold, hand-drive the supply, conclude it is dead,
            // press Stop rather than Abort in the dialog. Without the drain the signed record
            // shows an uninterrupted deposition that was in fact hand-driven — the very thing
            // NoteHeldManualCommand exists to prevent. It must also be drained (not merely
            // reported) so a later run cannot inherit this hold's commands.
            _pauseEvents.Add(new PauseEvent(DateTime.UtcNow, step, reason, "abort", "operator-stop",
                core.TakeHeldManualCommands()));
            string line = $"FAULT DECISION: abort (operator-stop, after {(DateTime.UtcNow - started).TotalSeconds:0} s)";
            SendRecipeLog(line);
            core.SendLogMesssage("Recipe", line);
            // NOT SetHeldForOperator(false) here either. An operator Stop leaves through this
            // rethrow and never returns to the decision loop — but it also unwinds through
            // ExecuteWithFaultPause's `using var faultHandling` scope, whose Dispose ends the
            // hold on EVERY exit including this one, such as a Stop inside ReEnergiseAfterCut
            // that never passes through this method.
            throw;
        }
        finally
        {
            watchdogCts.Cancel();                  // release the timer and the token registration
            // NOT SetHeldForOperator(false). This finally runs on every exit, including a held
            // wait handing Retry back to the decision loop — and a Retry whose re-energise is
            // then REFUSED has ended nothing: the line has not re-run, the machine is still
            // parked exactly where the hold left it, and the operator's tools must not be taken
            // back there nor the watchdog they had deliberately disarmed re-armed.
            // Two sites own the flag instead, with different jobs: EndHoldIfAny, where a HUMAN
            // ends a hold (and it is announced), and Core.ExitFaultHandling — the pause's
            // `using var` scope — which guarantees no hold outlives the pause however the pause
            // unwinds, including a Stop caught inside ReEnergiseAfterCut.
            core.CancelPendingFaultDecision();
            core.SendFaultPauseEndToUI();
        }
    }

    /// <summary>
    /// Waits out a hold. Never ends the wait itself — only the operator's decision or a run
    /// cancellation does that — but logs a warning every <see cref="Core.HeldWarningSeconds"/>
    /// so an unattended hold is not silent.
    /// <para>This is load-bearing, not a nicety. A hold disarms the watchdog on purpose, and
    /// either an operator or the agent may begin one, so nothing else in the system will ever
    /// say that a run is parked mid-recipe with the machine hand-drivable. The warning changes
    /// no machine state and never ends the run.</para>
    /// </summary>
    private async Task<Task> WaitHeldWithWarnings(
        Task<FaultDecision> decision, Task watchdog, string step, DateTime started, CancellationToken ct)
    {
        int every = core!.HeldWarningSeconds;      // read once; a mid-hold config change is not a thing
        if (every <= 0) return await Task.WhenAny(decision, watchdog).ConfigureAwait(false);

        // Own source so the losing tick timer is not left armed for up to `every` seconds after
        // the operator has answered — the same discipline the watchdog above keeps.
        using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            while (true)
            {
                var tick = Task.Delay(TimeSpan.FromSeconds(every), tickCts.Token);
                var winner = await Task.WhenAny(decision, watchdog, tick).ConfigureAwait(false);
                if (winner != tick) return winner;
                // A CANCELLED tick is not an elapsed interval: without this the next iteration
                // would create an already-cancelled tick, win on it again, and spin hot while
                // logging a warning per pass. Throw instead — the caller's catch records the
                // operator-stop and rethrows, exactly as it does for the watchdog.
                ct.ThrowIfCancellationRequested();

                var age = DateTime.UtcNow - started;
                string howLong = age.TotalMinutes >= 1
                    ? $"{age.TotalMinutes:0} min"
                    : $"{age.TotalSeconds:0} s";
                string warn = $"Recipe HELD for {howLong} on: {step} — manual control is unlocked "
                            + "and the run is waiting for Resume, Skip or Abort";
                SendRecipeLog(warn);
                core.SendLogMesssage("Recipe", warn);
            }
        }
        finally
        {
            tickCts.Cancel();
        }
    }

    /// <summary>
    /// For a <c>deposit</c> line whose dwell had already started when it failed, substitutes
    /// the remaining seconds (floor 1 s) so Retry does not deposit the whole layer again.
    /// Every other line is returned unchanged. Single-chamber form: <c>deposit &lt;src&gt; &lt;dwell&gt; &lt;retract&gt;</c> (third arg parks the SHUTTER);
    /// cluster form: <c>deposit &lt;src&gt; &lt;dwell&gt;</c> — dwell is parts[2] in both, already in
    /// canonical seconds (NormalizeTimeArgs ran on the first attempt).
    ///
    /// <paramref name="failedAtUtc"/> is the instant the step failed, NOT the instant the
    /// operator answered: the hold closes the shutters, so the pause deposits nothing and its
    /// duration must not be subtracted. Reading the clock here would truncate every retried
    /// layer to the 1 s floor once the operator deliberated longer than the planned dwell.
    /// </summary>
    internal string[] AdjustForResume(string[] parts, DateTime attemptStartedUtc, DateTime failedAtUtc)
    {
        if (parts.Length < 3 || !parts[0].Equals("deposit", StringComparison.OrdinalIgnoreCase))
            return parts;
        var dwell = core!.LastDwell;
        core.LastDwell = null;
        if (dwell is null || dwell.StartedUtc < attemptStartedUtc) return parts;   // dwell never began this attempt
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double planned))
            return parts;
        double elapsed = (failedAtUtc - dwell.StartedUtc).TotalSeconds;
        double remaining = Math.Max(1, Math.Min(planned, dwell.Seconds) - elapsed);
        var adjusted = (string[])parts.Clone();
        adjusted[2] = remaining.ToString("R", CultureInfo.InvariantCulture);
        string line = $"Deposit resumed with {remaining:0} s of {planned:0} s remaining";
        SendRecipeLog(line);
        core.SendLogMesssage("Recipe", line);
        return adjusted;
    }

    /// <summary>Lines whose Skip deserves a warning in the dialog: later lines assume the
    /// energy / gas state they set.</summary>
    internal static bool IsEnergyStep(string[] parts)
    {
        if (parts.Length == 0) return false;
        string cmd = parts[0];
        return cmd.Equals("power", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("set", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("ramp", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("warmup", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("MFC", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("valve", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetFaultPauseState()
    {
        _pauseEvents.Clear();
        _skippedSteps.Clear();
        _currentStepNumber = 0;
        core!.LastDwell = null;
        core!.ClearOperatorCut();
        core.SetHeldForOperator(false);
        core.ResetFaultHandlingDepth();
        core.TakeHeldManualCommands();
    }
}
