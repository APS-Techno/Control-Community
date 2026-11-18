using APS.ProcessPanel.Contracts;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;
using APS.RecipeModel.Model;
using APS.RecipeModel.Serialization;
using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Recipe;

namespace APS.SystemControl.Core;

partial class CoreThread : BaseThread
{
    /// <summary>
    /// Reason for the most recent failed recipe command (op exception message, or
    /// a "returned no result" note for a null op result). Surfaced in the top-level
    /// "Recipe failed — ..." message so operators see WHY, not just THAT it failed.
    /// </summary>
    private string? _lastCommandError;

    /// <summary>Outcome of the run in progress, set by <see cref="Terminal"/> at each terminal
    /// "[Recipe] Recipe complete/failed/aborted/INCOMPLETE:" line and written into the run's
    /// <c>.run.json</c> sidecar by the finally (Core.WriteRunRecord). Null until the run ends.</summary>
    private string? _runOutcome;
    private string? _runOutcomeMessage;

    /// <summary>
    /// Emits the terminal "[Recipe] Recipe &lt;label&gt;: &lt;name&gt; &lt;tail&gt;" event line and records
    /// the outcome for the run sidecar. <paramref name="label"/> is the log word (complete,
    /// failed, aborted, INCOMPLETE); <paramref name="outcome"/> the RunRecord vocabulary;
    /// <paramref name="tail"/> starts with the "(elapsed)" and carries any " — detail".
    /// </summary>
    private string Terminal(string label, string outcome, string recipeName, string tail)
    {
        string line = $"Recipe {label}: {recipeName} {tail}";
        _runOutcome = outcome;
        _runOutcomeMessage = tail;
        core!.SendLogMesssage("Recipe", line);
        core!.RaiseRecipeLifecycle(label.ToLowerInvariant(), recipeName, tail);   // complete|failed|aborted|incomplete
        return line;
    }

    /// <summary>
    /// Builds the top-level recipe-failure message, appending the captured
    /// <see cref="_lastCommandError"/> so the operator-visible "Recipe failed — ..."
    /// line names the actual cause instead of being blank. No leading newline: a
    /// leading '\n' made RecipeEventFormat.FirstLine() return an empty string.
    /// </summary>
    private string RecipeFailedMessage(string fileName)
    {
        string detail = string.IsNullOrWhiteSpace(_lastCommandError)
            ? ""
            : $" — {_lastCommandError!.Trim()}";
        return $"Recipe {System.IO.Path.GetFileName(fileName)} failed{detail}";
    }

    /// <summary>
    /// Flattens a multi-line reason into one log line. Operation messages are
    /// authored with a leading '\n' (so they read well in the error dialog),
    /// which would otherwise split a single event-log entry in two.
    /// </summary>
    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// The reason clause for an embedded recipe that returned false. Falls back
    /// to the bare "interpreter returned false" only when no step recorded a
    /// cause — that alone tells the operator nothing about which supply, valve or
    /// MFC actually refused.
    /// </summary>
    private string EmbeddedFailureDetail() =>
        string.IsNullOrWhiteSpace(_lastCommandError)
            ? " — interpreter returned false"
            : $" — {OneLine(_lastCommandError!).Trim()}";

    /// <summary>
    /// Reports a run that declared onStepFailure="continue", reached its end, but
    /// could not complete every step. Never silently, and never as success.
    /// </summary>
    private void ReportIncompleteTeardown(string recipeName, string elapsed)
    {
        string summary = Terminal("INCOMPLETE", Logging.RunRecord.Incomplete, recipeName,
            $"({elapsed}) — {_failureProtocolFailures.Count} step(s) failed. The system may NOT be in a " +
            "safe state — check the power supplies, gas valves, MFCs and the vent valve.");

        SendRecipeLog(summary);

        // Which step refused is the whole diagnostic value of an incomplete
        // teardown, so it goes to the operator event log directly. SendRecipeLog
        // alone would hide it: embedded-recipe step logging is off by
        // default (Core.LogEmbeddedRecipes).
        foreach (string failure in _failureProtocolFailures)
        {
            SendRecipeLog($"  failed: {failure}");
            // No leading indent: EventLogClassifier.Classify downgrades any
            // message starting with whitespace to Detail, which would strand
            // this line off the operator events overlay.
            core!.SendLogMesssage("Recipe", $"INCOMPLETE step: {OneLine(failure)}");
        }

        core!.SendErrorMesssage(summary);
    }

    internal void ProcessControllerCommand(ThreadMsg msg)
    {
        if (core!.hwElements.Item[DeviceType.Controller][msg.Device] is BaseController controller)
        {
            if (msg.Type == MsgType.Command)
            {
                if (msg is ParamMsg pMsg)
                {
                    controller.Command(pMsg.Args);
                }
            }
        }
    }


    /// <summary>
    /// Startup latency above this is called out as a problem rather than just a
    /// number. Starting a recipe is bookkeeping plus one cached sensor read, so
    /// anything near half a second means something on the path went back to
    /// doing blocking I/O (see <see cref="LogBasePressure"/>).
    /// </summary>
    private const int RecipeStartupSlowMs = 500;

    /// <summary>
    /// Takes the name rather than the <see cref="ThreadMsg"/> because the caller
    /// disposes the message as soon as it returns, while this method runs on.
    /// </summary>
    internal async Task ProcessEmbeddedRecipe(string recipeName)
    {
        var startedUtc = DateTime.UtcNow;
        if (core!.embRecipes is null || !core!.embRecipes.Contains(recipeName))
        {
            string err = $"Embedded recipe '{recipeName}' is not defined in EmbedRecipes.xml";
            SendRecipeLog($"ERROR: {err}");
            core!.SendErrorMesssage(err);
            core!.SendLogMesssage("Recipe", $"Recipe failed: {recipeName} ({RecipeEventFormat.FormatDuration(TimeSpan.Zero)}) — not defined in EmbedRecipes.xml");
            return;
        }
        // State gate: the recipe's notIn="…" names chamber states in which a button or
        // host launch is refused. Checked BEFORE the slot so a refusal never claims it.
        // The slot alone is not enough — the chamber stays in Processing after a run
        // while gas still flows, and venting or opening a gate into it then is exactly
        // what the profile forbids. (Nested 'call' does not come through here.)
        SystemState chamberState = core!.SystemState;
        if (core!.embRecipes.IsBlockedIn(recipeName, chamberState))
        {
            string why = $"Embedded recipe '{recipeName}' is not allowed while the chamber is {chamberState}";
            SendRecipeLog($"INTERLOCK: {why}");
            core!.WriteGUI(new ValMsg<string>(MsgType.Interlock, DeviceType.System, "Recipe", why));
            core!.SendLogMesssage("Recipe", $"Recipe refused: {recipeName} — chamber is {chamberState}");
            return;
        }
        // Single-flight: a button-triggered embedded recipe shares the same
        // hardware, data logger and recipe state as any running recipe — reject
        // it rather than cancelling/colliding with one already in progress.
        // (Nested 'call' invocations go through the interpreter, not here, so
        // they are unaffected.)
        if (!core!.TryBeginRecipeSlot())
        {
            string busy = $"A recipe is already running — embedded recipe '{recipeName}' ignored";
            SendRecipeLog($"ERROR: {busy}");
            core!.SendErrorMesssage(busy);
            return;
        }
        core!.IsUserRecipe = false;
        _recipeName = recipeName;
        _recipeStartTime = DateTime.Now;
        // Cleared per run so a failure is never reported with an earlier recipe's reason.
        _lastCommandError = null;
        _runOutcome = null; _runOutcomeMessage = null;
        // Embedded: no fallback path, so an unlogged embedded run still writes nothing (it can
        // neither pause nor be held). Called anyway so a previous USER run's path cannot leak
        // into this one.
        core!.BeginRunRecord(recipeName, _recipeStartTime, embedded: true);

        // The recipe declares its own step-failure policy; nothing is inferred
        // from its id. See EmbeddedRecipes.ContinueOnStepFailure.
        bool tolerant = core!.embRecipes!.ContinueOnStepFailure(recipeName);
        if (tolerant)
        {
            _continueOnError = true;
            _failureProtocolFailures.Clear();
        }

        string Elapsed() => RecipeEventFormat.FormatDuration(DateTime.Now - _recipeStartTime);
        try
        {
            // SystemState is driven by the state machine and the 'state <name>' DSL
            // command, not forced to Processing here: pump-down recipes that begin
            // from atmosphere need Startup.
            core!.StartRecipe();
            var token = core!.RecipeCancellationToken;
            LogBasePressure();   // Delphi unit_DS.ExeciteScript:696 parity (embedded recipes)
            // StartRecipe/LogBasePressure/StartDataLogging are INSIDE the try so the
            // finally's EndRecipeSlot always runs. StartDataLogging can throw
            // (UnauthorizedAccessException on a read-only / ACL-restricted Logs dir),
            // which would otherwise leak the single-flight slot permanently.
            if (core!.RecipeLoggingEnabled)
                core!.StartDataLogging(recipeName);
            core!.SendLogMesssage("Recipe", $"Recipe started: {recipeName}");
            core!.RaiseRecipeLifecycle("started", recipeName, "");

            // Everything above runs on the caller's thread (an async method runs
            // synchronously until its first await), so a blocking call up there
            // stalls the operator with no explanation. Report the number every run
            // so a regression is visible instead of being mistaken for a frozen UI.
            long startupMs = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds;
            core!.SendLogMesssage("Recipe",
                $"Recipe start latency: {startupMs} ms"
                + (startupMs >= RecipeStartupSlowMs ? " — slow, check the PLC/sensor link" : ""));

            // Route through the interpreter pipeline so embedded recipes can use 'call'
            // to invoke other embedded recipes (e.g. a button-triggered recipe that calls
            // LOAD_SUBSTRATE). The legacy ExecuteRecipe(string[]) path doesn't know 'call'.
            string[] script = core!.embRecipes!.Recipe(recipeName);
            string scriptText = string.Join('\n', script);
            var interpreter = new Interpreter();
            if (await ExecuteRecipeWithInterpreter(scriptText, interpreter, token))
            {
                // In continue-on-error mode the run always reports true, so a
                // teardown that limped to the end must be judged on its recorded
                // failures — never announced as complete.
                if (tolerant && _failureProtocolFailures.Count > 0)
                    ReportIncompleteTeardown(recipeName, Elapsed());
                else
                    Terminal("complete", Logging.RunRecord.Complete, recipeName, $"({Elapsed()})");
            }
            // A tolerant run can also reach here via a raw exception inside
            // ExecuteTokenLines (missing 'call' target, bad 'goto' label, 'if'
            // naming an unknown sensor) rather than through a guarded step —
            // ExecuteRecipeWithInterpreter swallows it and returns false. Any
            // failures already recorded before that point must still be
            // reported, not discarded in favour of a bare "Recipe failed".
            else if (tolerant && _failureProtocolFailures.Count > 0)
                ReportIncompleteTeardown(recipeName, Elapsed());
            else
                Terminal("failed", Logging.RunRecord.Failed, recipeName, $"({Elapsed()}){EmbeddedFailureDetail()}");
        }
        catch (OperationCanceledException)
        {
            // Same reasoning as the false-return branch above: an operator who
            // aborts a tolerant teardown mid-run must see every step that had
            // already failed, not just the fact that it was aborted.
            if (tolerant && _failureProtocolFailures.Count > 0)
                ReportIncompleteTeardown(recipeName, Elapsed());
            else
                Terminal("aborted", Logging.RunRecord.Aborted, recipeName, $"({Elapsed()})");
        }
        catch (Exception ex)
        {
            // ProcessEmbeddedRecipe runs fire-and-forget (HWDispatcher discards
            // the task), so rethrowing here only produced an unobserved task
            // fault. Log and contain — the failure is already surfaced and the
            // finally still runs the cleanup.
            Terminal("failed", Logging.RunRecord.Failed, recipeName, $"({Elapsed()}) — {RecipeEventFormat.FirstLine(ex.Message)}");
            core!.SendErrorMesssage($"Embedded recipe '{recipeName}' failed: {ex.Message}");
        }
        finally
        {
            _continueOnError = false;   // never leaks into the next run
            core!.IsUserRecipe = false;
            // OnRecipeEnd re-evaluates group transitions, which read live PLC
            // state and can throw PlcCommException. It MUST NOT prevent
            // EndRecipeSlot from running — otherwise a transient comm hiccup would
            // leak the single-flight slot and reject every future recipe until restart.
            try { core!.OnRecipeEnd(); }
            catch (Exception ex) { core!.SendErrorMesssage($"OnRecipeEnd failed: {ex.Message}"); }
            string? tsv = null;
            try { tsv = core!.StopDataLogging(); }
            catch (Exception ex) { core!.SendErrorMesssage($"StopDataLogging failed: {ex.Message}"); }
            core!.WriteRunRecord(tsv, recipeName, recipePath: null, embedded: true, _recipeStartTime,
                _runOutcome ?? Logging.RunRecord.Unknown, _runOutcomeMessage);
            core!.EndRecipeSlot();
        }
    }

    /// <summary>
    /// Stepper drives execute queued programs autonomously (a deposit dwell is a
    /// WT loop ON the drive), so cancelling the recipe task leaves the program
    /// running and every later drive command — the failure protocol's
    /// <c>shutter Home</c>, a panel jog, the next recipe — queues behind it.
    /// Delphi's abort sent Kill/Stop/Reset to both drives; do the same (minus
    /// the drive restart) before the failure protocol runs.
    /// </summary>
    private void AbortDriveMotion()
    {
        if (!core!.hwElements.Item.TryGetValue(DeviceType.Drive, out var drives) || drives == null)
            return;
        foreach (var item in drives)
        {
            if (item.Value is not BaseStepperDrive d || !d.Connected) continue;
            try
            {
                d.AbortMotion();
                SendRecipeLog($"Drive {item.Key}: queue killed, motion stopped");
            }
            catch (Exception ex)
            {
                SendRecipeLog($"Drive {item.Key}: abort failed — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Runs the FAILURE_PROTOCOL embedded recipe to bring the system to a safe state.
    /// Called from recipe error/abort catch blocks. Errors are logged but never propagated.
    /// </summary>
    internal async Task RunFailureProtocol()
    {
        const string protocolName = "FAILURE_PROTOCOL";
        string[] script = core!.embRecipes?.Recipe(protocolName) ?? [];
        if (script.Length == 0)
        {
            SendRecipeLog("Failure protocol: no FAILURE_PROTOCOL recipe defined, skipping");
            return;
        }

        SendRecipeLog("Running failure protocol...");
        core!.SendLogMesssage("Core", "Running failure protocol");

        // Clear before the run, not after. This method is reached from
        // ExecuteRecipe's catch blocks, where _lastCommandError already holds the
        // reason the ORIGINAL recipe failed, and it is never cleared on a
        // successful step. The "protocol did not complete" line below reads it,
        // so without this reset a structural failure inside the protocol would be
        // reported with the previous recipe's reason — naming the wrong device to
        // an operator deciding what to touch by hand.
        _lastCommandError = null;

        // EVERY step must be attempted. Normal recipe execution stops at the
        // first failing command, which is right for a process recipe and wrong
        // here: the whole point of the failure protocol is that the machine is
        // already in a bad state, and each remaining line — de-energising the
        // other supplies, zeroing the MFCs, closing the gas valves — is an
        // independent safety action that does not depend on the previous one
        // having worked.
        _continueOnError = true;
        _failureProtocolFailures.Clear();
        int attempted = 0;
        // Agent-facing status: the protocol reports progress under the failed recipe's name,
        // so mark the phase explicitly for pollers (RecipeProgressTracker keeps the outcome).
        APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.SetFailureProtocolActive(true);

        try
        {
            // Route through the interpreter pipeline for consistency with the rest of
            // the embedded-recipe execution path (so 'call' would work here too).
            string scriptText = string.Join('\n', script);
            var interpreter = new Interpreter();
            // ExecuteRecipeWithInterpreter catches exceptions raised outside a
            // guarded step (missing 'call' target, bad 'goto' label, 'if' naming
            // an unknown sensor) internally and returns false WITHOUT adding
            // anything to _failureProtocolFailures — the catch block below never
            // fires for that case, so check the bool explicitly.
            if (!await ExecuteRecipeWithInterpreter(scriptText, interpreter, CancellationToken.None))
                _failureProtocolFailures.Add(
                    $"protocol did not complete: {_lastCommandError ?? "interpreter returned false"}");
            attempted = script.Length;
        }
        catch (Exception ex)
        {
            // Continue-on-error handles per-step failures, so reaching here means
            // the run itself came apart (block structure, tokenizer). Report it —
            // never silently, and never as success.
            _failureProtocolFailures.Add($"protocol aborted: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _continueOnError = false;
            APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.SetFailureProtocolActive(false);
        }

        if (_failureProtocolFailures.Count == 0)
        {
            SendRecipeLog("Failure protocol complete");
            core!.SendLogMesssage("Core", "Failure protocol complete");
            return;
        }

        // Report honestly. A partially-applied safety protocol is exactly the
        // situation where the operator must not be told everything is fine.
        string summary =
            $"Failure protocol INCOMPLETE — {_failureProtocolFailures.Count} of " +
            $"{(attempted > 0 ? attempted : _failureProtocolFailures.Count)} step(s) failed. " +
            "The system may NOT be in a safe state — check gas valves and MFCs.";

        SendRecipeLog(summary);
        core!.SendLogMesssage("Core", summary);

        // Which step refused is the actionable part — it names the supply, valve
        // or MFC the operator has to go and handle by hand. SendRecipeLog alone
        // routes it to the recipe step-log stream and only while that stream is
        // enabled (RecipeLoggingEnabled: LogUserRecipes here, since the protocol
        // runs inside a user recipe). A safety protocol's failures must not be
        // contingent on a logging toggle, so they go to the operator event log
        // unconditionally, next to the summary.
        foreach (string failure in _failureProtocolFailures)
        {
            SendRecipeLog($"  failed: {failure}");
            // No leading indent: EventLogClassifier.Classify downgrades any
            // message starting with whitespace to Detail, which would strand
            // this line off the operator events overlay.
            core!.SendLogMesssage("Core", $"INCOMPLETE step: {OneLine(failure)}");
        }

        core!.SendErrorMesssage(summary);
    }

    /// <summary>
    /// When set, a failing recipe step is recorded and execution continues with
    /// the next one instead of aborting the run. Only the failure protocol turns
    /// this on — a process recipe must still stop at the first failure.
    /// </summary>
    internal bool _continueOnError;

    /// <summary>Steps that failed during the current continue-on-error run.</summary>
    internal readonly List<string> _failureProtocolFailures = [];

    /// <summary>
    /// Runs one resolved command. In continue-on-error mode a failure — whether
    /// signalled by a false/null result or by an exception — is recorded and
    /// swallowed so the remaining safety steps still execute.
    /// Cancellation is never swallowed.
    /// </summary>
    private async Task<bool> ExecuteResolvedPartsGuarded(string[] parts, CancellationToken cancellationToken)
    {
        // _holdInFlight counts exactly as _continueOnError here. A FAILURE_HOLD line must never
        // reach the pause path — a nested pause cancels the operator's own pending decision —
        // and must never escape the hold on a non-pausable failure, which would skip the
        // remaining shutter closes. It is a separate flag because _continueOnError is restored
        // as soon as the handler stops WAITING for an overrunning hold, while that hold is still
        // running its lines. See CoreThread.FaultPause.cs.
        if (!_continueOnError && !_holdInFlight.Value)
            return await ExecuteWithFaultPause(parts, cancellationToken).ConfigureAwait(false);

        string step = string.Join(' ', parts);
        try
        {
            if (!await ExecuteCountedParts(parts, cancellationToken).ConfigureAwait(false))
                RecordContinuedFailure(step, _lastCommandError ?? "step reported failure");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordContinuedFailure(step, $"{ex.GetType().Name}: {ex.Message}");
        }

        return true;   // keep going: the remaining safety steps still matter
    }

    private void RecordContinuedFailure(string step, string reason)
    {
        string entry = $"{step} — {reason}";
        // The fault-pause hold has its own sink: it runs INSIDE a user recipe, and on an overrun
        // it outlives the handler's bookkeeping restore, so appending to the shared list would
        // put a late hold line into the outer recipe's failure summary.
        if (_holdInFlight.Value) { lock (_holdFailures) _holdFailures.Add(entry); }
        else _failureProtocolFailures.Add(entry);
        SendRecipeLog($"  STEP FAILED (continuing): {step} — {reason}");
    }

    /// <summary>
    /// When an <see cref="Interpreter"/> is provided, variables, loops, and functions
    /// are resolved before each line is executed. Pass null for legacy behavior.
    /// </summary>
    public async Task<bool> ExecuteRecipe(string fileName, Interpreter? interpreter = null,
        IReadOnlyDictionary<string, string>? paramOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            SendRecipeLog("ERROR: No recipe file specified");
            core!.SendErrorMesssage("No recipe file specified");
            return false;
        }
        string recipeName = Path.GetFileNameWithoutExtension(fileName);
        bool result = false;
        // Single-flight: reject a new run while one is active rather than letting
        // StartRecipe() cancel the running recipe mid-operation.
        if (!core!.TryBeginRecipeSlot())
        {
            SendRecipeLog($"ERROR: a recipe is already running — '{recipeName}' ignored");
            core!.SendErrorMesssage("A recipe is already running.");
            return false;
        }
        core!.StartRecipe();
        var token = core!.RecipeCancellationToken;
        _recipeName = recipeName;
        _recipeStartTime = DateTime.Now;
        _lastCommandError = null;
        _runOutcome = null; _runOutcomeMessage = null;
        ResetFaultPauseState();
        // Settle where this run's .run.json goes BEFORE anything can pause or be held: with
        // recipe logging off there is no TSV to hang it off at run end, and the record carries
        // every pause and everything the operator hand-drove during a hold.
        core!.BeginRunRecord(recipeName, _recipeStartTime, embedded: false);
        string Elapsed() => RecipeEventFormat.FormatDuration(DateTime.Now - _recipeStartTime);
        core!.SendLogMesssage("Recipe", $"Recipe started: {recipeName}");
        core!.RaiseRecipeLifecycle("started", recipeName, "");
        try
        {
            core!.IsUserRecipe = true;
            // SystemState is driven by the state machine + the recipe-level
            // 'state <name>' DSL command (no implicit Processing here).
            LogBasePressure();   // Delphi unit_DS.ExeciteScript:696 parity (.recipe + .rcpx)
            SendRecipeLog($"Loading recipe: {fileName}");
            string script;
            if (Path.GetExtension(fileName).Equals(".rcpx", StringComparison.OrdinalIgnoreCase))
            {
                var recipeFile = RecipeSerializer.Load(fileName);
                // Apply operator-edited parameter values (per-run override) before
                // the var block is generated, so the recipe runs with edited values
                // rather than the file defaults. The .rcpx on disk is untouched.
                if (paramOverrides is { Count: > 0 })
                    recipeFile.ApplyParameterOverrides(paramOverrides);
                script = recipeFile.FullScript;
                interpreter ??= new Interpreter(); // .rcpx files need variable resolution
            }
            else
            {
                script = File.ReadAllText(fileName);
            }

            SendRecipeLog($"Recipe loaded, {script.Length} chars");

            var validator = core.BuildRecipeValidator();
            var validationFindings = validator.Validate(script);
            foreach (var f in validationFindings)
                SendRecipeLog($"  {f.Severity.ToString().ToUpperInvariant()}: line {f.Line}: {f.Message}");
            var hardErrors = validationFindings.Where(e => e.Severity == RecipeErrorSeverity.Error).ToList();
            if (hardErrors.Count > 0)
            {
                string summary = string.Join("; ", hardErrors.Select(e => e.Message));
                throw new InvalidOperationException($"\nRecipe validation failed: {summary}");
            }

            // Safety guard: every connected stepper drive must be homed, at the
            // home coordinate, AND stationary before a recipe can start. Physical
            // homing (SH7L) must be operator-initiated from the UI, never from a recipe.
            //
            // The drive reader thread updates IsHomed / IsMoving / CurrentPosition
            // asynchronously (50 ms). Take two consecutive samples separated by a
            // short interval and delegate the comparison to HomingCheck (pure,
            // unit-tested in CoreLogicTests).
            if (core.hwElements.Item.TryGetValue(DeviceType.Drive, out var drives) && drives != null)
            {
                const int StabilitySampleDelayMs = 120; // > one reader tick (50 ms)

                static Classes.HomingCheck.DriveSnapshot Snapshot(string id, BaseStepperDrive d)
                    => new(id, d.IsHomed, d.IsMoving, d.CurrentPosition);

                var first = new List<Classes.HomingCheck.DriveSnapshot>();
                foreach (var item in drives)
                {
                    if (item.Value is not BaseStepperDrive d || !d.Connected) continue;
                    first.Add(Snapshot(item.Key, d));
                }

                await Task.Delay(StabilitySampleDelayMs, token);

                var second = new List<Classes.HomingCheck.DriveSnapshot>();
                foreach (var item in drives)
                {
                    if (item.Value is not BaseStepperDrive d || !d.Connected) continue;
                    second.Add(Snapshot(item.Key, d));
                }

                var offenders = Classes.HomingCheck.FindOffenders(first, second);
                if (offenders.Count > 0)
                    throw new DrivesNotHomedException(Classes.HomingCheck.FormatError(offenders));
            }

            if (core!.RecipeLoggingEnabled)
                core!.StartDataLogging(recipeName);

            if (interpreter != null)
            {
                if (await ExecuteRecipeWithInterpreter(script, interpreter, token))
                    result = true;
                else
                    throw new InvalidOperationException(RecipeFailedMessage(fileName));
            }
            else
            {
                char[] separator = ['\r', '\n'];
                string[] lines = script.Split(separator, StringSplitOptions.RemoveEmptyEntries);

                if (await ExecuteRecipe(lines, cancellationToken: token))
                    result = true;
                else
                    throw new InvalidOperationException(RecipeFailedMessage(fileName));
            }

            if (_skippedSteps.Count > 0)
            {
                SendRecipeLog($"Recipe complete with {_skippedSteps.Count} skipped step(s)");
                Terminal("complete", Logging.RunRecord.CompleteWithSkips, recipeName,
                    $"({Elapsed()}) — {_skippedSteps.Count} step(s) skipped: {string.Join("; ", _skippedSteps)}");
            }
            else
            {
                SendRecipeLog("Recipe complete");
                Terminal("complete", Logging.RunRecord.Complete, recipeName, $"({Elapsed()})");
            }
            SendRecipeProgress(_recipeTotalSteps, "Complete", RecipeState.Complete);
        }
        catch (OperationCanceledException)
        {
            SendRecipeLog("Recipe aborted by user");
            Terminal("aborted", Logging.RunRecord.Aborted, recipeName, $"({Elapsed()})");
            SendRecipeProgress(0, APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.AbortedDescription, RecipeState.Idle);
            AbortDriveMotion();
            if (core!.AbortProtocolEnabled)
                await RunFailureProtocol();
        }
        catch (DrivesNotHomedException e)
        {
            Terminal("failed", Logging.RunRecord.Failed, recipeName, $"({Elapsed()}) — {RecipeEventFormat.FirstLine(e.Message)}");
            // Pre-flight safety check failed — no hardware state has been touched,
            // so skip the failure protocol (nothing to recover from).
            SendRecipeLog($"ERROR: {e.Message}");
            core!.SendErrorMesssage(e.Message);
            SendRecipeProgress(0, e.Message, RecipeState.Error);
        }
        catch (Exception e)
        {
            Terminal("failed", Logging.RunRecord.Failed, recipeName, $"({Elapsed()}) — {RecipeEventFormat.FirstLine(e.Message)}");
            SendRecipeLog($"ERROR: {e.GetType().Name}: {e.Message}");
            core!.SendErrorMesssage(e.Message);
            SendRecipeProgress(0, e.Message, RecipeState.Error);
            AbortDriveMotion();
            if (core!.FailureProtocolEnabled)
                await RunFailureProtocol();
        }
        finally
        {
            core!.IsUserRecipe = false;
            // OnRecipeEnd re-evaluates group transitions, which read live PLC
            // state and can throw PlcCommException. It MUST NOT prevent
            // EndRecipeSlot from running — otherwise a transient comm hiccup would
            // leak the single-flight slot and reject every future recipe until restart.
            try { core!.OnRecipeEnd(); }
            catch (Exception ex) { core!.SendErrorMesssage($"OnRecipeEnd failed: {ex.Message}"); }
            string? tsv = null;
            try { tsv = core!.StopDataLogging(); }
            catch (Exception ex) { core!.SendErrorMesssage($"StopDataLogging failed: {ex.Message}"); }
            // The sidecar names the file the run was started from: the agent's "recipe"
            // artifact resolves through it instead of a name search over Recipes\**.
            string? recipePath = null;
            try { recipePath = Path.GetFullPath(fileName); } catch { }
            core!.WriteRunRecord(tsv, recipeName, recipePath, embedded: false, _recipeStartTime,
                _runOutcome ?? Logging.RunRecord.Unknown, _runOutcomeMessage,
                pauses: _pauseEvents, skippedSteps: _skippedSteps);
            core!.EndRecipeSlot();
        }
        return result;
    }

    /// <summary>
    /// When an interpreter is provided, variable references ($name) in each line
    /// are resolved before the line is executed, while the existing command dispatch
    /// logic is preserved.
    /// </summary>
    public async Task<bool> ExecuteRecipe(string[] lines, Interpreter? interpreter = null, CancellationToken cancellationToken = default)
    {
        string[] parts;
        bool result = true;
        _recipeTotalSteps = lines.Length;
        // Set start time only if a caller hasn't already established one.
        // The user-recipe + embedded paths set _recipeStartTime up front so durations
        // reported from their catch blocks anchor at operator-Run. The failure protocol
        // reaches this overload without setting it, so default needs a baseline here
        // to prevent TimeSpan overflow in SendRecipeProgress.
        if (_recipeStartTime == default)
            _recipeStartTime = DateTime.Now;
        int stepIndex = 0;

        try
        {
            SendRecipeProgress(0, "Starting...", RecipeState.Running);

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (line.Equals("begin", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("finish", StringComparison.OrdinalIgnoreCase))
                {
                    stepIndex++;
                    continue;
                }

                stepIndex++;
                string trimmedLine = line.Trim();
                SendRecipeLog($"[{stepIndex}/{lines.Length}] {trimmedLine}");
                SendRecipeProgress(stepIndex, trimmedLine, RecipeState.Running);

                parts = Core.PrepareParts(line);

                // Blank (whitespace-only) and '#' comment lines are legal DSL and
                // stripped by the interpreter path; skip them here too instead of
                // crashing on an empty parts array or failing with "Command # not found".
                if (parts.Length == 0 || parts[0].StartsWith('#'))
                    continue;

                if (interpreter != null)
                    parts = ResolvePartsWithInterpreter(parts, interpreter);

                if (parts[0].Equals("var", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractVariable(parts, interpreter);
                    SendRecipeLog($"  var {parts[1]} = {(parts.Length > 3 ? parts[3] : parts[2])}");
                }
                else if (parts[0].Equals("delay", StringComparison.OrdinalIgnoreCase) ||
                         parts[0].Equals("wait", StringComparison.OrdinalIgnoreCase))
                {
                    float time = APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[1]);
                    SendRecipeLog($"  delay {time}s ...");
                    await core!.OpDelay(time, cancellationToken);
                    SendRecipeLog($"  delay done");
                }
                else if (parts[0].Equals("pwait", StringComparison.OrdinalIgnoreCase))
                {
                    SendRecipeLog($"  pwait {string.Join(' ', parts[1..])}");
                    parts = NormalizePwaitParts(parts);
                    // Resolve unit suffix on target so the worker receives canonical Torr.
                    parts[2] = PressureParser.ParseTorr(parts[2]).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    // Resolve time-unit suffix on the timeout so the worker receives canonical seconds.
                    parts[3] = APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[3]).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    var res = await ExecuteRecipeLine(parts, cancellationToken).ConfigureAwait(false);
                    if (res == null)
                    {
                        result = false;
                        throw new InvalidOperationException($"\nCommand {line} failed");
                    }
                    SendRecipeLog($"  pwait done");
                }
                else if (parts[0].Equals("state", StringComparison.OrdinalIgnoreCase))
                {
                    // Sets the alarm state machine state explicitly. Used by
                    // transition recipes (SYSTEM_START etc.) to declare intent
                    // that can't be inferred from valve/sensor events alone.
                    if (parts.Length < 2)
                        throw new InvalidOperationException("\nstate requires 1 argument: startup|idle|processing");
                    var newState = ConditionParser.ParseSystemState(parts[1]);
                    if (newState == SystemState.Unknown)
                        throw new InvalidOperationException($"\nstate: unknown state '{parts[1]}' (expected startup|idle|processing)");
                    SendRecipeLog($"  state {newState}");
                    core!.SystemState = newState;
                }
                else if (parts[0].Equals("swait", StringComparison.OrdinalIgnoreCase))
                {
                    // Passive sensor wait: polls the sensor until value <= target,
                    // never touches the throttle valve. Use when pumping
                    // through other paths (PP/SP roughing) where the TV setpoint
                    // is irrelevant or undesirable.
                    if (parts.Length < 4)
                        throw new InvalidOperationException($"\nswait requires 3 arguments: sensor_id target_pressure timeout_seconds");
                    string swSensor = parts[1];
                    float swTarget = PressureParser.ParseTorr(parts[2]);
                    int swTimeout = (int)APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[3]);
                    SendRecipeLog($"  swait {string.Join(' ', parts[1..])}");
                    await ExecutePressureWait(swSensor, swTarget, swTimeout, cancellationToken);
                    SendRecipeLog($"  swait done");
                }
                else if (parts[0].Equals("check", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 4)
                        throw new InvalidOperationException($"\ncheck requires 3 arguments: sensor_id condition value");
                    string sensorId = parts[1];
                    string condition = parts[2];
                    float checkValue = Interpreter.ParseNumberLiteral(parts[3]);   // suffix-aware
                    SendRecipeLog($"  check {sensorId} {condition} {checkValue}");
                    ExecuteCheck(sensorId, condition, checkValue);
                }
                else if (parts[0].Equals("message", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 2)
                        throw new InvalidOperationException("\nmessage requires at least 1 argument: message text");
                    string messageText = string.Join(" ", parts[1..]).Trim('"');
                    SendRecipeLog($"  message: {messageText}");
                    await ExecuteMessageCommand(messageText, cancellationToken);
                }
                else if (parts[0].Equals("rotation", StringComparison.OrdinalIgnoreCase) ||
                         parts[0].Equals("rotate", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 2)
                        throw new InvalidOperationException($"\nrotation requires at least 1 argument: on|off [speed]");
                    SendRecipeLog($"  rotation {string.Join(' ', parts[1..])}");
                    ExecuteRotation(parts);
                }
                else if (parts[0].Equals("warmup", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 2)
                        throw new InvalidOperationException("\nwarmup requires at least a time: warmup TIME [SRC...]");
                    float warmSeconds = APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[1]);
                    string[] warmSources = parts.Length > 2 ? parts[2..] : [];
                    SendRecipeLog($"  warmup {string.Join(' ', parts[1..])}");
                    await core!.OpWarmup(warmSeconds, warmSources, cancellationToken);
                    SendRecipeLog($"  warmup done");
                }
                else if (TryParsePumpSpinup(parts, out string spId, out float spSecs))
                {
                    SendRecipeLog($"  pump {spId} on (spin-up {spSecs}s, skip if running)");
                    if (!await ExecutePumpSpinup(spId, parts[2], spSecs, cancellationToken))
                    {
                        result = false;
                        throw new InvalidOperationException($"\nCommand {line} failed");
                    }
                }
                else
                {
                    NormalizeTimeArgs(parts);   // deposit dwell / bias delay accept 30s/5m suffixes
                    var res = await ExecuteRecipeLine(parts, cancellationToken).ConfigureAwait(false);
                    if (res == null)
                    {
                        result = false;
                        throw new InvalidOperationException($"\nCommand {line} failed");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException e)
        {
            result = false;
            core!.SendErrorMesssage(e.Message);
        }
        return result;
    }

    /// <summary>
    /// Refuses to execute a recipe whose lexer had to skip characters it could
    /// not tokenize. The lexer records them in <c>Diagnostics</c> instead of
    /// throwing, and embedded recipes run with no validator at all, so a stray
    /// character would execute silently dropped: `stage HS –283` (en-dash)
    /// becomes `stage HS 283`, reversed motion. A mutilated token stream is
    /// never safe to run.
    /// </summary>
    private static void ThrowOnLexerDiagnostics(Lexer lexer, string context)
    {
        if (lexer.Diagnostics.Count == 0)
            return;
        string details = string.Join("; ", lexer.Diagnostics.Select(d =>
            $"line {d.Line} col {d.Column}: '{d.Character}'"));
        throw new InvalidOperationException(
            $"{context} contains characters that are not part of any recipe token — " +
            $"refusing to run with them dropped ({details})");
    }

    /// <summary>
    /// Each resolved line is dispatched through the same command execution logic
    /// as the legacy path.
    /// </summary>
    internal async Task<bool> ExecuteRecipeWithInterpreter(string recipeText, Interpreter interpreter, CancellationToken cancellationToken = default)
    {
        var lexer = new Lexer();
        var tokenLines = lexer.Tokenize(recipeText);
        ThrowOnLexerDiagnostics(lexer, "recipe");

        interpreter.SensorValueProvider ??= (sensorId) =>
        {
            var sensor = core!.FindElement(DeviceType.Sensor, sensorId) as BaseSensor;
            if (sensor == null)
                throw new InvalidOperationException($"Sensor '{sensorId}' not found for 'if' condition");
            return sensor.Value();
        };

        interpreter.DeviceStateProvider ??= (deviceId) =>
        {
            var valve = core!.FindElement(DeviceType.Valve, deviceId) as BaseValve;
            if (valve != null)
                return valve.IsOpen ? "open" : "closed";
            var pump = core!.FindElement(DeviceType.Pump, deviceId) as BasePump;
            if (pump != null)
                return pump.IsRunning ? "on" : "off";
            throw new InvalidOperationException($"Device '{deviceId}' not found for 'if' condition");
        };

        interpreter.EmbeddedRecipeResolver ??= (name) =>
            core!.embRecipes != null && core.embRecipes.Contains(name)
                ? core.embRecipes.Recipe(name)
                : null;

        var blocks = interpreter.BuildBlockStructure(tokenLines);

        _recipeTotalSteps = tokenLines.Count;

        // Remaining-time plan for this run: durations the DSL declares, loop
        // bodies counted once per iteration, refined by measurement as the run goes.
        _estimatorLines = tokenLines;
        _estimator = new RecipeTimeEstimator(
            tokenLines.Select(tl => string.Join(' ', tl.Select(t => t.Value))).ToList());

        bool result = true;

        try
        {
            SendRecipeProgress(0, "Starting...", RecipeState.Running);
            result = await ExecuteTokenLines(tokenLines, blocks, 0, tokenLines.Count, interpreter, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            result = false;
            // Preserve a more specific command-level reason if one was already set.
            _lastCommandError ??= $"{e.GetType().Name}: {e.Message}".Trim();
            core!.SendErrorMesssage(e.Message);
        }

        return result;
    }

    /// <summary>Executes tokenized lines [startIdx, endIdx).</summary>
    private async Task<bool> ExecuteTokenLines(List<List<Token>> tokenLines, BlockContext blocks, int startIdx, int endIdx, Interpreter interpreter, CancellationToken cancellationToken = default)
    {
        int i = startIdx;
        while (i < endIdx)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = tokenLines[i];
            if (line.Count == 0) { i++; continue; }

            string cmd = line[0].Value.ToLowerInvariant();

            if (cmd == "begin" || cmd == "end" || cmd == "start" || cmd == "finish")
            {
                i++;
                continue;
            }

            if (cmd == "var")
            {
                interpreter.ProcessVarDeclaration(line);
                i++;
                continue;
            }

            if (cmd == "loop")
            {
                var block = blocks.GetBlock(i);
                if (block == null)
                    throw new InvalidOperationException($"Line {line[0].Line}: loop block structure not found");

                int loopCount = interpreter.GetLoopCount(line);

                // Only the outermost loop drives the multi-stack counter —
                // nested loops would otherwise overwrite the stack progress
                // mid-iteration.
                bool isOutermost = _totalStacks == 0;
                if (isOutermost) _totalStacks = loopCount;

                // `loop $stacks` resolves only here — until it does, the plan
                // plans one pass.
                if (ReferenceEquals(tokenLines, _estimatorLines))
                    _estimator?.SetLoopIterations(i, loopCount);

                APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.PushLoop(loopCount);
                try
                {
                    for (int iteration = 0; iteration < loopCount; iteration++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        interpreter.SetVariable("_i", iteration);
                        if (isOutermost) _currentStack = iteration + 1;
                        APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.SetLoopIteration(iteration + 1);
                        if (!await ExecuteTokenLines(tokenLines, blocks, block.StartLine + 1, block.EndLine, interpreter, cancellationToken))
                            return false;
                    }
                }
                finally
                {
                    APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.PopLoop();
                    if (isOutermost)
                    {
                        _currentStack = 0;
                        _totalStacks = 0;
                    }
                }

                i = block.EndLine + 1;
                continue;
            }

            if (cmd == "endloop")
            {
                i++;
                continue;
            }

            if (cmd == "if")
            {
                var block = blocks.GetBlock(i);
                if (block == null)
                    throw new InvalidOperationException($"Line {line[0].Line}: if block structure not found");

                bool conditionTrue = interpreter.EvaluateIfCondition(line);

                if (conditionTrue)
                {
                    int bodyEnd = block.ElseLine != -1 ? block.ElseLine : block.EndLine;
                    if (!await ExecuteTokenLines(tokenLines, blocks, block.StartLine + 1, bodyEnd, interpreter, cancellationToken))
                        return false;
                }
                else if (block.ElseLine != -1)
                {
                    if (!await ExecuteTokenLines(tokenLines, blocks, block.ElseLine + 1, block.EndLine, interpreter, cancellationToken))
                        return false;
                }

                i = block.EndLine + 1;
                continue;
            }

            if (cmd == "else" || cmd == "endif")
            {
                i++;
                continue;
            }

            // The child recipe shares the parent's variables and providers via the same
            // Interpreter instance, but gets its own BlockContext (so its loops/labels/functions
            // don't trample the parent's).
            if (cmd == "call")
            {
                if (line.Count < 2)
                    throw new InvalidOperationException(
                        $"Line {line[0].Line}: 'call' requires an embedded recipe name");

                string targetName = line[1].Value;
                string[]? childLines = interpreter.ResolveEmbeddedRecipe(targetName);
                if (childLines == null)
                    throw new InvalidOperationException(
                        $"Line {line[0].Line}: embedded recipe '{targetName}' not found");

                SendRecipeLog($"  call {targetName}");

                interpreter.EnterCall(line[0].Line, targetName);
                try
                {
                    var childLexer = new Lexer();
                    var childTokens = childLexer.Tokenize(string.Join('\n', childLines));
                    ThrowOnLexerDiagnostics(childLexer, $"embedded recipe '{targetName}'");
                    var childBlocks = interpreter.BuildBlockStructure(childTokens);

                    if (!await ExecuteTokenLines(childTokens, childBlocks, 0, childTokens.Count,
                                                 interpreter, cancellationToken))
                        return false;
                }
                finally
                {
                    interpreter.ExitCall();
                }

                i++;
                continue;
            }

            // Labels are registered in BuildBlockStructure; no-op at execution time.
            if (cmd == "label")
            {
                i++;
                continue;
            }

            if (cmd == "goto")
            {
                if (line.Count < 2)
                    throw new InvalidOperationException($"Line {line[0].Line}: 'goto' requires a label name");
                string labelName = line[1].Value;
                int targetLine = blocks.GetLabelLine(labelName);
                if (targetLine < 0)
                    throw new InvalidOperationException($"Line {line[0].Line}: label '{labelName}' not found");
                if (targetLine < startIdx || targetLine >= endIdx)
                    throw new InvalidOperationException($"Line {line[0].Line}: 'goto {labelName}' jumps outside current execution scope");
                i = targetLine;
                continue;
            }

            // Function bodies are registered in BuildBlockStructure; skip them here.
            if (cmd == "function")
            {
                var block = blocks.GetBlock(i);
                if (block != null)
                    i = block.EndLine + 1;
                else
                    i++;
                continue;
            }

            if (cmd == "endfunction")
            {
                i++;
                continue;
            }

            // Functions live on the BlockContext, not the Interpreter, so each 'call'
            // frame sees only its own functions.
            string cmdOriginal = line[0].Value;
            if (blocks.HasFunction(cmdOriginal))
            {
                var func = blocks.GetFunction(cmdOriginal)!;
                var resolvedArgs = interpreter.ResolveVariables(line);
                for (int p = 0; p < func.ParamNames.Count && p + 1 < resolvedArgs.Length; p++)
                {
                    interpreter.SetVariable(func.ParamNames[p], resolvedArgs[p + 1]);
                }
                // Functions share the 'call' depth budget: this dispatch recurses
                // SYNCHRONOUSLY, so a self- or mutually-recursive function would grow
                // the real stack to an uncatchable StackOverflowException.
                interpreter.EnterCall(line[0].Line, cmdOriginal);
                try
                {
                    if (!await ExecuteTokenLines(tokenLines, blocks, func.StartLine + 1, func.EndLine, interpreter, cancellationToken))
                        return false;
                }
                finally
                {
                    interpreter.ExitCall();
                }
                i++;
                continue;
            }

            string[] parts = interpreter.ResolveVariables(line);
            // open/closed/on/off substitution
            parts = ApplySubstitutions(parts);

            string resolvedLine = string.Join(" ", parts);

            if (ReferenceEquals(tokenLines, _estimatorLines))
                _estimator?.OnStepStarted(
                    i, (DateTime.Now - _recipeStartTime).TotalSeconds, resolvedLine);

            _currentStepNumber = i + 1;
            SendRecipeProgress(i + 1, resolvedLine, RecipeState.Running);

            if (!await ExecuteResolvedPartsGuarded(parts, cancellationToken))
                return false;

            i++;
        }
        return true;
    }

    private async Task<bool> ExecuteCountedParts(string[] parts, CancellationToken cancellationToken)
    {
        bool ok = await ExecuteResolvedParts(parts, cancellationToken).ConfigureAwait(false);
        if (ok && parts.Length > 0 && parts[0].Equals("deposit", StringComparison.OrdinalIgnoreCase))
            APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.RecordDepositCompleted();
        return ok;
    }

    private async Task<bool> ExecuteResolvedParts(string[] parts, CancellationToken cancellationToken = default)
    {
        if (parts.Length == 0) return true;

        cancellationToken.ThrowIfCancellationRequested();

        string cmd = parts[0];

        if (cmd.Equals("delay", StringComparison.OrdinalIgnoreCase) ||
            cmd.Equals("wait", StringComparison.OrdinalIgnoreCase))
        {
            float time = APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[1]);
            await core!.OpDelay(time, cancellationToken);
        }
        else if (cmd.Equals("pwait", StringComparison.OrdinalIgnoreCase))
        {
            parts = NormalizePwaitParts(parts);
            parts[2] = PressureParser.ParseTorr(parts[2]).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            parts[3] = APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[3]).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var res = await ExecuteRecipeLine(parts, cancellationToken).ConfigureAwait(false);
            if (res == null)
                return false;
        }
        else if (cmd.Equals("swait", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 4)
                throw new InvalidOperationException($"\nswait requires 3 arguments: sensor_id target_pressure timeout_seconds");
            string swSensor = parts[1];
            float swTarget = PressureParser.ParseTorr(parts[2]);
            int swTimeout = (int)APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[3]);
            await ExecutePressureWait(swSensor, swTarget, swTimeout, cancellationToken);
        }
        else if (cmd.Equals("state", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 2)
                throw new InvalidOperationException("\nstate requires 1 argument: startup|idle|processing");
            var newState = ConditionParser.ParseSystemState(parts[1]);
            if (newState == SystemState.Unknown)
                throw new InvalidOperationException($"\nstate: unknown state '{parts[1]}' (expected startup|idle|processing)");
            core!.SystemState = newState;
        }
        else if (cmd.Equals("check", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 4)
                throw new InvalidOperationException($"\ncheck requires 3 arguments: sensor_id condition value");
            string sensorId = parts[1];
            string condition = parts[2];
            float checkValue = Interpreter.ParseNumberLiteral(parts[3]);   // suffix-aware
            ExecuteCheck(sensorId, condition, checkValue);
        }
        else if (cmd.Equals("message", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 2)
                throw new InvalidOperationException("\nmessage requires at least 1 argument: message text");
            string messageText = string.Join(" ", parts[1..]).Trim('"');
            await ExecuteMessageCommand(messageText, cancellationToken);
        }
        else if (cmd.Equals("rotation", StringComparison.OrdinalIgnoreCase) ||
                 cmd.Equals("rotate", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 2)
                throw new InvalidOperationException($"\nrotation requires at least 1 argument: on|off [speed]");
            ExecuteRotation(parts);
        }
        else if (cmd.Equals("warmup", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 2)
                throw new InvalidOperationException("\nwarmup requires at least a time: warmup TIME [SRC...]");
            float warmSeconds = APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(parts[1]);
            string[] warmSources = parts.Length > 2 ? parts[2..] : [];
            await core!.OpWarmup(warmSeconds, warmSources, cancellationToken);
        }
        else if (TryParsePumpSpinup(parts, out string spId, out float spSecs))
        {
            if (!await ExecutePumpSpinup(spId, parts[2], spSecs, cancellationToken))
                return false;
        }
        else
        {
            NormalizeTimeArgs(parts);   // deposit dwell / bias delay accept 30s/5m suffixes
            var res = await ExecuteRecipeLine(parts, cancellationToken).ConfigureAwait(false);
            if (res == null)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Brings a 'pwait' line to the 4-part form the worker dispatch expects
    /// (<c>pwait sensor target timeout</c>), accepting both authored forms.
    ///
    /// The sensor argument is only meaningful when 'pwait' is UNBOUND, i.e.
    /// the sensor id selects which chamber's throttle valve to drive
    /// (a multi-chamber worker's Pwait -> ThrottleResolver). When 'pwait' is BOUND to a
    /// controller in Worker.xml (<c>device="..."</c>) that one
    /// controller is both the throttle actuator and the gauge — it commands
    /// SetPressure on itself and converges on its own reading — so a sensor
    /// name selects nothing. There the canonical form is the two-argument
    /// <c>pwait target timeout</c>, and the bound device's name is substituted
    /// for the log/timeout text the worker builds.
    ///
    /// A legacy three-argument line on a bound pwait keeps working; its sensor
    /// token is inert, and the validator warns the author to drop it.
    /// </summary>
    private string[] NormalizePwaitParts(string[] parts)
    {
        var op = core!.GetOperation("pwait");
        bool bound = op?.Device != null;

        if (parts.Length >= 4) return parts;

        if (!bound)
            throw new InvalidOperationException(
                "\npwait requires 3 arguments: sensor_id target_pressure timeout_seconds " +
                "(the sensor selects which chamber's throttle valve to drive)");

        if (parts.Length < 3)
            throw new InvalidOperationException(
                "\npwait requires 2 arguments: target_pressure timeout_seconds");

        // pwait <target> <timeout>  ->  pwait <controller> <target> <timeout>
        return [parts[0], op!.Device!, parts[1], parts[2]];
    }

    /// <summary>Mirrors the logic in <see cref="Core.Substitute"/>.</summary>
    internal static string[] ApplySubstitutions(string[] parts)
    {
        // Never rewrite operator-facing message text: 'please open the N2
        // valve' would reach the operator modal as 'please true the N2 valve'.
        if (parts.Length > 0 && parts[0].Equals("message", StringComparison.OrdinalIgnoreCase))
            return parts;

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i] switch
            {
                "open" => "true",
                "closed" => "false",
                "close" => "false",
                "on" => "true",
                "off" => "false",
                _ => parts[i]
            };
        }
        return parts;
    }

    private static string[] ResolvePartsWithInterpreter(string[] parts, Interpreter interpreter)
    {
        var resolved = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith('$') && parts[i].Length > 1)
            {
                string varName = parts[i][1..];
                object? val = interpreter.GetVariable(varName);
                resolved[i] = val != null ? Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture) ?? parts[i] : parts[i];
            }
            else
            {
                resolved[i] = parts[i];
            }
        }
        return resolved;
    }

    /// <summary>
    /// Expects parts: ["var", "name", "=", "value"] or ["var", "name", "value"].
    /// </summary>
    private void ExtractVariable(string[] parts, Interpreter? interpreter = null)
    {
        if (parts.Length < 3)
            throw new InvalidOperationException("'var' requires at least a name and value");

        string name = parts[1];
        int valueIdx = 2;
        if (valueIdx < parts.Length && parts[valueIdx] == "=")
            valueIdx++;

        if (valueIdx >= parts.Length)
            throw new InvalidOperationException("'var' declaration missing value");

        string rawValue = parts[valueIdx];

        if (interpreter != null)
        {
            object value;
            if (int.TryParse(rawValue, out int intVal))
                value = intVal;
            else if (float.TryParse(rawValue, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                value = floatVal;
            else if (bool.TryParse(rawValue, out bool boolVal))
                value = boolVal;
            else
                value = rawValue;

            interpreter.SetVariable(name, value);
        }
        // Without interpreter, variables are not supported (legacy behavior)
    }


    private async Task<object?> ExecuteRecipeLine(string[] parts, CancellationToken ct = default)
    {
        DeviceType target;
        string cmd = string.Join(' ', parts);

        try
        {
            SendRecipeLog($"  exec: {cmd}");
            var op = core.GetOperation(parts[0].ToLower());
            if (op != null)
            {
                Type[] fArgs = op.Args;
                target = core.FindTarget(parts[0]);
                SendRecipeLog($"  target={target}, argTypes=[{string.Join(',', fArgs.Select(t => t.Name))}]");

                object[] args = new object[fArgs.Length];
                int partsOffset = 1; // default: parts[1] maps to args[0]

                // Bound device: auto-inject as first arg, recipe parts shift by one
                if (op.Device != null)
                {
                    var device = core.FindElement(target, op.Device);
                    if (device == null)
                        throw new InvalidOperationException($"\nBound device '{op.Device}' not found");
                    args[0] = device;
                    partsOffset = 1; // parts[1] maps to args[1]
                    SendRecipeLog($"  bound device: {op.Device}");
                }

                for (int i = partsOffset; i < parts.Length; i++)
                {
                    int argIdx = op.Device != null ? i : i - 1;
                    if (argIdx < fArgs.Length)
                    {
                        var a = ConvertStringToType(parts[i], fArgs[argIdx]);
                        if (a != null)
                            args[argIdx] = a;
                        else
                        {
                            var element = core.FindElement(target, parts[i]);
                            SendRecipeLog($"  FindElement({target}, {parts[i]}) => {(element != null ? element.GetType().Name : "NULL")}");
                            args[argIdx] = element!;
                        }
                        if (args[argIdx] == null)
                        {
                            // A null here means the token is neither a value of
                            // the declared type nor a device of the target type.
                            // Pre-run validation should already have rejected the
                            // recipe; if anything still reaches here, fail loudly
                            // and say exactly what could not be resolved instead of
                            // an opaque reflection ArgumentException.
                            throw new InvalidOperationException(
                                $"\n'{parts[0]}' argument {argIdx + 1}: '{parts[i]}' is not a valid " +
                                $"{fArgs[argIdx].Name} value and is not a known {target} device");
                        }
                    }
                }
                var result = await ExecuteOperation(parts[0], args, ct).ConfigureAwait(false);
                SendRecipeLog($"  result: {result ?? "null"}");
                if (result == null)
                    _lastCommandError = $"command '{cmd}' returned no result " +
                        "(device not connected, or an operation precondition was not met)";
                string deviceLabel = parts.Length > 1 ? parts[1] : parts[0];
                core!.SendLogMesssage("Execute", ResToStr(target, deviceLabel, result));
                // Recipe lines that hit an interlock must abort the recipe —
                // a substrate-load that can't close GVL must NOT silently
                // continue. Returning null takes the existing abort path
                // (`if (res == null) return false`).
                if (result is InterlockCond ic)
                {
                    string msg = $"\nRecipe step '{cmd}' blocked by interlock on {Core.InterLockToString(ic)}";
                    // Record the reason like every other failure path: it names the cause in the
                    // top-level "Recipe failed — ..." line, and it is how the fault-pause handler
                    // recognises an interlock block (which must abort, never pause).
                    _lastCommandError = msg.Trim();
                    core!.SendLogMesssage("Interlock", msg);
                    core!.SendErrorMesssage(msg);
                    return null;
                }
                return result;
            }
            else
            {
                SendRecipeLog($"  ERROR: command '{parts[0]}' not found in worker");
                throw new InvalidOperationException($"\nCommand {parts[0]} not found");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _lastCommandError = $"{e.GetType().Name}: {e.Message}".Trim();
            SendRecipeLog($"  EXCEPTION: {e.GetType().Name}: {e.Message}");
            core!.SendErrorMesssage(e.Message);
            return null;
        }
    }

    private async Task ProcessSingleWorkerCommand(ThreadMsg msg)
    {
        if (msg.Type != MsgType.Command) return;

        // This runs fire-and-forget (HWDispatcher discards the task), so the
        // synchronous net in RunThread does NOT cover exceptions raised after
        // the first await. An operator action whose interlock/state read hits a
        // PlcCommException must surface as a visible blocked operation rather
        // than an unobserved task fault + silent no-op.
        try
        {
            await ProcessSingleWorkerCommandCore(msg);
        }
        catch (OperationCanceledException)
        {
            // Recipe/operator abort — expected, already reported by the abort path.
        }
        catch (PlcCommException ex)
        {
            core!.SendErrorMesssage($"Operation '{msg.Device}' blocked — PLC communication fault: {ex.Message}");
            core!.SendLogMesssage("Operation", $"'{msg.Device}' aborted: {ex.Message}");
        }
        catch (Exception ex)
        {
            core!.SendErrorMesssage($"Operation '{msg.Device}' failed: {ex.Message}");
            System.Diagnostics.Trace.TraceError($"ProcessSingleWorkerCommand: {ex}");
        }
    }

    private async Task ProcessSingleWorkerCommandCore(ThreadMsg msg)
    {
        switch (msg.Device)
        {
            case "Execute":
                // UI run path: ParamMsg carries [fileName, edited-parameter dict].
                if (msg is ParamMsg pm && pm.Args.Length >= 1 && pm.Args[0] is string pmFile)
                {
                    var overrides = pm.Args.Length > 1
                        ? pm.Args[1] as IReadOnlyDictionary<string, string>
                        : null;
                    _ = ExecuteRecipe(fileName: pmFile, paramOverrides: overrides);
                }
                // Remote API / legacy path: filename only, no operator overrides.
                else if (msg is ValMsg<string> valMsg)
                {
                    _ = ExecuteRecipe(fileName: valMsg.Value);
                }
                break;

            case "Isolate":
            {
                // DELIBERATELY UNGATED, and it must stay that way (Ruling P). Isolate is the
                // gas-side kill switch — the OutputOff of the valve/MFC path — and the spec
                // leans on it as THE emergency path, which is precisely why a plain valve close
                // was NOT carved out of the recipe gate in `default:` below. Do not "fix" this
                // by gating it on IsManualControlGated; an operator must always be able to shut
                // the gas off, running recipe or not.
                var valves = core.hwElements.Item.TryGetValue(DeviceType.Valve, out var valveElements)
                    ? valveElements._elements.Values.OfType<BaseValve>()
                    : Enumerable.Empty<BaseValve>();
                var mfcs = core.hwElements.Item.TryGetValue(DeviceType.MFC, out var mfcElements)
                    ? mfcElements._elements.Values.OfType<BaseMFC>()
                    : Enumerable.Empty<BaseMFC>();
                await IsolateOperation.RunAsync(valves, mfcs, core.RecipeCancellationToken).ConfigureAwait(false);
                core.SendLogMesssage("Isolate", "System isolated: all valves closed, all MFCs zeroed.");
                // The EVIDENCE half does follow: a panic button pressed during a hold closes
                // EVERY valve and zeroes EVERY MFC — strictly more than the single
                // `Valve V1 = False` the gate below refuses — so it must appear in the signed
                // record. Recorded after the fact, like every other path.
                if (core.IsHeldForOperator)
                    core.NoteHeldManualCommand("Manual", "Isolate — all valves closed, all MFCs zeroed");
                break;
            }

            default:
                if (msg is ParamMsg pMsg)
                {
                    // Direct operator action (GUI button → HW channel). Gated while a recipe
                    // runs, for the same reason PSU and jog commands are: closing the Ar valve,
                    // zeroing an MFC or stopping a pump mid-deposition corrupts the run and its
                    // evidence.
                    //
                    // ExecuteOperation has exactly two callers — the RECIPE's own line dispatch
                    // and this manual path — so gating here cannot stall a running recipe.
                    //
                    // StageStop is carved out as a motion kill switch (the OutputOff of this
                    // path). Isolate needs no carve-out: it has its own case above, outside
                    // this branch.
                    if (core.IsManualControlGated
                        && !string.Equals(pMsg.Operation, "StageStop", StringComparison.OrdinalIgnoreCase))
                    {
                        string blocked = ManualOperationLabel(pMsg);
                        core.SendErrorMesssage($"{blocked} rejected — recipe running.");
                        core.SendLogMesssage("Manual", $"{blocked} rejected — recipe running");
                        break;
                    }
                    // Honour recipe-abort via the shared cancellation token.
                    var result = await ExecuteOperation(
                        pMsg.Operation,
                        RefineMsgArgs(pMsg.Args),
                        core.RecipeCancellationToken).ConfigureAwait(false);

                    // Record AFTER the operation, never before it — and RULING Q: what decides
                    // the record is whether a WIRE WAS TOUCHED, not whether the read-back
                    // verified. Three outcomes, three answers:
                    //
                    //   InterlockCond   refused; provably never reached hardware   → omit
                    //   unknown op      nothing ran, nothing was touched           → omit
                    //   null from an op that DID run — the command went out and the read-back
                    //                   did not confirm it, so the machine state may well have
                    //                   changed                                    → record, MARKED
                    //
                    // The third is the one that has no clean answer. Recording it as an ordinary
                    // success would assert a hand action that may not have happened; omitting it
                    // would leave the record silent about a valve that may have moved. The
                    // marker is the honest third answer, and it must be unmistakable to a human
                    // reading the run record.
                    //
                    // The two null cases ARE distinguishable here: Core.GetOperation is a pure
                    // dictionary lookup (Worker's indexer returns null for a missing key), so
                    // asking it again costs nothing and has no side effects.
                    if (core.IsHeldForOperator && result is not InterlockCond)
                    {
                        if (result is not null)
                            core.NoteHeldManualCommand("Manual", ManualOperationLabel(pMsg));
                        else if (core.GetOperation(pMsg.Operation) is not null)
                            core.NoteHeldManualCommand("Manual",
                                $"{ManualOperationLabel(pMsg)} (UNVERIFIED — command sent, " +
                                "read-back did not confirm it)");
                    }

                    if (result is InterlockCond res)
                    {
                        // Robot ops (home/abort) carry only [DeviceType, id] — don't
                        // index Args[2] unconditionally.
                        string what = ManualOperationLabel(pMsg);
                        string interlock = $"\nCan't perform '{what}' due to interlock on {Core.InterLockToString(res)}";
                        core!.SendLogMesssage("Interlock", interlock);
                        core!.SendErrorMesssage(interlock);
                    }
                    else
                    {
                        string arg1 = Convert.ToString(pMsg.Args[1]);
                        core!.SendLogMesssage("Operation", ResToStr((DeviceType)pMsg.Args[0], arg1, result));
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Renders a manual worker operation for the recipe gate's rejection message and for the
    /// held-command record: "Valve GVL = False", "MFC Ar = 20", "StageHome HS".
    /// <para>Mirrors the interlock message already emitted in the same branch, including its
    /// guard: robot ops (home/abort) carry only [DeviceType, id], so Args[2] must not be
    /// indexed unconditionally. Internal purely as a test seam.</para>
    /// </summary>
    internal static string ManualOperationLabel(ParamMsg msg) =>
        msg.Args.Length > 2
            ? $"{msg.Operation} {msg.Args[1]} = {msg.Args[2]}"
            : $"{msg.Operation}{(msg.Args.Length > 1 ? $" {msg.Args[1]}" : "")}";

    // internal (not private) purely as a test seam: PumpOperationLogTests asserts the
    // rendered Operation-log line. Visible only to CoreLogicTests via InternalsVisibleTo.
    internal static string ResToStr(DeviceType target, string id, object result)
    {
        if (result == null)
            return $"{target} {id} failed";
        if (result is bool br)
            return $"{target} {id} {BoolToString(target, br)}";
        // DANGER: bra[0] assumes status[0] is the device's headline bit. That is only
        // BasePump.IsRunningFrom's DEFAULT; S7200TurboPump overrides it to
        // status[1] || status[2] because its status[0] is the ACCEL transient.
        // No registered operation returns bool[] any more (all are typeof(bool)/
        // typeof(float)), so this branch is currently unreachable from the operation
        // path. If you ever make an operation return a status array, DO NOT rely on
        // this: render it through the device's own decoder, not index 0.
        if (result is bool[] bra && bra.Length > 0)
            return $"{target} {id} {BoolToString(target, bra[0])}";
        // Stage moves: pair the OpStage "moving to ..." begin message with an
        // "arrived at ..." end message so the floating Events overlay shows a
        // clear begin/end for HS/VS moves.
        if (target == DeviceType.Stage && result is float fpos)
            return $"Stage {id}: arrived at {fpos:F1} mm";
        // Interlock-blocked operations: render a readable message instead of
        // leaking the struct's type name into the event log.
        if (result is InterlockCond ic)
            return $"{target} {id}: blocked by interlock on {Core.InterLockToString(ic)}";
        return $"{target} {id} {result}";
    }

    private static string BoolToString(DeviceType target, bool value)
    {
        switch (target)
        {
            case DeviceType.Valve:
                return value ? "open" : "closed";
            case DeviceType.Pump:
                return value ? "on" : "off";
            default:
                return Convert.ToString(value)!;
        }
    }
    
    /// <summary>
    /// Converts the GUI's leading [DeviceType, id] pair into the target element (args[0]);
    /// an operation without a target keeps its arguments, shifted down by one.
    /// </summary>
    private object[] RefineMsgArgs(object[] pMsgArgs)
    {
        object[] args = new object[pMsgArgs.Length - 1];
        
        object arg = pMsgArgs[1];
        string stringValue = arg.ToString();

        var e = core.FindElement((DeviceType)pMsgArgs[0], stringValue)!;
        if (e != null)
        {
            args[0] = e;
            for (int i = 2; i < pMsgArgs.Length; i++)
                args[i - 1] = pMsgArgs[i];
        }
        else
        {
            for (int i = 1; i < pMsgArgs.Length; i++)
                args[i - 1] = pMsgArgs[i];
        }
        return args;
    }

    private async Task<object?> ExecuteOperation(string opName, object[] fArgs, CancellationToken ct = default)
    {
        var interlock = core.CheckInterlocks(opName, fArgs);
        if (interlock != null) return interlock;

        opName = opName.ToLower();
        Operation? op = core.GetOperation(opName);
        if (op == null) return null;

        var result = await op.F!.Invoke(fArgs, ct).ConfigureAwait(false);

        if (opName == "valve" && fArgs[0] is BaseValve valve)
            core.CheckValveStateTransition(valve.Params.ID, valve.IsOpen);

        return result;
    }

    /// <summary>
    /// Pure-metadata drive commands that mutate the in-memory drive object only
    /// (no hardware traffic). These must succeed even when the drive failed to
    /// connect. Motion commands (MoveTo, Home, Scan, Stop) remain gated.
    /// </summary>
    internal static bool IsDriveMetadataCommand(string command) =>
        command is "SetOffset" or "SetReverseOffset";

    /// <summary>
    /// Drive commands an operator may not issue while a recipe owns the machine.
    /// Manual jogging moves the substrate holder, which mid-deposition would
    /// drag it under an energized magnetron. Recipe-issued motion does not pass
    /// through here, so gating this command cannot stall a running recipe.
    /// </summary>
    internal static bool IsRecipeGatedDriveCommand(string command) =>
        command is "JogTo";

    /// <summary>Steps within which a move target counts as the home (closed) coordinate.</summary>
    private const float DriveHomeToleranceSteps = 1f;

    /// <summary>
    /// True for a manual drive command that can move a shutter off its home
    /// coordinate and so is subject to the <c>drive_&lt;id&gt;_true</c> opening
    /// guard (XML id <c>Drive_&lt;id&gt;_open</c>). Stop, Home, offset edits and any
    /// move to flange 1 / step 0 close or hold the shutter and are never guarded.
    /// A missing or unparsable target is treated as opening.
    /// </summary>
    internal static bool IsDriveOpeningCommand(string command, object[] args)
    {
        switch (command)
        {
            case "Stop":
            case "Home":
            case "SetOffset":
            case "SetReverseOffset":
                return false;
            case "MoveToFlange":
                return !(args.Length >= 1 && TryToFloat(args[0], out float flange) && flange == 1f);
            case "MoveTo":
            case "JogTo":
                return !(args.Length >= 1 && TryToFloat(args[0], out float steps)
                         && MathF.Abs(steps) <= DriveHomeToleranceSteps);
            default:
                return true;
        }

        static bool TryToFloat(object value, out float result)
        {
            try { result = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture); return float.IsFinite(result); }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            { result = 0f; return false; }
        }
    }

    /// <summary>
    /// Recognizes the state-aware spin-up form "pump &lt;id&gt; on &lt;seconds&gt;".
    /// Accepts both the raw "on" and the post-substitution "true" token.
    /// Returns false for the plain on/off form, "off" with an arg, a
    /// non-numeric duration, or any non-pump command.
    /// </summary>
    internal static bool TryParsePumpSpinup(string[] parts, out string id, out float seconds)
    {
        id = "";
        seconds = 0f;
        if (parts.Length != 4) return false;
        if (!parts[0].Equals("pump", StringComparison.OrdinalIgnoreCase)) return false;
        bool isOn = parts[2].Equals("on", StringComparison.OrdinalIgnoreCase)
                 || parts[2].Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!isOn) return false;
        if (!APS.SystemControl.Core.Classes.TimeParser.TryParseSeconds(parts[3], out seconds))
            return false;
        id = parts[1];
        return true;
    }

    /// <summary>
    /// Handles "pump &lt;id&gt; on &lt;seconds&gt;": if the pump is already
    /// running, skip both the command and the spin-up wait; otherwise issue
    /// the on-command and wait the given seconds. Returns false on a hard
    /// error (pump not found / failed command) to abort the recipe.
    /// </summary>
    private async Task<bool> ExecutePumpSpinup(string id, string onToken, float seconds, CancellationToken ct)
    {
        var pump = core!.FindElement(DeviceType.Pump, id) as BasePump;
        if (pump == null)
        {
            core!.SendErrorMesssage($"\npump '{id}' not found");
            return false;
        }
        if (pump.IsRunning)
        {
            core!.SendLogMesssage("Pump", $"Pump {id} already running — skipping spin-up wait ({seconds}s)");
            return true;
        }
        var res = await ExecuteRecipeLine(["pump", id, onToken], ct).ConfigureAwait(false);
        if (res == null) return false;
        await core!.OpDelay(seconds, ct);
        return true;
    }

    /// <summary>Device-specific command handling lives in the HW library, not here.</summary>
    private void ProcessDriveCommand(ThreadMsg msg)
    {
        if (msg is ParamMsg paramMsg)
        {
            var drive = core.FindElement(DeviceType.Drive, paramMsg.Device!) as BaseStepperDrive;
            if (drive == null) return;

            string command = (string)paramMsg.Args[0];
            object[] args = paramMsg.Args.Length > 1 ? paramMsg.Args[1..] : [];

            // IsManualControlGated, not IsRecipeRunning: a HELD run has handed the machine back
            // to the operator, and jogging the holder clear of a source is one of the repairs
            // they were asked to make. An ordinary running recipe still refuses the jog.
            if (IsRecipeGatedDriveCommand(command) && core.IsManualControlGated)
            {
                core.SendLogMesssage("Drive", $"{paramMsg.Device} jog rejected — recipe running");
                return;
            }
            bool isMetadataWrite = IsDriveMetadataCommand(command);
            if (!isMetadataWrite && !drive.Connected) return;

            if (IsDriveOpeningCommand(command, args))
            {
                var blocked = core.CheckInterlocks("drive", [drive, true]);
                if (blocked != null)
                {
                    string blockedMsg =
                        $"Drive {drive.Params.ID} {command} blocked by interlock on {Core.InterLockToString(blocked.Value)}.";
                    core.SendLogMesssage("Interlock", blockedMsg);
                    core.SendErrorMesssage(blockedMsg);
                    return;
                }
            }

            drive.ProcessCommand(command, args);

            // Let through BECAUSE of the hold: the run record must show the machine was
            // hand-driven, or the run reads as an uninterrupted deposition. It lands in the
            // <stem>.run.json sidecar beside the TSV (Core.WriteRunRecord), NOT in the
            // .depo.json, and that sidecar is neither signed nor fingerprinted.
            // AFTER ProcessCommand, not before: the Connected guard above returns without
            // moving anything, and recording a jog that never reached the drive would put a
            // hand action in the signed record that did not happen.
            if (core.IsHeldForOperator && IsRecipeGatedDriveCommand(command))
                core.NoteHeldManualCommand("Drive", $"{paramMsg.Device} {command}");

            // Emit an operator-visible "Drive"-source log for offset edits so the
            // floating events log and EventLogClassifier still pick them up.
            if (isMetadataWrite && args.Length >= 1)
            {
                string field = command == "SetOffset" ? "offset" : "reverseOffset";
                core.SendLogMesssage("Drive", $"{paramMsg.Device} {field} set to {args[0]}");
            }
        }
    }

    /// <summary>
    /// Routes power supply commands from UI panels to the hardware.
    /// Supports channel-qualified command names in the form "Command:Channel"
    /// (e.g. "SetVoltage:2", "OutputOn:3") for multi-channel supplies such as
    /// a multi-channel analyser. Single-channel panels omit the colon suffix.
    /// </summary>
    private void ProcessPowerCommand(ThreadMsg msg)
    {
        if (msg is ParamMsg paramMsg)
        {
            var psu = core.FindElement(DeviceType.Power, paramMsg.Device!) as BasePowerSupply;
            if (psu == null) return;

            // Command is in Args[0] (panel sends [command, ...values]).
            // paramMsg.Operation == panel ID, NOT the command name.
            string rawCommand = (string)paramMsg.Args[0];

            // Reconnect must be reachable when the device has dropped — handle it
            // before the Connected guard.
            if (string.Equals(rawCommand, "Reconnect", StringComparison.OrdinalIgnoreCase))
            {
                psu.Reconnect();
                // Recorded for the same reason OutputOff is: "always allowed" is not the same
                // as "not an operator action". Re-establishing the link to a supply is part of
                // the hand repair the hold exists for, and the record must show it happened.
                if (core.IsHeldForOperator)
                    core.NoteHeldManualCommand("Power", $"{psu.Params.ID} {rawCommand}");
                return;
            }

            if (!psu.Connected) return;

            object[] args = paramMsg.Args.Length > 1 ? paramMsg.Args[1..] : [];

            // Parse optional channel suffix: "SetVoltage:2" → command="SetVoltage", channel=2, hasChannel=true
            // Single-channel panels omit the colon; hasChannel stays false.
            string command = rawCommand;
            int channel = 1;
            bool hasChannel = false;
            int colonIdx = rawCommand.IndexOf(':');
            if (colonIdx >= 0)
            {
                command = rawCommand[..colonIdx];
                if (int.TryParse(rawCommand[(colonIdx + 1)..], out channel))
                    hasChannel = true;
                else
                    channel = 1;
            }

            // Manual PSU commands are gated while a recipe runs — the same
            // policy the drive path (jog) and the agent path already enforce.
            // Power-OFF (and Reconnect, handled above) stay always-allowed as
            // the kill switch; anything that ENERGIZES or changes the operating
            // point mid-run silently corrupts the active deposition and its
            // erosion/evidence accounting.
            // A HELD run is exempt (IsManualControlGated, not IsRecipeRunning): the operator
            // was asked to go and fix this very supply, so SetPower / OutputOn / ClearFault
            // are the tools for the job they were handed.
            if (core.IsManualControlGated
                && !string.Equals(command, "OutputOff", StringComparison.OrdinalIgnoreCase))
            {
                core.SendErrorMesssage(
                    $"{command} {psu.Params.ID} rejected — recipe running (only power OFF is allowed during a run).");
                core.SendLogMesssage("Power", $"{psu.Params.ID} {command} rejected — recipe running");
                return;
            }
            // Set by the branches below that REFUSED the command — an interlock block or an
            // unknown command name — and by the ArgumentException catch. Nothing reached the
            // hardware in those cases, so the hold's record must not claim otherwise (see the
            // recording at the end of this method).
            bool refused = false;

            // ApplySetpoint throws on a parameter name the driver does not
            // know (so a recipe typo aborts the run instead of depositing with
            // the setpoint unapplied). This dispatcher runs on the HW thread, so
            // report such a fault to the operator rather than letting it unwind
            // the poll loop — a panel command name is a config/code error, not a
            // reason to lose hardware polling.
            try
            {
                switch (command)
                {
                case "OutputOn":
                    // Honour the same power_<id>_on interlock the recipe path checks
                    // (e.g. don't energize a sputter source at atmosphere).
                    var pwrInterlock = core.CheckInterlocks("power", [psu, true]);
                    if (pwrInterlock != null)
                    {
                        // Also publish to EventLogBus: SendErrorMesssage alone is a modal dialog
                        // with no log-bus record, so an unattended/remote (agent-driven) OutputOn
                        // denial would leave no trace in logs.tail.
                        string pwrInterlockMsg =
                            $"Power on {psu.Params.ID} blocked by interlock on {Core.InterLockToString(pwrInterlock.Value)}.";
                        core.SendLogMesssage("Interlock", pwrInterlockMsg);
                        core.SendErrorMesssage(pwrInterlockMsg);
                        refused = true;
                        break;
                    }
                    if (hasChannel)
                        psu.OutputOn(channel);
                    else
                        psu.OutputOn();
                    break;
                case "OutputOff":
                {
                    // Snapshot BEFORE the output drops — afterwards SetPower/IsOutputOn read as "off" and a
                    // resume would restore nothing. The kill switch itself is unchanged and still runs first:
                    // it is never behind a dialog, a confirmation or a gate.
                    var cut = OperatorCut.From(psu);
                    if (hasChannel)
                        psu.OutputOff(channel);
                    else
                        psu.OutputOff();
                    // Tell the RECIPE. Without this the run sits out the rest of the dwell growing nothing,
                    // books the erosion, and re-energises the supply at the next `power X on`.
                    if (core.RequestOperatorCut(cut))
                    {
                        // IsFaultPaused is true only while a decision is actively pending — i.e.
                        // a dialog is ALREADY up. A press then lands during a pause already in
                        // progress and is folded into the restoration set (Ruling K), not treated
                        // as a fresh cause.
                        //
                        // IsHandlingStepFailure covers the rest of that window, which IsFaultPaused
                        // alone misses: while RunFailureHold is still aborting drives and running
                        // FAILURE_HOLD there is no pending decision yet, but the recipe is already
                        // being paused. It is armed and restored on exactly the same path either
                        // way; only the text differs.
                        string note = core.IsFaultPaused || core.IsHandlingStepFailure
                            ? $"{psu.Params.ID} de-energised by the operator while the recipe is already " +
                              "handling a step failure — recorded for restoration"
                            : $"{psu.Params.ID} de-energised by the operator during a run — pausing the recipe";
                        core.SendLogMesssage("Power", note);
                    }
                    break;
                }
                case "SetCurrent":
                    psu.ApplySetpoint(hasChannel ? $"Current:{channel}" : "Current", Convert.ToSingle(args[0]));
                    break;
                case "SetVoltage":
                    psu.ApplySetpoint(hasChannel ? $"Voltage:{channel}" : "Voltage", Convert.ToSingle(args[0]));
                    break;
                case "SetMode":
                    psu.ApplySetpoint(hasChannel ? $"Mode:{channel}" : "Mode", Convert.ToSingle(args[0]));
                    break;
                case "SetPower":
                    psu.ApplySetpoint("Power", Convert.ToSingle(args[0]));
                    break;
                case "SetFrequency":
                    psu.ApplySetpoint("Frequency", Convert.ToSingle(args[0]));
                    break;
                case "SetDutyCycle":
                    psu.ApplySetpoint("DutyCycle", Convert.ToSingle(args[0]));
                    break;
                case "SetRampUp":
                    psu.ApplySetpoint("RampUpTime", Convert.ToSingle(args[0]));
                    break;
                case "SetRampDown":
                    psu.ApplySetpoint("RampDownTime", Convert.ToSingle(args[0]));
                    break;
                case "PulseMode":
                    psu.PulseMode = Convert.ToBoolean(args[0]);
                    break;
                case "RampEnabled":
                    psu.RampEnabled = Convert.ToBoolean(args[0]);
                    break;
                case "SpeedDetect":
                    psu.SpeedDetectMode = Convert.ToBoolean(args[0]);
                    break;
                case "ClearFault":
                    psu.ClearFault();
                    break;
                default:
                    // No silent fallthrough: a wrong command name would otherwise be
                    // accepted and discarded while the caller believed the supply was
                    // energised. Same contract as ApplySetpoint, which throws on an
                    // unknown parameter name.
                    core.SendErrorMesssage(PowerCommands.UnknownMessage(psu.Params.ID, command));
                    core.SendLogMesssage("Power", PowerCommands.UnknownMessage(psu.Params.ID, command));
                    refused = true;
                    break;
                }
            }
            catch (ArgumentException ex)
            {
                core.SendErrorMesssage(
                    $"{psu.Params.ID}: panel command '{rawCommand}' rejected — {ex.Message}");
                refused = true;
            }

            // Let through BECAUSE of the hold — recorded for the PauseEvent, which
            // Core.WriteRunRecord writes into the run's <stem>.run.json sidecar (an unsigned,
            // unfingerprinted, best-effort file; the .depo.json reaches it only indirectly,
            // through the TSV name in outcome.dataFile).
            // AFTER the switch, not before it: a command an interlock blocked, an
            // unknown command name, or one whose setpoint threw never reached the supply, and a
            // signed record that ASSERTS a hand action which did not happen is worse than one
            // that omits it. Recorded for OutputOff too (and for Reconnect, above): those are
            // part of the hand repair even though they would have been allowed anyway.
            if (core.IsHeldForOperator && !refused)
                core.NoteHeldManualCommand("Power", $"{psu.Params.ID} {rawCommand}");
        }
    }

    private async Task ExecutePressureWait(string sensorId, float targetPressure, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var sensor = core!.FindElement(DeviceType.Sensor, sensorId) as BaseSensor;
        if (sensor == null)
            throw new InvalidOperationException($"Sensor '{sensorId}' not found");

        core.SendLogMesssage("pwait", $"Waiting for {sensorId} <= {targetPressure} (timeout {timeoutSeconds}s)");

        // Stall detection: if the sensor reading never changes for N
        // consecutive polls the operator gets a warning so a stuck feed
        // doesn't hide behind the full pwait timeout.
        const int PollIntervalMs = 200;
        const int StallSamples = 25;    // 25 * 200 ms = 5 s
        var stallDetector = new Classes.SensorStallDetector(StallSamples);

        int timeoutMs = timeoutSeconds * 1000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float currentValue = sensor.Value();
            if (currentValue <= targetPressure)
            {
                core.SendLogMesssage("pwait", $"{sensorId} reached target: {currentValue} <= {targetPressure}");
                return;
            }

            var tick = stallDetector.Tick(currentValue);
            if (tick.Warning is not null)
                core.SendLogMesssage("pwait",
                    $"WARNING: {sensorId} value unchanged at {tick.Warning.Value} for {StallSamples * PollIntervalMs / 1000}s — sensor may not be responding.");
            if (tick.Recovered is not null)
                core.SendLogMesssage("pwait",
                    $"{sensorId} started updating again ({tick.Recovered.Value}).");

            core.UpdateProgress($"Waiting for {sensorId}", (float)sw.ElapsedMilliseconds / timeoutMs);
            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        float finalValue = sensor.Value();
        throw new TimeoutException($"Pressure wait timed out: {sensorId} = {finalValue}, target = {targetPressure}");
    }

    /// <summary>Throws if the condition is not met, aborting the recipe.</summary>
    private void ExecuteCheck(string sensorId, string condition, float checkValue)
    {
        var sensor = core!.FindElement(DeviceType.Sensor, sensorId) as BaseSensor;
        if (sensor == null)
            throw new InvalidOperationException($"Sensor '{sensorId}' not found");

        float currentValue = sensor.Value();
        bool passed = condition switch
        {
            "<"  => currentValue < checkValue,
            ">"  => currentValue > checkValue,
            "="  => Math.Abs(currentValue - checkValue) < float.Epsilon,
            "==" => Math.Abs(currentValue - checkValue) < float.Epsilon,
            "<=" => currentValue <= checkValue,
            ">=" => currentValue >= checkValue,
            _    => throw new InvalidOperationException($"Unknown condition '{condition}' in check command"),
        };

        core.SendLogMesssage("check", $"{sensorId} = {currentValue} {condition} {checkValue}: {(passed ? "PASS" : "FAIL")}");

        if (!passed)
            throw new InvalidOperationException($"Check failed: {sensorId} = {currentValue} is NOT {condition} {checkValue}");
    }

    /// <summary>
    /// Uses PLC-based rotation control (not stepper drive).
    /// Syntax: rotation [on|off] [speed?]
    /// </summary>
    private void ExecuteRotation(string[] parts)
    {
        float speed = 0;
        bool turnOn;

        if (parts[1].Equals("on", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            turnOn = true;
            if (parts.Length >= 3)
                speed = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (parts[1].Equals("off", StringComparison.OrdinalIgnoreCase) ||
                 parts[1].Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            turnOn = false;
        }
        else if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out speed))
        {
            // Numeric value: >0 means on at that speed, 0 means off
            turnOn = speed > 0;
        }
        else
        {
            throw new InvalidOperationException($"\nrotation: invalid argument '{parts[1]}'");
        }

        var stage = core!.FindElement(DeviceType.Stage, "rotation") as BaseStage
                 ?? core!.FindElement(DeviceType.Stage, "Rotation") as BaseStage;

        if (stage != null)
        {
            if (turnOn)
            {
                stage.Position = speed;
                core.SendLogMesssage("rotation", $"Rotation ON, speed={speed}");
            }
            else
            {
                stage.Position = 0;
                core.SendLogMesssage("rotation", "Rotation OFF");
            }
        }
        else
        {
            core.SendLogMesssage("rotation", "Rotation device not found");
        }
    }

    /// <summary>
    /// Logs the chamber base pressure (system pressure sensor) at recipe
    /// start so post-mortem comparisons across runs have a definitive
    /// baseline. Mirrors Delphi unit_DS.ExeciteScript:696. Quiet failure if
    /// no system sensor is configured — base-pressure logging is informational,
    /// not a precondition.
    /// </summary>
    private void LogBasePressure()
    {
        APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.BeginRun();
        try
        {
            // Sensor id "pm" is the System chamber pressure sensor (HWConf.xml).
            // Other configurations should add
            // their system sensor under the same id or update this lookup.
            var sensor = core?.FindElement(DeviceType.Sensor, "pm") as BaseSensor;
            if (sensor is null)
            {
                LogChamberBasePressures();
                return;
            }

            // Say so rather than reporting the 0.00E+000 a failed read leaves
            // behind — an operator reading a base pressure has to be able to
            // trust it. Value() has no Connected guard (Poll() does), so a dead
            // sensor would log its zero as a measurement.
            if (!sensor.Connected)
            {
                EmitBasePressureLine("Base pressure: unavailable (pm not connected)");
                return;
            }

            // LastValue, never Value(). This runs on the recipe-start path,
            // which is the HW message-pump thread up to the first await, and
            // Value() does a live PLC read under the same lock the bus poller
            // holds: a dead socket blocks it for seconds, and Abort — which is
            // dispatched from the same loop — cannot get through either.
            // The poller refreshes LastValue every cycle, so this is at most one
            // poll interval stale, which is the right number anyway.
            float p = sensor.LastValue;
            // The PLC pressure sensor ("pm") reports Torr (SensorEmulator and
            // real PLC write chamber pressure in Torr to the sensor DB; the
            // engine compares it directly to Torr recipe targets). The Delphi
            // "mTorr" formatting referred to the serial *controller* Sensor[1]
            // array, a different source. Use scientific notation so a ~1e-4
            // Torr base pressure is legible.
            EmitBasePressureLine($"Base pressure: {p.ToString("E2", System.Globalization.CultureInfo.InvariantCulture)} Torr");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"LogBasePressure failed: {ex.Message}");
        }
    }

    private void EmitBasePressureLine(string line)
    {
        APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.RecordBasePressureLog(line);
        core!.SendLogMesssage("recipe", line);
    }

    private void LogChamberBasePressures()
    {
        if (core is null || !core.hwElements.Item.TryGetValue(DeviceType.Sensor, out var sensors) || sensors is null)
            return;
        var candidates = new List<BasePressureSensors.Candidate>();
        foreach (var kvp in sensors)
            if (kvp.Value is BaseSensor { Connected: true } s)
                candidates.Add(new BasePressureSensors.Candidate(kvp.Key, s.Chamber, s.Params.Description));

        var primaries = BasePressureSensors.PickPrimaries(candidates, chamber =>
            core.chamberAlarms.TryGetValue(chamber, out var alarms)
                ? alarms.Rules.Select(r => r.Sensor).ToList()
                : []);
        foreach (var (chamber, sensorId) in primaries)
        {
            var sensor = (BaseSensor)sensors[sensorId]!;
            EmitBasePressureLine(BasePressureSensors.FormatLine(chamber, sensorId, sensor.LastValue));
        }
    }

    /// <summary>
    /// The recipe is paused until the operator clicks OK on the modal dialog shown
    /// on the UI thread.
    /// </summary>
    private async Task ExecuteMessageCommand(string messageText, CancellationToken cancellationToken = default)
    {
        core!.SendLogMesssage("message", $"Showing message to operator: {messageText}");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Cancel any stale pending ack (shouldn't happen — one message at a time —
        // but defends against a prior abort that didn't drain the TCS).
        var previous = core.SetPendingMessageAck(tcs);
        previous?.TrySetCanceled();
        core.SendMessageToUI(messageText);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
        await tcs.Task;
        core!.SendLogMesssage("message", "Operator acknowledged message");
    }

    /// <summary>
    /// Normalizes the time-valued arguments of resolved recipe commands from time
    /// literals (<c>30</c>, <c>30s</c>, <c>5m</c>, <c>1.5h</c>) to canonical
    /// seconds, so the downstream numeric parse succeeds. The generic operation
    /// binder float-parses its args (deposit dwell, biasArb delay) and <c>Bias</c>
    /// float-parses its own string arg (bias set Delay) — none understand unit
    /// suffixes, so a suffixed time would otherwise throw ArgumentException
    /// ("String cannot be converted to Single") / a FormatException deep in the op.
    /// Mirrors the wait/pwait time handling in the dispatch ladders. Positions:
    /// <list type="bullet">
    ///   <item><c>deposit &lt;src&gt; &lt;dwell&gt; &lt;end&gt;</c> — parts[2]</item>
    ///   <item><c>bias set &lt;ch&gt; Delay &lt;seconds&gt;</c> — parts[4]</item>
    ///   <item><c>biasArb &lt;ch&gt; &lt;v&gt; &lt;freq&gt; &lt;duty&gt; &lt;delay&gt;</c> — parts[5]</item>
    /// </list>
    /// Other commands/positions pass through unchanged; an unparseable time throws
    /// a clear <see cref="FormatException"/> instead of a cryptic binder error.
    /// </summary>
    internal static string[] NormalizeTimeArgs(string[] parts)
    {
        if (parts.Length == 0) return parts;
        string cmd = parts[0];

        if (parts.Length >= 3 && cmd.Equals("deposit", StringComparison.OrdinalIgnoreCase))
            parts[2] = NormSeconds(parts[2]);
        else if (parts.Length >= 5 && cmd.Equals("bias", StringComparison.OrdinalIgnoreCase)
                 && parts[1].Equals("set", StringComparison.OrdinalIgnoreCase)
                 && parts[3].Equals("Delay", StringComparison.OrdinalIgnoreCase))
            parts[4] = NormSeconds(parts[4]);
        else if (parts.Length >= 6 && cmd.Equals("biasArb", StringComparison.OrdinalIgnoreCase))
            parts[5] = NormSeconds(parts[5]);

        return parts;

        static string NormSeconds(string s) =>
            APS.SystemControl.Core.Classes.TimeParser.ParseSeconds(s)
                .ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static object? ConvertStringToType(string input, Type targetType)
    {
        // Recipe literals are culture-invariant (e.g. the deposit dwell "16.9").
        // Parse numerics with InvariantCulture so recipe values round-trip on a
        // comma-decimal locale (ru-RU).
        if (targetType == typeof(int))
        {
            if (int.TryParse(input, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int result))
                return result;
        }
        else if (targetType == typeof(float))
        {
            if (float.TryParse(input, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
        }
        else if (targetType == typeof(bool))
        {
            if (bool.TryParse(input, out bool result))
                return result;
        }
        else if (targetType == typeof(DateTime))
        {
            if (DateTime.TryParse(input, out DateTime result))
                return result;
        }
        else if (targetType == typeof(string))
            return input;
        return null;
    }

    private DateTime _recipeStartTime;
    private string _recipeName = "";
    private int _recipeTotalSteps;

    // Multi-stack progress — tracks the outermost active `loop` block so the
    // UI can show "Stack N of M" alongside step progress. Mirrors Delphi
    // SCH_VAL_LPROG / SCH_VAL_LTOT broadcasts. 0/0 = not currently in a loop.
    // Nested loops show the outermost stack (matches operator intuition for
    // multi-stack deposition recipes — the inner loops are usually layer
    // sub-cycles within a stack).
    private int _currentStack;
    private int _totalStacks;

    // Duration-plan estimator for the "Remaining" readout. Non-null only for a
    // tokenized run: it is built from that run's token lines and indexes them,
    // so it must be fed line indices from THAT list and no other (a `call`
    // executes a child list whose indices mean something else — the child's
    // time lands on the calling line's measurement instead, which is what we
    // want). The legacy line-based executor leaves it null and keeps the old
    // average-per-step estimate.
    private RecipeTimeEstimator? _estimator;
    private List<List<Token>>? _estimatorLines;

    private void SendRecipeProgress(int currentStep, string stepDescription, RecipeState state)
    {
        // FAILURE_HOLD runs through this same pipeline under the OUTER recipe's name, so its
        // "Starting…" and step updates would make the Process panel look like the run had
        // restarted and make recipe_status report Running on `shutter Home` — at exactly the moment the
        // operator is judging machine state. Drop them. Only Running is dropped, and only while
        // the hold is in flight: the handler's own PAUSED update is sent while an OVERRUNNING
        // hold is still going, and it must get through.
        if (_holdInFlight.Value && state == RecipeState.Running) return;

        // Guard against uninitialized/stale _recipeStartTime causing huge elapsed values
        var elapsed = (state == RecipeState.Idle || _recipeStartTime == default)
            ? TimeSpan.Zero
            : DateTime.Now - _recipeStartTime;
        if (elapsed.TotalDays > 1) elapsed = TimeSpan.Zero;

        TimeSpan? remaining = null;
        if (state == RecipeState.Running)
        {
            // Duration plan first; the average-per-step rule survives only as
            // the fallback for the legacy executor, which has no plan.
            remaining = _estimator?.Remaining;

            if (remaining is null && currentStep > 0 && _recipeTotalSteps > 0)
            {
                double avgPerStep = elapsed.TotalSeconds / currentStep;
                double remainingSecs = avgPerStep * (_recipeTotalSteps - currentStep);
                // Clamp to a sane upper bound to avoid TimeSpan.FromSeconds overflow
                if (remainingSecs >= 0 && remainingSecs < TimeSpan.MaxValue.TotalSeconds)
                    remaining = TimeSpan.FromSeconds(remainingSecs);
            }
        }

        object[] args =
        [
            _recipeName,
            currentStep,
            _recipeTotalSteps,
            stepDescription,
            (int)state,
            elapsed.TotalSeconds,
            remaining?.TotalSeconds ?? -1.0,
            _currentStack,    // 0 when not in a loop; 1-based when inside outer loop
            _totalStacks      // 0 when not in a loop
        ];
        APS.SystemControl.Core.Remote.Agent.RecipeProgressTracker.Update(
            new APS.ProcessPanel.Contracts.RecipeInfo(
                _recipeName, currentStep, _recipeTotalSteps, stepDescription, state,
                elapsed, remaining, _currentStack, _totalStacks));
        core!.WriteGUI(new ParamMsg(MsgType.Progress, DeviceType.Recipe, "RecipeProgress", args));
    }

    private void SendRecipeLog(string message)
    {
        // Suppress the detailed step-log stream when the running recipe's class
        // has logging disabled. Event-log lines (SendLogMesssage) are unaffected.
        if (core is null || !core.RecipeLoggingEnabled) return;
        core.WriteGUI(new ValMsg<string>(MsgType.Log, DeviceType.Recipe, "Execute", message));
    }

    // Pre-flight home-position check failure. Distinct from other errors so the
    // catch handler can skip FAILURE_PROTOCOL — no hardware action has occurred.
    private sealed class DrivesNotHomedException(string message) : InvalidOperationException(message);
}
