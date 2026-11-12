using System.Collections.Concurrent;
using System.Threading.Channels;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.Diagnostics;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Logging;

namespace APS.SystemControl.Core;

public enum SystemState {
    Startup,
    Idle,
    Maintenance,
    Stopped,
    Processing,
    Unknown,
}

public partial class Core(ChannelWriter<ThreadMsg> guiwriter)
{
    internal readonly Dictionary<string, Alarms> chamberAlarms = [];
    private readonly Dictionary<string, SystemState> chamberStates = [];
    private readonly object _stateLock = new();
    public bool IsUserRecipe { get; set; }

    /// <summary>Live toggle: log user (.recipe/.rcpx) runs. Seeded from RootConfig at load.</summary>
    public bool LogUserRecipes { get; set; } = true;

    /// <summary>Live toggle: log embedded (SYSTEM_*, …) runs. Seeded from RootConfig at load.</summary>
    public bool LogEmbeddedRecipes { get; set; } = false;

    internal bool RecipeLoggingEnabled =>
        APS.SystemControl.Core.Classes.RecipeLogPolicy.ShouldLog(IsUserRecipe, LogUserRecipes, LogEmbeddedRecipes);

    /// <summary>Returns <see cref="SystemState.Startup"/> for a chamber with no entry yet.</summary>
    public SystemState GetChamberState(string chamberId)
    {
        lock (_stateLock)
        {
            return chamberStates.TryGetValue(chamberId, out var s) ? s : SystemState.Startup;
        }
    }

    /// <summary>Raised after a chamber state actually changes (not on same-value writes), on the
    /// thread that changed it. Handlers must not block; a throwing handler is contained.</summary>
    public event Action<string, SystemState, SystemState>? ChamberStateChanged;

    /// <summary>Recipe run lifecycle for out-of-process observers. Phases: started, complete,
    /// failed, aborted, incomplete. Raised by CoreThread at the same points it writes the
    /// "Recipe started/complete/..." log lines.</summary>
    public event Action<RecipeLifecycleEvent>? RecipeLifecycle;

    internal void RaiseRecipeLifecycle(string phase, string recipe, string detail)
    {
        try { RecipeLifecycle?.Invoke(new RecipeLifecycleEvent(phase, recipe, detail)); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"RecipeLifecycle handler threw: {ex.Message}"); }
    }

    /// <summary>Emits a <see cref="ParamMsg"/> to the GUI thread only when the value changes.</summary>
    public void SetChamberState(string chamberId, SystemState value)
    {
        SystemState old;
        bool isUserRecipe;
        lock (_stateLock)
        {
            chamberStates.TryGetValue(chamberId, out old);
            if (old == value) return;
            chamberStates[chamberId] = value;
            isUserRecipe = IsUserRecipe;
        }
        // FrmMain reads the default chamber's state under the bare "SystemState" key.
        string key = chamberId == CoreConstants.DefaultChamberId
            ? "SystemState"
            : $"SystemState:{chamberId}";
        guiwriter.TryWrite(new ParamMsg(
            MsgType.Value, DeviceType.System, key,
            [(int)value, (int)old, isUserRecipe]));
        try { ChamberStateChanged?.Invoke(chamberId, old, value); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ChamberStateChanged handler threw: {ex.Message}"); }
    }

    // Targets the default chamber. New code uses Get/SetChamberState.
    public SystemState SystemState
    {
        get => GetChamberState(CoreConstants.DefaultChamberId);
        set => SetChamberState(CoreConstants.DefaultChamberId, value);
    }

    /// <summary>True while a deposition is in progress — a running recipe
    /// (<see cref="IsRecipeRunning"/>) or any power supply drawing watts. Keyed
    /// on the recipe flag, not the chamber SystemState (which flips to Processing
    /// on gas/pressure regulation alone). Reads only cached device state; safe
    /// to call from the UI thread.</summary>
    public bool IsDepositionActive =>
        DepositionActivity.IsActive(IsRecipeRunning, EnumeratePsuWatts());

    /// <summary>Below this, a power reading is treated as noise, not deposition.
    /// Real depositions run tens-to-hundreds of watts, so 1 W cannot mask one.</summary>
    internal const double MinDepositionWatts = 1.0;

    private IEnumerable<double> EnumeratePsuWatts()
    {
        if (hwElements.Item.TryGetValue(DeviceType.Power, out var powers) && powers is not null)
            foreach (var kvp in powers)
                // Only watts from a live, responding supply count. Without this a
                // supply that failed to connect (e.g. one returning a stale
                // reading) or an RF head idling on a few watts of noise would
                // falsely mark the system "depositing" and block the hardware
                // self-test — leaving a no-turbo bench stuck in Startup unable to
                // run diagnostics at all.
                if (kvp.Value is BasePowerSupply psu
                        && psu.Connected && psu.IsResponding && psu.Power > MinDepositionWatts)
                    yield return psu.Power;
    }

    /// <summary>
    /// True when <c>Alarms.xml</c> declared at least one explicit
    /// <c>&lt;chamber&gt;</c> but did not declare one with id
    /// <see cref="CoreConstants.DefaultChamberId"/>, and Core auto-created a
    /// fallback "Main" entry in <see cref="LoadAlarms"/>. UI code uses this
    /// to suppress the orphan "Main" tile on multi-chamber configs
    /// (e.g. Cluster_Demo: TM, LL, C1).
    /// </summary>
    public bool MainIsAutoFallback { get; private set; }

    /// <summary>
    /// Snapshot of all chamber IDs currently known to Core (includes the
    /// auto-created fallback "Main" if one exists; callers that want to hide
    /// the orphan should consult <see cref="MainIsAutoFallback"/>).
    /// </summary>
    public IReadOnlyCollection<string> ChamberIds
    {
        get
        {
            lock (_stateLock) return chamberStates.Keys.ToArray();
        }
    }

    public readonly Dictionary<string, IPLCController> plcs = [];
    public readonly HwElementCollection hwElements = new();
    public readonly APS.SystemControl.Core.Classes.ActiveAlarmRegistry ActiveAlarms = new();
    /// <summary>Offline/Local/Remote control state; operator-owned. Entry points authorize against it.</summary>
    public readonly ControlModeService ControlMode = new();
    public EmbeddedRecipes? embRecipes;

    private HwDiagnostics? _diagnostics;
    public HwDiagnostics Diagnostics => _diagnostics ??= new HwDiagnostics();
    // Concurrent because the HW watchdog may briefly run a replacement poller
    // alongside a still-draining old one (it cancels but does not await the old
    // task), so these change-gating caches can see overlapping writes. A plain
    // Dictionary can corrupt its buckets / throw under concurrent writes; the
    // worst case here is a cosmetic duplicate GUI update during that window.
    private readonly ConcurrentDictionary<string, HWStatus> lastStatus = new();
    private readonly ConcurrentDictionary<string, bool> lastValveOpen = new();

    // Change-gate for the non-valve poll Value messages (Pump/Stage/MFC/
    // Controller). The 200 ms poll re-reads identical state every cycle; re-
    // broadcasting it makes the GUI replay a full RecalculateFlow + schematic
    // repaint per device per tick, which collapses responsiveness on large
    // (cluster) configs. We forward a Value message only when the reading
    // actually changed — the same discipline already applied to valves via
    // lastValveOpen. Sensors are deliberately NOT gated (their stream feeds the
    // live trend graph); see ReadState.
    private readonly ConcurrentDictionary<string, object?> lastPolledValue = new();

    public string? ConfigDir { get; internal set; }

    // Message acknowledgment support for the 'message' recipe command.
    // Accessed from both the recipe-execution thread (installs a TCS) and
    // the UI thread (acknowledges it), so all reads/writes go through
    // Interlocked.Exchange to avoid lost acks or torn swaps.
    private TaskCompletionSource<bool>? _pendingMessageAck;

    /// <summary>Returns the previous TCS if one was still pending (caller may cancel it).</summary>
    public TaskCompletionSource<bool>? SetPendingMessageAck(TaskCompletionSource<bool> tcs)
    {
        ArgumentNullException.ThrowIfNull(tcs);
        return Interlocked.Exchange(ref _pendingMessageAck, tcs);
    }

    /// <summary>
    /// Called from the UI thread when the operator dismisses a recipe message dialog.
    /// Atomically clears and signals the TCS installed by the recipe thread.
    /// </summary>
    public void AcknowledgeMessage()
    {
        var tcs = Interlocked.Exchange(ref _pendingMessageAck, null);
        tcs?.TrySetResult(true);
    }

    public void SendMessageToUI(string messageText)
    {
        guiwriter.TryWrite(new ValMsg<string>(MsgType.Command, DeviceType.System, "Message", messageText));
    }

    // Failure protocol: run FAILURE_PROTOCOL embedded recipe on error/abort.
    // Abort runs it too: a user-stopped recipe otherwise leaves whatever was
    // energised at that moment (supplies, gas, shutters) live into the next
    // run. Profiles without a FAILURE_PROTOCOL recipe are unaffected — RunFailureProtocol skips.
    public bool FailureProtocolEnabled { get; set; } = true;
    public bool AbortProtocolEnabled { get; set; } = true;

    // Recipe cancellation support. _recipeCts is reached from several threads
    // (the HW dispatch loop via StartRecipe, an operator Abort via AbortRecipe,
    // and the token getter from fire-and-forget recipe continuations on
    // thread-pool threads). All access is serialised by _recipeCtsLock so a
    // Cancel()/Dispose() can never interleave a Token read and throw
    // ObjectDisposedException on a background thread (which would crash the
    // process instead of aborting the recipe).
    private readonly object _recipeCtsLock = new();
    private CancellationTokenSource? _recipeCts;

    public CancellationToken RecipeCancellationToken
    {
        get
        {
            lock (_recipeCtsLock)
            {
                try { return _recipeCts?.Token ?? CancellationToken.None; }
                catch (ObjectDisposedException) { return CancellationToken.None; }
            }
        }
    }

    public void StartRecipe()
    {
        lock (_recipeCtsLock)
        {
            _recipeCts?.Cancel();
            _recipeCts?.Dispose();
            _recipeCts = new CancellationTokenSource();
        }
        WorkerResetRecipeState?.Invoke();
    }

    public void AbortRecipe()
    {
        lock (_recipeCtsLock)
        {
            try { _recipeCts?.Cancel(); }
            catch (ObjectDisposedException) { /* already torn down — nothing to abort */ }
        }
    }

    // Single-flight recipe gate. Without it a second Execute would call
    // StartRecipe() — cancelling the running recipe mid-operation (a PSU mid-ramp,
    // a pump mid-spin-up) — and then run concurrently, sharing the data logger
    // and _recipeName/_recipeStartTime state. Claim the slot atomically at each
    // top-level recipe entry point and release it in that entry point's finally.
    private int _recipeActive;

    /// <summary>Atomically claims the single recipe slot. Returns true if claimed
    /// (caller MUST release via <see cref="EndRecipeSlot"/> in a finally); false
    /// if a recipe is already running.</summary>
    public bool TryBeginRecipeSlot() => Interlocked.CompareExchange(ref _recipeActive, 1, 0) == 0;

    public void EndRecipeSlot()
    {
        // A fault-pause dialog left open must not act on the NEXT run.
        CancelPendingFaultDecision();
        Interlocked.Exchange(ref _recipeActive, 0);
    }

    public bool IsRecipeRunning => Volatile.Read(ref _recipeActive) == 1;

    private DataLogger dataLogger = new("Data");
    private readonly LogConfig logConfig = new();

    /// <summary>Re-point the data logger at the configured Data root. Called by
    /// CoreThread.LoadConfig after <see cref="AppPaths.Initialize"/> has run.</summary>
    internal void ApplyConfiguredLogPaths() => dataLogger = new DataLogger(AppPaths.DataDir);

    private OverrideRegistry? overrides;

    /// <summary>Registry of currently-disabled alarm rules and interlocks.
    /// Null until LoadConfig completes.</summary>
    internal OverrideRegistry? Overrides => overrides;

    /// <summary>Directory where daily audit JSONL files are written.
    /// Set during <see cref="InitializeOverrides"/>; null before LoadConfig completes.</summary>
    internal string? AuditLogDirectory { get; private set; }

    internal Alarms Alarms => chamberAlarms[CoreConstants.DefaultChamberId];

    internal Interlocks Interlocks => interlocks;

    /// <summary>Call after LoadAlarms and LoadInterlocks.</summary>
    internal void InitializeOverrides(string overridesFilePath)
    {
        // Fan safety-override log calls to both the operator pane (GUI
        // channel) and the tamper-evident hash-chained audit file (M10).
        // The audit file lives alongside the persisted overrides in the
        // Configs dir so the recordkeeping bundle travels with the config.
        var gui = new GuiChannelOverrideLogWriter(guiwriter);
        var auditDir = Path.Combine(Path.GetDirectoryName(overridesFilePath)!, "AuditLog");
        AuditLogDirectory = auditDir;
        var fileAudit = new FileAuditLogWriter(auditDir);
        var writer = new CompositeOverrideLogWriter(gui, fileAudit);

        // Route buffered + future account auth events (login/logout/switch) into the same
        // hash-chained audit so there is one tamper-evident timeline. Login happens before
        // this writer exists, so the bus replays anything queued during startup.
        APS.SystemControl.Accounts.AccountsAuditBus.Attach(writer);

        var registry = new OverrideRegistry(writer);

        // Build the set of valid IDs so orphans can be dropped on Load.
        var validAlarmIds = CollectAlarmIds();
        var validInterlockIds = CollectInterlockIds();

        registry.Load(overridesFilePath, validAlarmIds, validInterlockIds);

        foreach (var a in chamberAlarms.Values)
            a.Overrides = registry;
        overrides = registry;
    }

    private HashSet<string> CollectAlarmIds()
    {
        var ids = new HashSet<string>();
        foreach (var chamber in chamberAlarms.Values)
            foreach (var a in chamber.Rules)
                ids.Add(OverrideIds.Alarm(chamber.ChamberId, a.Sensor, a.State, a.Condition, a.Value));
        return ids;
    }

    private HashSet<string> CollectInterlockIds()
        => new(interlocks.GetAllInterlocks().Keys);

    private sealed class GuiChannelOverrideLogWriter(ChannelWriter<ThreadMsg> writer) : IOverrideLogWriter
    {
        public void Write(string line)
            => writer.TryWrite(new ValMsg<string>(MsgType.Log, DeviceType.System, "Override", line));
    }

    public void UpdateProgress(string operation, float complete)
    {
        guiwriter.TryWrite(new ValMsg<int>(MsgType.Progress, DeviceType.System, operation, (int)Math.Round(complete * 100)));
    }

    public IBaseComponent? FindElement(DeviceType type, string id)
    {
        if (hwElements.Item.TryGetValue(type, out Elements? elements))
        {
            var element = elements[id];
            if (element != null) return element;

            // Fallback: numeric index (1-based) for recipe shorthand like "valve 10"
            if (int.TryParse(id, out int index) && index >= 1 && index <= elements.Count)
                return elements.ByIndex(index - 1);
        }
        return null;
    }

    public HashSet<string> GetKnownDeviceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in hwElements.Item)
            foreach (var element in kvp.Value)
                names.Add(element.Key);
        return names;
    }

    /// <summary>
    /// Device names grouped by DeviceType name ("Valve", "Power", "Drive", ...).
    ///
    /// <see cref="GetKnownDeviceNames"/> flattens every type into one set, so it
    /// can only answer "does this name exist anywhere" — it would accept
    /// "valve PSU-B". The pre-run validator needs the per-type split to check a
    /// device reference against the type the command actually requires.
    /// </summary>
    public Dictionary<string, IReadOnlyCollection<string>> GetDeviceNamesByKind()
    {
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in hwElements.Item)
        {
            var names = new List<string>();
            foreach (var element in kvp.Value)
                names.Add(element.Key);
            if (names.Count > 0)
                result[kvp.Key.ToString()] = names;
        }
        return result;
    }

    /// <summary>
    /// Power-supply device name to the setpoint parameter names its driver
    /// accepts (<see cref="BasePowerSupply.SetpointParameterNames"/>), for the
    /// recipe validator's `set` check. Supplies whose driver declares no list
    /// are omitted, so they stay unchecked rather than rejecting every name.
    /// </summary>
    public Dictionary<string, IReadOnlyCollection<string>> GetSetpointParameterNames()
    {
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        if (hwElements.Item.TryGetValue(DeviceType.Power, out var powers) && powers is not null)
            foreach (var kvp in powers)
                if (kvp.Value is BasePowerSupply psu && psu.SetpointParameterNames.Count > 0)
                    result[kvp.Key] = psu.SetpointParameterNames;
        return result;
    }

    /// <summary>
    /// Returns installed source names (effective handle = name attribute, else
    /// material) from Sources.xml.
    /// </summary>
    public HashSet<string> GetInstalledSourceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ConfigDir == null) return names;
        string sourcesPath = Path.Combine(ConfigDir, "Sources.xml");
        foreach (var n in APS.RecipeModel.Validation.SourcesReader.Read(sourcesPath))
            names.Add(n);
        return names;
    }

    /// <summary>
    /// Re-reads Sources.xml and refreshes all in-memory caches that don't
    /// re-read on demand: SourceManager, Worker's material→flange map. Returns
    /// the new flange → material map so the caller can rebroadcast it to the
    /// process panel on the UI thread (schematic flange labels). Mirrors
    /// Delphi unit_DS.DispatchInternalMessage DS_LOAD_SOURCES +
    /// frm_main.actSystemSources rebroadcast.
    /// </summary>
    public IReadOnlyDictionary<int, APS.ProcessPanel.Contracts.FlangeSourceInfo> ReloadSources(
        APS.SystemControl.Core.Classes.SourceManager sourceManager,
        string sourcesFile)
    {
        sourceManager.Load(sourcesFile);
        if (ConfigDir != null)
        {
            InvokeWorkerInit(ConfigDir);
        }
        var flangeMap = ConfigDir != null
            ? APS.SystemControl.Core.Classes.SourcesMapLoader.LoadFlangeSourceMap(ConfigDir)
            : new Dictionary<int, APS.ProcessPanel.Contracts.FlangeSourceInfo>();
        _psuToMagnetron = APS.SystemControl.Core.Production.PsuMagnetronMap.Build(flangeMap);
        return flangeMap;
    }

    /// <summary>Rebuilds the worker's static material→flange map from the current Sources.xml.</summary>
    private void InvokeWorkerInit(string configDir)
    {
        if (WorkerOpsType == null) return;
        var initMethod = WorkerOpsType.GetMethod("Init", [typeof(string)]);
        if (initMethod == null)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"ReloadSources: Operations.Init(string) not found on {WorkerOpsType.FullName} — material→flange map will not refresh.");
            return;
        }
        initMethod.Invoke(null, [configDir]);
    }

    public void CheckValveStateTransition(string valveId, bool isOpen)
    {
        foreach (var (chamberId, chAlarms) in chamberAlarms)
        {
            var chState = GetChamberState(chamberId);
            var newState = chAlarms.CheckValveTransition(chState, valveId, isOpen, resolveValveId =>
            {
                var v = FindElement(DeviceType.Valve, resolveValveId) as BaseValve;
                return v?.IsOpen;
            });
            if (newState.HasValue)
            {
                SendLogMesssage("State", $"Transition {chamberId}: {chState} -> {newState.Value} (valve {valveId} {(isOpen ? "open" : "closed")})");
                SetChamberState(chamberId, newState.Value);
            }
        }

        var valve = FindElement(DeviceType.Valve, valveId) as BaseValve;
        if (valve is { IsGasValve: true })
            CheckGroupTransitionAndLog($"valve {valveId} {(isOpen ? "opened" : "closed")}");
    }

    /// <summary>
    /// Evaluates group transitions for every chamber and applies any that fire.
    /// Cluster configs (no "Main" chamber) need per-chamber iteration; otherwise
    /// chambers like C1 never see their idle→processing on `group="gasvalve"`.
    /// Each chamber is evaluated against ONLY its own gas valves and MFCs
    /// (matched on <see cref="BaseValve.Chamber"/> / <see cref="BaseMFC.Chamber"/>),
    /// so one chamber's gas flow never drags another in or out of Processing.
    /// </summary>
    public void CheckGroupTransitionAndLog(string reason)
    {
        foreach (var (chamberId, chAlarms) in chamberAlarms)
        {
            var chState = GetChamberState(chamberId);
            var newState = chAlarms.CheckGroupTransition(chState,
                (kind, cond) => EvaluateGroup(chamberId, kind, cond));
            if (!newState.HasValue) continue;
            SendLogMesssage("State", $"Transition {chamberId}: {chState} -> {newState.Value} ({reason})");
            SetChamberState(chamberId, newState.Value);
        }
    }

    /// <summary>
    /// Called by CoreThread at the end of any recipe (success, failure, or abort)
    /// instead of a blanket SystemState = Idle assignment. Lets group rules
    /// keep the state at Processing if gas is still flowing.
    /// </summary>
    public void OnRecipeEnd()
    {
        var stateBefore = SystemState;
        CheckGroupTransitionAndLog("recipe end");
        // Only fall back to Idle if we were Processing before AND still are now —
        // avoids overwriting a concurrent state transition (e.g. sensor→Startup).
        if (stateBefore == SystemState.Processing && SystemState == SystemState.Processing)
            SystemState = SystemState.Idle;
    }

    private bool EvaluateGroup(string chamberId, GroupKind kind, GroupCondition cond)
    {
        IEnumerable<bool> memberIsActive;

        switch (kind)
        {
            case GroupKind.GasValve:
                memberIsActive = GasValveActiveFlags(chamberId);
                break;
            case GroupKind.Gas:
                memberIsActive = GasValveActiveFlags(chamberId).Concat(MfcActiveFlags(chamberId));
                break;
            default:
                return false;
        }

        return cond switch
        {
            GroupCondition.AnyOpen => memberIsActive.Any(x => x),
            GroupCondition.AllOff  => memberIsActive.All(x => !x),
            _ => false,
        };
    }

    private IEnumerable<bool> GasValveActiveFlags(string chamberId)
    {
        if (!hwElements.Item.TryGetValue(DeviceType.Valve, out var valves) || valves is null)
            yield break;
        foreach (var kvp in valves)
            if (kvp.Value is BaseValve { IsGasValve: true } gv && gv.Chamber == chamberId)
                yield return gv.LastOpen;
    }

    /// <summary>Actual flow (sccm) above which an MFC counts as "flowing" for the
    /// gas-group transition, independent of its commanded setpoint. Tunable.</summary>
    private const float MfcFlowActiveSccm = 0.5f;

    private IEnumerable<bool> MfcActiveFlags(string chamberId)
    {
        if (!hwElements.Item.TryGetValue(DeviceType.MFC, out var mfcs) || mfcs is null)
            yield break;
        foreach (var kvp in mfcs)
            if (kvp.Value is BaseMFC m && m.Chamber == chamberId)
                // Commanded (Setpoint) OR actually flowing (LastFlow). Setpoint is
                // write-side only and is lost on a crash-restart, so fall back to the
                // poll-fed actual flow — otherwise a restart with gas physically
                // flowing reads Setpoint=0 → false "gas allOff" → processing→idle.
                yield return m.Setpoint != 0f || m.LastFlow > MfcFlowActiveSccm;
    }

    // Last HW poll tick timestamp — written at the end of every HwStateReader
    // cycle, observed by the watchdog. UTC ticks are monotonic enough for this
    // and avoid DST/wall-clock jumps.
    private long _lastHwPollUtcTicks;

    // Erosion accrual engine — sampled from the HW poll loop with each source's
    // actual power so mounted sputter targets accrue usage. Null when production
    // tracking is disabled (Standalone / no DB). Sample() is the only engine call
    // allowed on the poll thread; Flush()/Reconcile() run on the UI thread.
    public APS.SystemControl.Core.Production.IPowerAccrualSink? PowerAccrual { get; set; }

    // PSU-id -> magnetron-instance-id, rebuilt whenever Sources.xml (re)loads. Volatile so the
    // poll thread sees the latest after an operator edits sources. Ambiguous shared PSUs are
    // absent (see PsuMagnetronMap), so their power simply isn't attributed.
    private volatile IReadOnlyDictionary<string, string> _psuToMagnetron =
        new Dictionary<string, string>();

    public void SetPowerToMagnetronMap(IReadOnlyDictionary<string, string> map) => _psuToMagnetron = map;

    // Re-spawned by the watchdog if it ever completes.
    private volatile Task? _hwPollerTask;

    // Cancellation source for the CURRENT poller. The watchdog cancels this
    // before spawning a replacement so a stalled poller stops looping instead
    // of running forever alongside the new one and racing the shared status
    // dictionaries (a "stale" verdict otherwise produced two live pollers).
    private volatile CancellationTokenSource? _hwPollerCts;

    private readonly List<APS.SystemControl.Core.Threads.BusPoller> _busPollers = new();
    private readonly List<Task> _busPollerTasks = new();
    // Per-bus cancellation (linked to _hwPollerCts) so the watchdog can cancel a
    // wedged poller BEFORE respawning it, and per-bus respawn-pending flags so a
    // still-wedged bus is not respawned again every 30 s — that would pile a
    // new LongRunning thread behind the same port lock each round, and ALL of
    // them would run forever once the wedge broke.
    // Both lists are touched only by StartHwStateReader and the single watchdog task.
    private readonly List<CancellationTokenSource> _busPollerCts = new();
    private readonly List<bool> _busRespawnPending = new();

    /// <summary>Never respawn while a previous respawn's predecessor is
    /// still wedged (its token is already cancelled; it exits when the blocking
    /// read returns) — piling further pollers behind the same port lock only
    /// multiplies the post-recovery poll rate.</summary>
    internal static bool ShouldRespawnBus(
        bool busDead, bool busStale, bool respawnPending, bool oldTaskCompleted)
        => (busDead || busStale) && !(respawnPending && !oldTaskCompleted);

    // Poll period for single-device serial buses (gauges/PSUs). Matches the 200 ms
    // rate at which HwStateReader/UI consume LastMeasurement, so faster polling
    // would only flood the port. See StartHwStateReader / BusPoller.minCyclePeriodMs.
    private const int SerialBusPollIntervalMs = 200;

    // Cancelled on shutdown to stop the watchdog loop so it exits instead of
    // respawning the poller while the app is tearing down.
    private readonly CancellationTokenSource _hwShutdownCts = new();

    /// <summary>
    /// Stops the HW poller and its watchdog: cancels the watchdog (so it will
    /// not respawn a replacement poller) and the current poller token. Called
    /// from CoreThread.Stop on application exit. Safe to call more than once.
    /// </summary>
    public void StopHwStateReader()
    {
        try { _hwShutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }

        var cts = _hwPollerCts;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Partitions all hardware into independent buses: one per distinct PLC
    /// controller instance (its host=main devices) and one per serial device
    /// (its own port). Drives are excluded — they keep DriveStateReader.
    /// </summary>
    internal List<(string name, IReadOnlyList<IBaseComponent> devices, bool isPlc)> EnumerateBuses()
    {
        var plcGroups = new Dictionary<IPLCController, List<IBaseComponent>>();
        var serial = new List<(string, IReadOnlyList<IBaseComponent>, bool)>();

        foreach (var group in hwElements.Item)
        {
            if (group.Key == DeviceType.Drive) continue;           // DriveStateReader owns drives
            // Single-reader rule: during a transfer the WORKER
            // owns the robot's PLC reads, and while idle the coordinator's Robot
            // case polls it. A dedicated 200 ms BusPoller would be a third concurrent
            // reader that could overwrite a fresh worker snapshot with a stale
            // pre-latch one, making the worker see premature Idle mid-motion.
            if (group.Key == DeviceType.Robot) continue;
            foreach (var kv in group.Value)
            {
                var dev = kv.Value;
                if (dev is IBasePLCComponent plcDev)
                {
                    if (!plcGroups.TryGetValue(plcDev.PLC, out var list))
                        plcGroups[plcDev.PLC] = list = new List<IBaseComponent>();
                    list.Add(dev);
                }
                else
                {
                    serial.Add(($"Serial:{dev.Params.ID}", new[] { dev }, false));
                }
            }
        }

        var buses = new List<(string, IReadOnlyList<IBaseComponent>, bool)>();
        int i = 0;
        foreach (var g in plcGroups.Values)
            buses.Add(($"PLC:{i++}", g, true));
        buses.AddRange(serial);
        return buses;
    }

    /// <summary>
    /// Reads every device once to populate its LastX cache before the coordinator's
    /// first cycle. The coordinator reads caches, not the wire; without seeding it
    /// would read cold defaults on the startup race — a pm of 0 Torr fires a spurious
    /// startup->idle transition (and a false "pressure too high" alarm), and an empty
    /// pump status crashes the turbo widget. Drives are excluded
    /// (DriveStateReader owns them). Runs on the CoreThread; each read is
    /// <see cref="Classes.PollWatchdog"/>-bounded so a slow/dead device cannot stall
    /// startup. Disconnected devices are a no-op (Poll guards on Connected).
    /// </summary>
    internal void SeedDeviceCaches()
    {
        foreach (var group in hwElements.Item)
        {
            if (group.Key == DeviceType.Drive) continue;
            foreach (var kv in group.Value)
                Classes.PollWatchdog.RunWithTimeout($"seed:{kv.Key}", kv.Value.Poll);
        }
    }

    public void StartHwStateReader()
    {
        SeedDeviceCaches();

        _hwPollerCts = new CancellationTokenSource();

        _busPollers.Clear();
        _busPollerTasks.Clear();
        _busPollerCts.Clear();
        _busRespawnPending.Clear();
        foreach (var (name, devices, isPlc) in EnumerateBuses())
        {
            Action? prePass = null;
            if (isPlc)
            {
                var busValves = new Elements();
                foreach (var d in devices.Where(d => d.Params.Type == DeviceType.Valve))
                    busValves.Add(d.Params.ID, d);
                if (busValves.Count > 0)
                    prePass = () => PollValvesBulk(busValves);
            }
            var nonValve = isPlc
                ? devices.Where(d => d.Params.Type != DeviceType.Valve).ToList()
                : (IReadOnlyList<IBaseComponent>)devices;
            // A serial bus is a single device on its own port; one short R5/R6
            // round-trip + the 1 ms floor would poll it hundreds of times a second.
            // The coordinator/UI only sample LastMeasurement every 200 ms, so pace
            // serial buses to that rate. PLC buses read many devices per cycle and
            // self-pace, so leave them at 0.
            int minCyclePeriodMs = isPlc ? 0 : SerialBusPollIntervalMs;
            var poller = new APS.SystemControl.Core.Threads.BusPoller(name, nonValve, prePass, minCyclePeriodMs);
            _busPollers.Add(poller);
            var busCts = CancellationTokenSource.CreateLinkedTokenSource(_hwPollerCts.Token);
            _busPollerCts.Add(busCts);
            _busRespawnPending.Add(false);
            _busPollerTasks.Add(poller.Run(busCts.Token));
        }

        _hwPollerTask = HwStateReader(_hwPollerCts.Token);

        var __ = DriveStateReader().ContinueWith(task =>
        {
            if (task.Exception != null)
            {
                ValueTask valueTask = guiwriter.WriteAsync(new ValMsg<string>(MsgType.Error, DeviceType.System, "Drive Thread", task.Exception.ToString()));
            }
        }, TaskContinuationOptions.OnlyOnFaulted);

        StartHwWatchdog();
    }

    /// <summary>
    /// Background watchdog that checks every 30 s whether the HW poller is
    /// still ticking. Respawns it on stall (60 s without a tick) or task
    /// completion. The Delphi equivalent freed the entire DepositionSystem
    /// and re-init'd hardware — we only respawn the poller because PLC /
    /// serial connections survive the bad cycle.
    /// </summary>
    private void StartHwWatchdog()
    {
        const int WatchdogIntervalMs = 30_000;
        const int StallThresholdMs   = 60_000;
        var shutdownToken = _hwShutdownCts.Token;

        _ = Task.Factory.StartNew(async () =>
        {
            while (!shutdownToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(WatchdogIntervalMs, shutdownToken);

                    long lastTicks = Interlocked.Read(ref _lastHwPollUtcTicks);
                    bool taskDead  = _hwPollerTask?.IsCompleted == true;
                    bool stale     = lastTicks != 0 &&
                        (DateTime.UtcNow.Ticks - lastTicks) / TimeSpan.TicksPerMillisecond
                            > StallThresholdMs;

                    if (taskDead || stale)
                    {
                        SendLogMesssage("Watchdog",
                            taskDead
                                ? "HW poller task ended unexpectedly — respawning"
                                : "HW poller stalled (>60s without a tick) — respawning");

                        // Cancel the old poller FIRST so it stops looping (a
                        // stalled poller stuck in a read exits when that read
                        // returns) instead of running concurrently with the new
                        // one and racing the shared status dictionaries.
                        var oldCts = _hwPollerCts;
                        try { oldCts?.Cancel(); oldCts?.Dispose(); }
                        catch (ObjectDisposedException) { }

                        // Reset the tick stamp so the next watchdog round
                        // judges from the new task's first cycle.
                        Interlocked.Exchange(ref _lastHwPollUtcTicks, 0);
                        _hwPollerCts  = new CancellationTokenSource();
                        // Re-warm caches before the respawned coordinator reads them,
                        // exactly as StartHwStateReader does — otherwise the reconnect
                        // race re-opens the cold-cache window (spurious transitions /
                        // false alarms while gas may be flowing).
                        SeedDeviceCaches();
                        _hwPollerTask = HwStateReader(_hwPollerCts.Token);
                        for (int b = 0; b < _busPollers.Count; b++)
                        {
                            // Re-link each bus to the fresh master token (the old
                            // linked CTS died with the cancelled master).
                            try { _busPollerCts[b].Cancel(); _busPollerCts[b].Dispose(); }
                            catch (ObjectDisposedException) { }
                            _busPollerCts[b] = CancellationTokenSource.CreateLinkedTokenSource(_hwPollerCts.Token);
                            _busRespawnPending[b] = false;
                            _busPollerTasks[b] = _busPollers[b].Run(_busPollerCts[b].Token);
                        }
                    }

                    // Per-bus liveness: respawn only the stalled bus, leave others.
                    for (int b = 0; b < _busPollers.Count; b++)
                    {
                        long busTick = _busPollers[b].LastTickUtc;
                        bool busDead  = _busPollerTasks[b].IsCompleted;
                        bool busStale = busTick != 0 &&
                            (DateTime.UtcNow.Ticks - busTick) / TimeSpan.TicksPerMillisecond > StallThresholdMs;

                        if (!busDead && !busStale)
                        {
                            _busRespawnPending[b] = false;   // healthy again — arm future respawns
                            continue;
                        }
                        if (_hwPollerCts is not { IsCancellationRequested: false })
                            continue;
                        if (!ShouldRespawnBus(busDead, busStale, _busRespawnPending[b],
                                              _busPollerTasks[b].IsCompleted))
                            continue;   // predecessor still wedged; its token is cancelled — don't pile

                        SendLogMesssage("Watchdog", $"Bus '{_busPollers[b].Name}' stalled — respawning");
                        // Cancel the OLD poller first so it exits when its blocking
                        // read returns instead of running forever alongside the new one.
                        try { _busPollerCts[b].Cancel(); _busPollerCts[b].Dispose(); }
                        catch (ObjectDisposedException) { }
                        _busPollerCts[b] = CancellationTokenSource.CreateLinkedTokenSource(_hwPollerCts.Token);
                        _busRespawnPending[b] = true;
                        _busPollerTasks[b] = _busPollers[b].Run(_busPollerCts[b].Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Watchdog itself must never crash silently.
                    System.Diagnostics.Trace.TraceWarning(
                        $"HW watchdog loop error: {ex.Message}");
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Dedicated high-frequency polling loop for stepper drives.
    /// Runs at 50ms so the GUI has enough samples for smooth sweep animation.
    /// This is the ONLY caller of drive.ReadPosition; recipe wait loops just
    /// observe IsMoving without touching the serial port.
    /// </summary>
    public Task DriveStateReader()
    {
        // Observe the shutdown token so the 50 ms loop stops on
        // StopHwStateReader (app exit) instead of surviving CoreThread.Stop and
        // hammering disposed/closed serial ports during teardown.
        var token = _hwShutdownCts.Token;
        return Task.Factory.StartNew(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (hwElements.Item.TryGetValue(DeviceType.Drive, out var drives) && drives != null)
                    {
                        foreach (var item in drives)
                        {
                            if (!item.Value.Connected) continue;
                            var drive = (item.Value as BaseStepperDrive)!;
                            try
                            {
                                drive.ReadPosition();
                                guiwriter.TryWrite(new ParamMsg(
                                    MsgType.Value, DeviceType.Drive, item.Key,
                                    [drive.CurrentPosition, drive.IsMoving, drive.IsHomed, drive.UserRegister]));
                            }
                            catch (Exception driveEx)
                            {
                                // One transient error on one drive must not stop
                                // the 50 ms loop for the rest.
                                System.Diagnostics.Trace.TraceWarning(
                                    $"DriveStateReader[{item.Key}]: {driveEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(
                        $"DriveStateReader loop error: {ex.Message}");
                }
                try { await Task.Delay(50, token); }
                catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }


    /// <summary>
    /// Power-supply ids assigned to a mounted source in Sources.xml, frozen at the
    /// start of the current run. Sputter PSUs are logged only if they appear here
    /// (bias analyzers are always logged); mirrors the .depo.json snapshot's source
    /// set instead of guessing from the port-open Connected flag.
    /// </summary>
    private IReadOnlyCollection<string> _logSourcePsuIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> ComputeSourcePsuIds()
    {
        var map = ConfigDir != null
            ? APS.SystemControl.Core.Classes.SourcesMapLoader.LoadFlangeSourceMap(ConfigDir)
            : new Dictionary<int, APS.ProcessPanel.Contracts.FlangeSourceInfo>();
        return map.Values
            .Select(f => f.PowerSupplyId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void StartDataLogging(string recipeName)
    {
        _ = LoggingProfile;
        _logSourcePsuIds = ComputeSourcePsuIds();
        var columns = logConfig.BuildColumns(hwElements, _logSourcePsuIds);
        dataLogger.Start(recipeName, columns);
    }

    // Accumulates the run's Outcome.Means + base pressure from the TSV row values
    // (fed in LogDataRow). Reset at run start, read at finalize.
    private readonly APS.SystemControl.Core.Evidence.RunActualsAccumulator _runActuals = new();

    // Built lazily from the DataLogger's Data root so Create() writes into the same tree.
    private APS.SystemControl.Core.Evidence.DepositionRecordService? _records;
    private APS.SystemControl.Core.Evidence.DepositionRecordService Records =>
        _records ??= new APS.SystemControl.Core.Evidence.DepositionRecordService(AppPaths.DataDir);

    /// <summary>
    /// Freeze + fingerprint + write the deposition snapshot at run start, and re-route this
    /// run's TSV log into the same dated folder. Returns the .depo.json path, or null on failure
    /// (a record failure never blocks a deposition — StoreEvidence parity).
    /// </summary>
    public string? CreateDepositionSnapshot(
        APS.SystemControl.Core.Evidence.DepositionConditions conditions, string? basedOn = null)
    {
        try
        {
            var (_, path) = Records.Create(conditions, basedOn);
            // Start this run's Means accumulation fresh. Pressure channel is the TSV's
            // pm key/unit (S_pm, Torr) so BasePressureOrNull tracks the same samples.
            _runActuals.Reset();
            _runActuals.ConfigurePressure("S_pm", "Torr");
            dataLogger.NextRunOutputDirectory = Path.GetDirectoryName(path);
            // Stale-run guard: if this run's logging is suppressed, Start() never runs
            // and finalize must not stamp the PREVIOUS run's TSV into this snapshot.
            dataLogger.ClearLastFilePath();
            SendLogMesssage("Core", $"DepositionSnapshot: {path}");
            return path;
        }
        catch (Exception ex)
        {
            SendLogMesssage("Core", $"DepositionSnapshot failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Append the unsigned outcome to a snapshot at run end, stamping the
    /// run's TSV file name (null when logging was off). Best-effort.</summary>
    public void FinalizeDepositionSnapshot(string path, APS.SystemControl.Core.Evidence.DepositionOutcome outcome)
    {
        try
        {
            outcome.DataFile = Path.GetFileName(dataLogger.LastFilePath); // GetFileName(null) → null
            // Name the .run.json explicitly (snapshot schema 2). Derived, not remembered: this
            // runs on the UI thread off the terminal progress message, while WriteRunRecord runs
            // in the recipe thread's finally, and the two are not ordered. Both resolve the same
            // path from the same two inputs — the TSV when there is one, else the run-start
            // fallback below — so the reference is right whichever lands first.
            outcome.RunRecord = Path.GetFileName(RunRecordPath(dataLogger.LastFilePath));
            // Means + base pressure mirror the TSV columns (accumulated in LogDataRow).
            // Empty when recipe logging was off (no TSV rows → no means), which is correct.
            outcome.Means = _runActuals.Means();
            outcome.BasePressure = _runActuals.BasePressureOrNull();
            Records.Finalize(path, outcome);
        }
        catch (Exception ex) { SendLogMesssage("Core", $"DepositionSnapshot finalize failed: {ex.Message}"); }
    }

    /// <summary>
    /// Stops data recording. Returns the TSV that was being written for this run, or null
    /// when the run was not logged — the caller passes it to <see cref="WriteRunRecord"/>.
    /// </summary>
    public string? StopDataLogging() => dataLogger.Stop();

    /// <summary>The TSV of the run being recorded right now, or null. Lets run discovery
    /// (<c>runs.list</c>) name the live run by its file instead of guessing from "newest TSV
    /// while a recipe holds the slot" — embedded recipes hold the slot without a TSV.</summary>
    public string? ActiveDataLogPath => dataLogger.ActiveFilePath;

    /// <summary>
    /// Where this run's <c>.run.json</c> goes when recipe logging is OFF and there is no TSV for
    /// it to be a sibling of. Fixed at run START (<see cref="BeginRunRecord"/>) so it does not
    /// depend on run-end ordering; null for an embedded run, and null before the first run.
    /// </summary>
    private string? _unloggedRunRecordPath;

    /// <summary>
    /// Recipe thread, at run start: settles where this run's <c>.run.json</c> will go if recipe
    /// logging turns out to be off, and clears the previous run's answer — the same staleness
    /// guard <c>DataLogger.ClearLastFilePath</c> keeps for the TSV.
    /// <para>Embedded runs get NULL (no TSV, no record): they cannot
    /// pause, cannot be held and cannot be hand-driven (only a user recipe reaches
    /// <c>ExecuteWithFaultPause</c>), so an unlogged embedded run has nothing to lose — while
    /// SYSTEM_* / LOAD_* sequences run many times an hour and would otherwise litter the operator's
    /// dated data folders with empty sidecars.</para>
    /// </summary>
    public void BeginRunRecord(string recipeName, DateTime startedAt, bool embedded)
    {
        // Never throws into the run: a misconfigured DataDir must not stop a recipe starting.
        try { _unloggedRunRecordPath = embedded ? null : UnloggedRunRecordPath(recipeName, startedAt); }
        catch (Exception ex)
        {
            _unloggedRunRecordPath = null;
            SendLogMesssage("Core", $"Run record path failed: {ex.Message}");
        }
    }

    /// <summary>Where a run with no TSV puts its record: the same dated run folder the TSV and
    /// the <c>.depo.json</c> use, under the same stem the DataLogger would have given the TSV —
    /// so it lands beside its snapshot and <c>RunId</c> keeps the shape
    /// <c>RunArtifacts.TryParseStem</c> parses. Pure, so <see cref="BeginRunRecord"/> and
    /// <see cref="WriteRunRecord"/> cannot disagree and neither depends on the other having
    /// run.</summary>
    private static string UnloggedRunRecordPath(string recipeName, DateTime startedAt)
    {
        string dir = APS.SystemControl.Core.Evidence.DepositionRecordService.DatedDir(AppPaths.DataDir, startedAt);
        string stem = startedAt.ToString(DataLogger.StampFormat, System.Globalization.CultureInfo.InvariantCulture)
                    + "_" + DataLogger.SanitizeFileName(recipeName);
        return APS.SystemControl.Core.Logging.RunRecord.PathIn(dir, stem);
    }

    /// <summary>This run's <c>.run.json</c> path as the SNAPSHOT sees it: beside the TSV when
    /// there is one, else what <see cref="BeginRunRecord"/> settled at run start. Null only for a
    /// run that writes no record (an embedded run with logging off).</summary>
    private string? RunRecordPath(string? tsvPath) =>
        tsvPath is not null
            ? APS.SystemControl.Core.Logging.RunRecord.PathFor(tsvPath)
            : _unloggedRunRecordPath;

    /// <summary>
    /// Writes the <c>&lt;stem&gt;.run.json</c> sidecar at recipe end: recipe name and source
    /// file, start/end, outcome and terminal message, plus every fault pause and everything the
    /// operator hand-drove during a hold. Best-effort — a sidecar failure never changes the
    /// run's result; it is logged.
    /// <para>Written whether or not recipe logging is on, so pause events and hand-driven
    /// commands are never lost for a run with logging off. With no TSV to be a sibling of, the
    /// record goes where <see cref="BeginRunRecord"/> settled at run start.</para>
    /// </summary>
    public void WriteRunRecord(string? tsvPath, string recipeName, string? recipePath, bool embedded,
        DateTime startedAt, string outcome, string? message,
        IReadOnlyList<APS.SystemControl.Core.Logging.PauseEvent>? pauses = null,
        IReadOnlyList<string>? skippedSteps = null)
    {
        // Resolved from this call's OWN arguments, not from BeginRunRecord: a record must never
        // go missing because a new entry point forgot to announce itself at run start.
        // BeginRunRecord exists so the .depo.json can NAME this file without racing it.
        if (tsvPath is null && embedded) return;              // nothing to lose: see BeginRunRecord
        try
        {
            string path = tsvPath is not null
                ? APS.SystemControl.Core.Logging.RunRecord.PathFor(tsvPath)
                : UnloggedRunRecordPath(recipeName, startedAt);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            APS.SystemControl.Core.Logging.RunRecord.WriteAt(path, new APS.SystemControl.Core.Logging.RunRecord
            {
                // The stem, not the file name: PathFor/PathIn both append a two-part
                // ".run.json", so GetFileNameWithoutExtension of the record itself would leave a
                // trailing ".run" on the run id every reader parses.
                RunId = Path.GetFileName(path[..^APS.SystemControl.Core.Logging.RunRecord.Suffix.Length]),
                Recipe = recipeName,
                RecipePath = recipePath,
                Embedded = embedded,
                StartedAt = startedAt,
                EndedAt = DateTime.Now,
                Outcome = outcome,
                Message = message,
                Pauses = pauses is null ? [] : [.. pauses],
                SkippedSteps = skippedSteps is null ? [] : [.. skippedSteps],
            });
        }
        catch (Exception ex) { SendLogMesssage("Core", $"Run record failed: {ex.Message}"); }
    }

    internal void LogDataRow()
    {
        if (dataLogger.IsRecording)
        {
            var values = logConfig.CollectValues(hwElements, _logSourcePsuIds);
            dataLogger.LogRow(values);
            // Accumulate the snapshot's Outcome.Means from the SAME per-row value set
            // the TSV writes, so the .depo.json summary mirrors the TSV columns exactly
            // (PSU V/I, per-channel bias, sanitized MFCs) instead of drifting on a
            // separate telemetry feed. Thread-safe; runs on the poll thread.
            // An over/under-range or invalid reading is a status in the TSV, not a number to average.
            foreach (var kv in values)
                if (double.IsFinite(kv.Value)) _runActuals.Sample(kv.Key, kv.Value);
        }
        ManualLogTick();
    }

    /// <summary>
    /// Polling loop that reads all hardware status every 200 ms until
    /// <paramref name="ct"/> is cancelled (by the watchdog when it respawns the
    /// poller). Honouring the token ensures a respawn replaces the poller rather
    /// than running two concurrently.
    /// </summary>
    public Task HwStateReader(CancellationToken ct)
    {
        return Task.Factory.StartNew(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    foreach (KeyValuePair<DeviceType, Elements> item in hwElements.Item)
                    {
                        try { ReadState(item.Value, item.Key); }
                        catch (Exception readEx)
                        {
                            // Isolate per-device-type failures so one hung or
                            // broken group doesn't kill the whole 200 ms loop.
                            System.Diagnostics.Trace.TraceWarning(
                                $"HwStateReader[{item.Key}]: {readEx.Message}");
                        }
                    }
                    LogDataRow();

                    // Re-evaluate group transitions every cycle so the state
                    // converges on startup (gas valves may already be open when
                    // the software connects to a running system).
                    CheckGroupTransitionAndLog("poll");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(
                        $"HwStateReader loop error: {ex.Message}");
                }
                // Accrue target erosion from actual source power this cycle.
                // Read the PREVIOUS cycle's stamp before it is overwritten below
                // so dt measures the real gap. Sample() only touches in-memory
                // _gate-guarded state, so it is the one engine call safe here on
                // the poll thread (Flush/Reconcile stay on the UI thread).
                if (PowerAccrual is not null
                    && hwElements.Item.TryGetValue(DeviceType.Power, out var powers) && powers is not null)
                {
                    long prevTicks = Interlocked.Read(ref _lastHwPollUtcTicks);
                    double dt = prevTicks == 0 ? 0.2
                        : Math.Max(0, (DateTime.UtcNow.Ticks - prevTicks) / (double)TimeSpan.TicksPerSecond);
                    var psuToMag = _psuToMagnetron;   // snapshot the volatile reference once
                    foreach (var kvp in powers)
                        if (kvp.Value is BasePowerSupply psu && psu.Connected && psu.IsOutputOn
                            && psuToMag.TryGetValue(psu.Params.ID, out var magId))
                            PowerAccrual.Sample(magId, psu.Power, dt);
                }

                // Stamp the last-tick timestamp for the watchdog. Done here
                // (after the per-type reads + group transitions) so the stamp
                // truly reflects a completed cycle, not just having entered
                // the loop body.
                Interlocked.Exchange(ref _lastHwPollUtcTicks, DateTime.UtcNow.Ticks);
                try { await Task.Delay(200, ct); }
                catch (OperationCanceledException) { break; }
            }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Releases every connected hardware resource — power supplies, serial
    /// controllers, stepper drives, and PLC sockets — in a deterministic
    /// order. Called from the main form's FormClosing handler so COM ports
    /// and TCP sockets are returned to the OS instead of being reclaimed
    /// by process exit. Failures on individual devices are logged and
    /// swallowed so a single broken disconnect can't strand the others.
    /// Idempotent — calling on an already-disconnected stack is a no-op.
    /// </summary>
    public void DisconnectAll()
    {
        try { StopManualLogging(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"DisconnectAll: measurement log close failed: {ex.Message}"); }
        // A serial port with no peer (emulator off / no real device) can wedge
        // .NET's SerialPort.Close() FOREVER — it joins the port's internal
        // eventLoopRunner thread, which is stuck in the native WaitCommEvent that
        // never returns without a peer. This method runs on the UI thread during
        // FormClosing, so a single wedged Close() freezes application exit
        // ("Not Responding", never closes). Bound the teardown: run it off-thread
        // and abandon it if it overruns. Clean disconnect is only an optimization —
        // the OS reclaims every COM/socket/PLC handle on process exit regardless.
        var worker = Task.Run(DisconnectAllCore);
        if (!worker.Wait(TimeSpan.FromSeconds(5)))
            System.Diagnostics.Trace.TraceWarning(
                "DisconnectAll: a device Close() is wedged (likely a serial port with no peer) — "
              + "abandoning after 5s so application exit is not blocked.");
    }

    private void DisconnectAllCore()
    {
        // Order matters: stop talking to peripherals before closing the
        // PLC link they live on, so an in-flight write doesn't throw on
        // the way out. Power → Controllers → Drives (Dispose) → PLCs.
        DisconnectGroup<BasePowerSupply>(DeviceType.Power,      p => p.Disconnect());
        DisconnectGroup<BaseController> (DeviceType.Controller, c => c.Disconnect());
        DisconnectGroup<IDisposable>    (DeviceType.Drive,      d => d.Dispose());

        foreach (var (id, plc) in plcs)
        {
            try { plc.Disconnect(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"DisconnectAll: PLC '{id}' disconnect failed: {ex.Message}");
            }
        }
    }

    private void DisconnectGroup<T>(DeviceType type, Action<T> disconnect) where T : class
    {
        if (!hwElements.Item.TryGetValue(type, out var elements) || elements is null)
            return;
        foreach (var kvp in elements)
        {
            if (kvp.Value is T item)
            {
                try { disconnect(item); }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        $"DisconnectAll: {type} '{kvp.Key}' disconnect failed: {ex.Message}");
                }
            }
        }
    }

    public IEnumerable<IBaseComponent> AllDevices()
    {
        foreach (var group in hwElements.Item.Values)
            if (group is not null)
                foreach (var kvp in group)
                    yield return kvp.Value;
    }

    /// <summary>Runs the idle-gated active self-test over all devices and PLCs.</summary>
    public SelfTestReport RunHardwareSelfTest()
        => Diagnostics.RunSelfTest(AllDevices(), plcs, IsDepositionActive);

    public void ReadState(Elements list, DeviceType type)
    {
        if (list != null)
        {
            if (type == DeviceType.Valve)
            {
                foreach (var item in list)
                {
                    HWStatus status = item.Value.Connected ? HWStatus.Connected : HWStatus.Disconnected;
                    ThreadMsg? vmsg = item.Value.Connected
                        ? new ValMsg<bool>(MsgType.Value, DeviceType.Valve, item.Key, (item.Value as BaseValve)!.LastOpen)
                        : null;
                    DispatchStatusAndMessage(DeviceType.Valve, item.Key, status, vmsg);
                }
                return;
            }
            foreach (var item in list)
            {
                ThreadMsg? msg = null;
                HWStatus status = HWStatus.Disconnected;

                if (item.Value.Connected)
               {
                    switch (type)
                    {
                        case DeviceType.Valve:
                            msg = new ValMsg<bool>(MsgType.Value, type, item.Key, (item.Value as BaseValve)!.LastOpen);
                            break;
                        case DeviceType.MFC:
                            msg = new ValMsg<float>(MsgType.Value, type, item.Key, (item.Value as BaseMFC)!.LastFlow);
                            break;
                        case DeviceType.Power:
                            var psu = (item.Value as BasePowerSupply)!;
                            msg = new ParamMsg(MsgType.Value, type, item.Key, psu.GetReadback());
                            break;
                        case DeviceType.Sensor:
                        {
                            var sensor = (item.Value as BaseSensor)!;
                            float val = sensor.LastValue;
                            msg = new ValMsg<float>(MsgType.Value, type, item.Key, val);
                            string sensorId = sensor.Params.ID;
                            string chamberId = sensor.Chamber;
                            if (!chamberAlarms.TryGetValue(chamberId, out var chAlarms))
                            {
                                break;
                            }
                            var chState = GetChamberState(chamberId);
                            var newState = chAlarms.CheckSensorTransitions(chState, sensorId, val);
                            if (newState.HasValue)
                            {
                                SendLogMesssage("State", $"Transition {chamberId}: {chState} -> {newState.Value} (sensor {sensorId} = {val:g3})");
                                SetChamberState(chamberId, newState.Value);
                                chState = newState.Value;
                            }
                            if (chAlarms.Check(chState, sensorId, val,
                                    vid => (FindElement(DeviceType.Valve, vid) as BaseValve)?.LastOpen))
                            {
                                ActiveAlarms.Report(chAlarms.ChamberId, chAlarms.AlarmMessage);
                                guiwriter.TryWrite(new ValMsg<string>(MsgType.Alarm, DeviceType.System, item.Key, chAlarms.AlarmMessage));
                            }
                            break;
                        }
                        case DeviceType.Pump:
                        {
                            var pump = (item.Value as BasePump)!;
                            bool[] pumpStatus = pump.LastStatus;
                            // Cold cache: LastStatus is empty until the pump's bus
                            // poller runs its first Poll(). Forwarding an empty array
                            // crashes TurboPumpControl.SetState (needs a full status
                            // array). Skip this cycle — same guard as the Controller
                            // case below. The next cycle (post-poll) forwards normally.
                            if (pumpStatus.Length == 0) break;
                            msg = new ValMsg<bool[]>(MsgType.Value, type, item.Key, pumpStatus);

                            string pumpId = pump.Params.ID;
                            // Route the device-status alarm to the pump's OWN chamber
                            // (HWConf carries chamber="..."), so non-Main cluster turbo
                            // alarms fire. Fall back to the default chamber when the
                            // pump's chamber has no Alarms instance.
                            var pumpChAlarms = chamberAlarms.TryGetValue(pump.Chamber, out var pa)
                                ? pa
                                : chamberAlarms[CoreConstants.DefaultChamberId];
                            if (pumpChAlarms.CheckDeviceStatus(GetChamberState(pump.Chamber), pumpId, pumpStatus))
                            {
                                ActiveAlarms.Report(pumpChAlarms.ChamberId, pumpChAlarms.AlarmMessage);
                                guiwriter.TryWrite(new ValMsg<string>(
                                    MsgType.Alarm, DeviceType.System, pumpId, pumpChAlarms.AlarmMessage));
                            }
                            break;
                        }
                        case DeviceType.Stage:
                            msg = new ValMsg<float>(MsgType.Value, type, item.Key, (item.Value as BaseStage)!.LastPosition);
                            break;
                        case DeviceType.Controller:
                            var ctrl = (item.Value as BaseController)!;
                            object[] measurement = ctrl.LastMeasurement;
                            if (measurement.Length == 0)
                                break;
                            msg = new ParamMsg(MsgType.Value, type, item.Key, measurement);
                            break;
                        case DeviceType.Drive:
                            // Drives are polled by DriveStateReader at 50ms for smooth animation.
                            continue;
                        case DeviceType.Robot:
                        {
                            var robot = (item.Value as BaseRobot)!;
                            var robotState = GetChamberState(robot.Chamber);
                            // Poll only while the chamber is idle: during a transfer the
                            // worker owns the robot's PLC reads (single-reader socket),
                            // so the hot loop must not poll it concurrently.
                            // Since the robot is excluded from the BusPollers
                            // this is its ONLY idle-time reader, so the poll must not be
                            // gated on the chamber having an Alarms instance — the
                            // transfer widget goes stale otherwise.
                            if (robotState == SystemState.Idle)
                            {
                                robot.Poll();
                                if (chamberAlarms.TryGetValue(robot.Chamber, out var robotAlarms)
                                    && robotAlarms.CheckDeviceStatus(robotState, robot.Params.ID, robot.StatusBits()))
                                {
                                    ActiveAlarms.Report(robotAlarms.ChamberId, robotAlarms.AlarmMessage);
                                    guiwriter.TryWrite(new ValMsg<string>(
                                        MsgType.Alarm, DeviceType.System, robot.Params.ID, robotAlarms.AlarmMessage));
                                }
                            }
                            break;
                        }
                        default:
                            break;
                    }
                    status = HWStatus.Connected;
                }
                string statusKey = $"{type}_{item.Key}";
                if (!lastStatus.TryGetValue(statusKey, out HWStatus prevStatus) || prevStatus != status)
                {
                    lastStatus[statusKey] = status;
                    // A status change (disconnect/reconnect) invalidates the cached
                    // value so the first reading after it is always forwarded afresh.
                    lastPolledValue.TryRemove(statusKey, out _);
                    guiwriter.TryWrite(new ValMsg<HWStatus>(MsgType.Status, type, item.Key, status));
                }
                if (msg != null && ShouldForwardPolledValue(type, statusKey, msg))
                {
                    guiwriter.TryWrite(msg);
                }
            }
        }
    }

    /// <summary>
    /// Change-gates the non-valve poll Value messages so the 200 ms poll only
    /// forwards a reading that actually changed since the previous cycle.
    /// Sensors are exempt (their stream feeds the live trend graph and is
    /// expected every tick). Valves are gated separately on their own bulk path
    /// (<see cref="DispatchStatusAndMessage"/>). Returns <c>true</c> when the
    /// message should be written to the GUI channel.
    /// </summary>
    private bool ShouldForwardPolledValue(DeviceType type, string statusKey, ThreadMsg msg)
    {
        if (type == DeviceType.Sensor)
            return true;

        object? current = msg switch
        {
            ValMsg<float> vf  => vf.Value,
            ValMsg<bool[]> vb => vb.Value,
            ParamMsg pm       => pm.Args,
            _                 => null,
        };

        // Unknown payload shape — never suppress (fail open).
        if (current is null)
            return true;

        if (lastPolledValue.TryGetValue(statusKey, out object? previous)
            && !PolledValueChanged(previous, current))
            return false;

        lastPolledValue[statusKey] = current;
        return true;
    }

    /// <summary>
    /// Value-equality for the poll change-gate, by payload shape: floats compare
    /// with a small tolerance (PLC ADC jitter must not defeat the gate), bool[]
    /// and object[] compare element-wise (a moving throttle / spinning pump
    /// flips bits that must propagate), everything else by Equals.
    /// </summary>
    private static bool PolledValueChanged(object? prev, object? cur)
    {
        switch (cur)
        {
            case float cf:
                return prev is not float pf || !FloatsClose(pf, cf);
            case bool[] cb:
                if (prev is not bool[] pb || pb.Length != cb.Length) return true;
                for (int i = 0; i < cb.Length; i++)
                    if (pb[i] != cb[i]) return true;
                return false;
            case object[] co:
                if (prev is not object[] po || po.Length != co.Length) return true;
                for (int i = 0; i < co.Length; i++)
                {
                    if (co[i] is float ef)
                    {
                        if (po[i] is not float pe || !FloatsClose(pe, ef)) return true;
                    }
                    else if (!Equals(po[i], co[i])) return true;
                }
                return false;
            default:
                return !Equals(prev, cur);
        }
    }

    private static bool FloatsClose(float a, float b)
    {
        float diff = Math.Abs(a - b);
        if (diff <= 1e-6f) return true;
        return diff <= 1e-4f * Math.Max(Math.Abs(a), Math.Abs(b));
    }

    /// <summary>
    /// Bus-poller wire pass for valves: one bulk PLC round-trip per controller,
    /// refreshing each valve's LastOpen cache. No GUI dispatch (the coordinator
    /// emits gated valve messages from LastOpen).
    /// </summary>
    internal void PollValvesBulk(Elements list)
    {
        if (list == null || list.Count == 0) return;
        var bulkable = new List<(PlcValve valve, (int, int, int) desc)>();
        foreach (var item in list)
        {
            if (item.Value.Connected && item.Value is PlcValve pv && pv.TryGetBulkReadDescriptor(out var desc))
                bulkable.Add((pv, desc));
            else
                item.Value.Poll();                       // fallback: per-valve wire read -> LastOpen
        }
        foreach (var group in bulkable.GroupBy(t => t.valve.PLC))
        {
            var entries = group.ToList();
            try
            {
                bool[] states = group.Key.ReadBoolsBulk(entries.Select(e => e.desc).ToList());
                for (int i = 0; i < entries.Count; i++)
                    entries[i].valve.CacheOpen(states[i]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"PollValvesBulk: bulk failed ({ex.Message}); per-valve fallback");
                foreach (var e in entries) e.valve.Poll();
            }
        }
    }

    private void DispatchStatusAndMessage(DeviceType type, string id, HWStatus status, ThreadMsg? msg)
    {
        string statusKey = $"{type}_{id}";
        if (!lastStatus.TryGetValue(statusKey, out HWStatus prevStatus) || prevStatus != status)
        {
            lastStatus[statusKey] = status;
            // Any status change (e.g. disconnect/reconnect) invalidates the
            // cached value so the next reading is always forwarded afresh.
            lastValveOpen.TryRemove(statusKey, out _);
            guiwriter.TryWrite(new ValMsg<HWStatus>(MsgType.Status, type, id, status));
        }
        if (msg != null)
        {
            // Change-gate the valve open/closed value: the 200 ms poll re-reads
            // the same state every cycle, and re-broadcasting it makes the GUI
            // replay the valve colour transition + flow recalculation on every
            // tick (the "valve can't decide its colour" shimmer). Only forward
            // when the boolean actually flips, or when first seen after a status
            // change cleared the cache.
            if (msg is ValMsg<bool> boolMsg)
            {
                if (lastValveOpen.TryGetValue(statusKey, out bool prevOpen) && prevOpen == boolMsg.Value)
                    return;
                lastValveOpen[statusKey] = boolMsg.Value;
            }
            guiwriter.TryWrite(msg);
        }
    }


}

public sealed record RecipeLifecycleEvent(string Phase, string Recipe, string Detail);



