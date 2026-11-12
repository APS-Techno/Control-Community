using System.Xml;
using APS.ProcessPanel.Contracts;
using APS.RecipeModel.Model;
using APS.RecipeModel.Serialization;
using APS.RecipeModel.Validation;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Threads;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Production IAgentSystemAccess over the live Core. Reads ONLY poll-fed caches
/// (LastValue/LastFlow/LastOpen/...) — never live PLC — mirroring FrmMain.RefreshClusterPanel.</summary>
internal sealed class CoreAgentSystemAccess(CoreThread systemThread, ChannelPool pool, EventLogRingBuffer log)
    : IAgentSystemAccess
{
    private Core Core => systemThread.Core;

    public object DescribeMachine() => new
    {
        profile = Path.GetFileName(Core.ConfigDir ?? ""),
        appVersion = AppVersion.Full,
        machineName = Environment.MachineName,
        chambers = Core.ChamberIds,
        protocol = AgentProtocol.Version,
        // Visible on the FIRST call an agent makes, not only when an upload fails
        // (agent campaign 2026-09-15, item 8).
        controlMode = Core.ControlMode.Mode.ToString(),
        hostCommands = "mutating commands (recipe.upload/start/abort/resolve_pause, device.command) need " +
                       "controlMode == \"Remote\" (Online-Remote); only an operator can switch it, under " +
                       "Service → Control mode… — do not poll for it",
    };

    public object DescribeHardware()
    {
        var groups = new Dictionary<string, object>();
        foreach (var (type, elements) in Core.hwElements.Item)
        {
            var list = new List<object>();
            foreach (var (name, dev) in elements)
                list.Add(DescribeDevice(name, dev));
            if (list.Count > 0) groups[type.ToString()] = list;
        }
        object sources;
        try
        {
            sources = new
            {
                flanges = SourcesMapLoader.LoadFlangeSourceMap(Core.ConfigDir!).Values,
                powerSources = SourcesMapLoader.LoadPowerSources(Core.ConfigDir!),
            };
        }
        catch { sources = new { }; }
        return new { devices = groups, sources };
    }

    internal static object DescribeDevice(string name, IBaseComponent dev) => dev switch
    {
        BaseMFC m => new
        {
            id = name,
            plcId = dev.Params.ID,
            description = dev.Params.Description,
            connected = dev.Connected,
            chamber = m.Chamber,
            gateValves = m.GateValves,
            gatingLogic = m.GateValves.Count > 0 ? "OR" : "none",
        },
        BaseStage => new
        {
            id = name,
            plcId = dev.Params.ID,
            description = dev.Params.Description,
            connected = dev.Connected,
            chamber = ChamberOf(dev),
            isOperational = dev.Connected,
        },
        _ => new
        {
            id = name,
            plcId = dev.Params.ID,
            description = dev.Params.Description,
            connected = dev.Connected,
            chamber = ChamberOf(dev),
        },
    };

    private static string? ChamberOf(object dev) => dev switch
    {
        BaseSensor s => s.Chamber, BaseValve v => v.Chamber, BaseMFC m => m.Chamber,
        BasePump p => p.Chamber, _ => null,
    };

    public object DescribeRecipes(string? folder)
    {
        var user = new List<object>();
        // A key scoped to a recipe folder lists only that folder: the rest of the library is other work's.
        foreach (var f in RecipeStore.ListRecipeFiles(AppPaths.RecipesDir, folder))
            try
            {
                var rf = RecipeSerializer.Load(f);
                user.Add(new
                {
                    name = Path.GetFileNameWithoutExtension(f),
                    description = rf.Description,
                    parameters = rf.Parameters,
                });
            }
            catch { user.Add(new { name = Path.GetFileNameWithoutExtension(f), error = "unreadable" }); }
        return new
        {
            user,
            embedded = Core.embRecipes?.GetKnownNames() ?? Enumerable.Empty<string>(),
        };
    }

    public object DescribeDsl() => new
    {
        reference = DslReference.Text,
        commands = RecipeValidator.AllKnownCommands,
        operations = Core.GetOperationDeclarations()
            .Select(o => new { o.Name, o.BoundDevice, o.Vars }),
        devicesByKind = Core.GetDeviceNamesByKind(),
        installedSources = Core.GetInstalledSourceNames(),
    };

    public object DescribeInterlocks() =>
        Core.Interlocks.GetAllInterlocks().Select(kv => new
        {
            id = kv.Key,
            conditions = kv.Value.Conditions.Select(c => new
            {
                device = c.Target?.Params.ID,
                value = c.Value,
                condition = c.Condition,          // -1 less, 0 equal, 1 more
                stringCondition = c.StringCondition,
            }),
        });

    public object DescribeAlarms() => DescribeAlarmChambers(Core.chamberAlarms);

    internal static IEnumerable<object> DescribeAlarmChambers(IEnumerable<KeyValuePair<string, Alarms>> chambers) =>
        chambers.Select(kv => (object)new
        {
            chamber = kv.Key,
            rules = kv.Value.Rules.Select(r => new
            {
                sensor = r.Sensor,
                value = r.Value,
                condition = r.Condition,
                state = r.State.ToString(),
                message = r.Message,
                delaySeconds = r.DelaySeconds,
                unless = r.GuardValve is null ? null : $"{r.GuardValve} {(r.GuardValveOpen ? "open" : "closed")}",
                unit = "Torr",
            }),
            deviceStatusRules = kv.Value.DeviceStatusRules.Select(r => new
            { device = r.DeviceId, statusBit = r.StatusBit, state = r.State.ToString(), message = r.Message }),
            transitions = new
            {
                sensor = kv.Value.SensorTransitions.Select(t => new
                { from = t.From.ToString(), to = t.To.ToString(), sensor = t.Sensor, condition = t.Condition, value = t.Value }),
                valve = kv.Value.ValveTransitions.Select(t => new
                { from = t.From.ToString(), to = t.To.ToString(), valve = t.ValveId, open = t.ValveOpen, requirements = t.Requirements }),
                group = kv.Value.GroupTransitions.Select(t => new
                { from = t.From.ToString(), to = t.To.ToString(), group = t.Group.ToString(), condition = t.Condition.ToString() }),
            },
        });

    public object SystemStatus() => new
    {
        chambers = Core.ChamberIds.Select(id => new
        {
            id,
            state = Core.GetChamberState(id).ToString(),
        }),
        activeAlarms = Core.ActiveAlarms.Snapshot(),   // sticky until reset
        isRecipeRunning = Core.IsRecipeRunning,
        isDepositionActive = Core.IsDepositionActive,
        controlMode = Core.ControlMode.Mode.ToString(),
        recipe = RecipeProgressTracker.Snapshot(Core.IsRecipeRunning).Progress,
    };

    public object DeviceSnapshot()
    {
        var snap = new Dictionary<string, object>();
        snap["valves"] = Select<BaseValve>(DeviceType.Valve,
            (id, v) => new { id, open = v.LastOpen, connected = v.Connected });
        snap["pumps"] = Select<BasePump>(DeviceType.Pump,
            (id, p) => new { id, running = p.LastRunning, status = p.LastStatus, connected = p.Connected });
        // setpoint = what the DEVICE holds (polled readback); commanded = what
        // this Control session wrote. They differ after a Control restart, when
        // the machine is already flowing but nothing has been commanded yet —
        // reporting only `commanded` made a running MFC look like phantom gas.
        snap["mfcs"] = Select<BaseMFC>(DeviceType.MFC,
            (id, m) => new
            {
                id,
                flow = m.LastFlow,
                setpoint = m.LastSetpoint,
                commanded = m.Setpoint,
                connected = m.Connected,
            });
        snap["sensors"] = Select<BaseSensor>(DeviceType.Sensor,
            (id, s) => new { id, value = s.LastValue, connected = s.Connected });
        snap["power"] = Select<BasePowerSupply>(DeviceType.Power,
            (id, p) => new
            {
                id, voltage = p.Voltage, current = p.Current, power = p.Power,
                on = p.IsOutputOn, responding = p.IsResponding,
                setVoltage = p.SetVoltage, setCurrent = p.SetCurrent, setPower = p.SetPower,
            });
        snap["stages"] = Select<BaseStage>(DeviceType.Stage,
            (id, s) => new { id, position = s.LastPosition, connected = s.Connected });
        snap["drives"] = Select<BaseStepperDrive>(DeviceType.Drive,
            (id, d) => new { id, position = d.CurrentPosition, moving = d.IsMoving, homed = d.IsHomed, connected = d.Connected });
        snap["controllers"] = Select<BaseController>(DeviceType.Controller,
            (id, c) => new { id, measurement = c.LastMeasurement, connected = c.Connected });
        return snap;
    }

    private List<object> Select<T>(DeviceType type, Func<string, T, object> project) where T : class
    {
        var list = new List<object>();
        if (!Core.hwElements.Item.TryGetValue(type, out var elements)) return list;
        foreach (var (name, dev) in elements)
            if (dev is T typed) list.Add(project(name, typed));
        return list;
    }

    public object TailLog(int count) =>
        log.Tail(Math.Clamp(count, 1, 500)).Select(e => new
        {
            ts = e.Timestamp.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            source = e.Source, message = e.Message, level = e.Level.ToString(),
        });

    /// <summary>Roots are read per call: AppPaths is initialised at config load, which may follow
    /// the AgentServer's construction.</summary>
    private static RunArtifacts Runs => new(AppPaths.DataDir, AppPaths.LogsDir, AppPaths.RecipesDir);

    public object ListRuns(int count) => new
    {
        runs = Runs.List(Math.Clamp(count, 1, 50), Core.ActiveDataLogPath).Select(r => new
        {
            runId = r.RunId,
            recipe = r.Recipe,
            startedAt = r.StartedAt,
            endedAt = r.EndedAt,
            outcome = r.Outcome,
            message = r.Message,
            artifacts = r.Artifacts.Select(a => new { kind = a.Kind, fileName = a.FileName, bytes = a.Bytes }),
        }),
    };

    public object ReadRunFile(string runId, string kind, long offset, int maxBytes)
    {
        var c = Runs.Read(runId, kind, offset, maxBytes);
        return new
        {
            runId = c.RunId,
            kind = c.Kind,
            fileName = c.FileName,
            offset = c.Offset,
            bytes = c.Bytes.Length,
            totalBytes = c.TotalBytes,
            eof = c.Eof,
            contentBase64 = Convert.ToBase64String(c.Bytes),
        };
    }

    /// <summary>
    /// KNOWN LIMITATION — held is not distinguishable on the wire. Since the operator-hold
    /// feature, a paused recipe can be either "waiting at the fault dialog" (watchdog armed,
    /// manual hardware control still gated) or "HELD for the operator" (watchdog disarmed,
    /// <c>Core.IsManualControlGated</c> false, the machine handed back for manual repair) —
    /// but this method reports neither <c>Core.IsHeldForOperator</c> nor
    /// <c>IsManualControlGated</c>. <c>frmMain</c> shows the difference locally (the status-strip
    /// HELD banner and a <c>HeldStepPrefix</c> on the process-panel step text,
    /// <c>Forms/Main/frmMain.cs</c>), but that is a UI-only overlay on the same
    /// <c>RecipeState.Paused</c> this method already returns — an integrator polling
    /// <c>recipe.status</c> alone cannot tell the two apart. Add <c>isHeldForOperator</c> here if
    /// a remote caller ever needs to know before deciding whether it is safe to, say, poll
    /// hardware itself. See docs/superpowers/specs/2026-09-04-operator-hold-design.md §5.1.
    /// </summary>
    public object RecipeStatus()
    {
        // Live view: elapsed carried forward to now, per-step age, and the last terminal
        // outcome kept beside the failure protocol's own progress (see RecipeProgressTracker).
        var s = RecipeProgressTracker.Snapshot(Core.IsRecipeRunning);
        return new
        {
            isRecipeRunning = Core.IsRecipeRunning,
            isUserRecipe = Core.IsUserRecipe,
            failureProtocolActive = s.FailureProtocolActive,
            serverTime = RecipeProgressTracker.Clock(),
            progress = s.Progress,
            stepElapsedSeconds = s.StepElapsed?.TotalSeconds,
            progressUpdatedAt = s.UpdatedAt,
            lastOutcome = s.LastOutcome is null ? null : new
            {
                name = s.LastOutcome.Name,
                state = s.LastOutcome.State.ToString(),
                message = s.LastOutcome.Message,
                at = s.LastOutcome.At,
            },
        };
    }

    public IReadOnlyList<RecipeError> ValidateRecipe(string content, bool isRcpx)
    {
        string script;
        if (isRcpx)
        {
            try { script = RecipeSerializer.LoadFromString(content).FullScript; }
            catch (XmlException ex)
            { throw new AgentOpException(AgentErrors.RecipeParseError, $".rcpx XML error: {ex.Message}"); }
        }
        else script = content;
        return BuildValidator().Validate(script);
    }

    // Same construction as the worker's pre-run validation (CoreThread.Execute.cs:479-499).
    private RecipeValidator BuildValidator()
    {
        var semantics = RecipeSemantics.Build(
            Core.GetOperationDeclarations(), Core.GetDeviceNamesByKind(), Core.GetSetpointParameterNames(),
            sourceToPsu: SourceToPsuMap(Core.ConfigDir is null
                ? []
                : SourcesMapLoader.LoadFlangeSourceMap(Core.ConfigDir).Values),
            mfcGateValves: MfcGateValvesMap(MfcDevices()),
            rotationOperational: Core.FindElement(DeviceType.Stage, "rotation") is IBaseComponent { Connected: true }
                || Core.FindElement(DeviceType.Stage, "Rotation") is IBaseComponent { Connected: true });
        return new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            Core.GetKnownDeviceNames(),
            Core.GetInstalledSourceNames(),
            Core.embRecipes?.GetKnownNames(),
            throttleValveSensor: "pm",
            semantics: semantics);
    }

    private IEnumerable<(string Id, IBaseComponent Dev)> MfcDevices() =>
        Core.hwElements.Item.TryGetValue(DeviceType.MFC, out var mfcs)
            ? mfcs.Select(kv => (kv.Key, (IBaseComponent)kv.Value)).ToList()
            : [];

    internal static IReadOnlyDictionary<string, string> SourceToPsuMap(IEnumerable<FlangeSourceInfo> sources)
    {
        var suppliers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sources)
        {
            string name = SourceNames.Effective(s.Name, s.Material);
            if (name.Length == 0 || string.IsNullOrWhiteSpace(s.PowerSupplyId)) continue;
            if (!suppliers.TryGetValue(name, out var set))
                suppliers[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(s.PowerSupplyId.Trim());
        }
        return suppliers.Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Single(), StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyDictionary<string, IReadOnlyCollection<string>> MfcGateValvesMap(
        IEnumerable<(string Id, IBaseComponent Dev)> mfcs) =>
        mfcs.Where(m => m.Dev is BaseMFC { GateValves.Count: > 0 })
            .ToDictionary(m => m.Id, m => ((BaseMFC)m.Dev).GateValves.ToArray() as IReadOnlyCollection<string>,
                StringComparer.OrdinalIgnoreCase);

    public object SaveRecipe(string name, string content, bool overwrite, string? folder)
    {
        if (!RecipeStore.IsValidRecipeName(name))
            throw new AgentOpException(AgentErrors.BadRequest, $"invalid recipe name '{name}'");
        var findings = ValidateRecipe(content, isRcpx: true);
        var hard = findings.Where(f => f.Severity == RecipeErrorSeverity.Error).ToList();
        if (hard.Count > 0)
            throw new AgentOpException(AgentErrors.RecipeParseError,
                "recipe failed validation",
                hard.Select(f => new { line = f.Line, message = f.Message }).ToList());
        // Name lookup over the library, the key's folder, the overwrite guard and the write, under one
        // lock: RecipeStore.Save. recipe.start runs an embedded recipe before any file of the same name,
        // so a scoped key may not take an embedded name either.
        string path = RecipeStore.Save(AppPaths.RecipesDir, name, content, overwrite, folder,
            n => RecipeStore.IsEmbeddedName(Core.embRecipes?.GetKnownNames(), n));
        return new { saved = name, path, warnings = findings.Count - hard.Count };
    }

    public void StartRecipe(string name, IReadOnlyDictionary<string, string>? parameters)
    {
        if (Core.IsRecipeRunning)
            throw new AgentOpException(AgentErrors.Busy, "a recipe is already running");
        if (Core.embRecipes?.Contains(name) == true)
        {
            pool.WriteHW(new ThreadMsg(MsgType.Command, DeviceType.Recipe, name));
            return;
        }
        string? path = RecipeStore.FindRecipePath(AppPaths.RecipesDir, name);
        if (path is null)
            throw new AgentOpException(AgentErrors.NotFound,
                $"no user recipe '{name}.rcpx' and no embedded recipe '{name}'");
        var overrides = parameters is null
            ? null
            : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        pool.WriteHW(new ParamMsg(MsgType.Command, DeviceType.Worker, "Execute",
            overrides is null ? [path] : [path, overrides]));
    }

    public void AbortRecipe() =>
        pool.WriteHW(new ValMsg<string>(MsgType.Command, DeviceType.Worker, "Abort", ""));

    /// <summary>Answers a waiting fault pause. Goes straight to <see cref="Core.ResolveFault"/> —
    /// the same seam the pause dialog's buttons use — rather than through the HW channel: the
    /// decision is handed to a <c>TaskCompletionSource</c> the recipe thread is already awaiting,
    /// and it is <c>Interlocked</c>-guarded, so it needs no worker round trip.
    ///
    /// THE GUARD IS THE POINT. <c>ResolveFault</c> is deliberately a no-op when nothing is
    /// pending (the watchdog or the recipe end got there first), which is right for a dialog
    /// button that may be pressed a moment too late and wrong for a wire caller: a test — or an
    /// agent — that resolves a pause which never happened would be told it succeeded. The check
    /// is inherently racy against the watchdog, and that is acceptable: it converts the common
    /// "nothing is paused" mistake into a loud error and leaves the rare, genuinely-simultaneous
    /// case behaving exactly as the dialog does.</summary>
    public void ResolveFaultPause(FaultDecision decision)
    {
        if (!Core.IsFaultPaused)
            throw new AgentOpException(AgentErrors.NotFound, "no fault pause is waiting for a decision");
        Core.ResolveFault(decision);
    }

    /// <summary>DeviceType kinds whose manual commands are dispatched as BUILT-IN worker
    /// operations (Core.LoadEmbeddedOperations: "valve"/"mfc"/"stage"/"pump") rather than
    /// routed straight to a per-type HWDispatcher handler. Mirrors the exact wire shape the
    /// GUI itself uses for these types — frmMain.cs SystemControl_DoubleClick /
    /// SendFloatValueMessage: Target=Worker, Operation=&lt;the op name&gt;,
    /// Args=[deviceType, deviceId, ...values]. HWDispatcher has NO case for
    /// DeviceType.Valve/MFC/Pump/Stage directly (its switch(msg.Target) falls through to a
    /// no-op default for anything but Worker/Recipe/Controller/Power/Drive), so sending
    /// those types with Target=type (the naive IHwCommandSink shape below) would silently
    /// drop the command.</summary>
    private static readonly Dictionary<DeviceType, string> WorkerRoutedOps = new()
    {
        [DeviceType.Valve] = "Valve",
        [DeviceType.MFC] = "MFC",
        [DeviceType.Pump] = "Pump",
        [DeviceType.Stage] = "Stage",
    };

    public void SendDeviceCommand(string deviceType, string deviceId, object[] args)
    {
        if (!Enum.TryParse<DeviceType>(deviceType, ignoreCase: true, out var type))
            throw new AgentOpException(AgentErrors.BadRequest, $"unknown device type '{deviceType}'");
        if (Core.FindElement(type, deviceId) is null)
            throw new AgentOpException(AgentErrors.UnknownDevice, $"no {type} device '{deviceId}'");
        // IsManualControlGated, not IsRecipeRunning. A HELD run has parked itself and handed
        // the machine over deliberately, and remote diagnosis-and-recovery — an agent
        // re-igniting an RF source that dropped out — is one of the reasons Hold exists. An
        // ordinary running recipe still refuses every manual device command BUT ONE: see
        // IsKillSwitch.
        if (Core.IsManualControlGated && !IsKillSwitch(type, args))
            throw new AgentOpException(AgentErrors.Busy,
                "manual device commands are rejected while a recipe is running");

        // Past the gate above there are now THREE ways to be here: no recipe running but a power
        // supply still drawing watts (manual RF/DC run, or a recipe that just ended without the
        // operator powering down), a HELD run — which still owns the machine — or the kill
        // switch on a running one. Either way,
        // block the devices that can disturb an in-progress deposition — the stage/holder drive,
        // the rotation stage, and any valve that is a source shutter. Power commands are
        // deliberately NEVER blocked here (an operator must always be able to kill power), which
        // is also what makes a held run remotely recoverable: a remote caller may re-ignite a
        // supply but may not move the holder out from under an operator who is standing at the
        // machine with their hands on it. That asymmetry with the local panel — which DOES allow
        // jog while held — is intended.
        // IsShutterId is deferred behind the IsDepositionActive/Valve short-circuit below —
        // it loads Sources.xml, so it should only run when the guard could actually fire.
        if (Core.IsDepositionActive)
        {
            bool isShutter = type is DeviceType.Valve && IsShutterId(deviceId);
            if (IsDepositionGated(type, isShutter))
            {
                throw new AgentOpException(AgentErrors.Busy,
                    $"manual {type} commands are rejected while {DepositionGuardReason()}");
            }
        }

        if (WorkerRoutedOps.TryGetValue(type, out var opName))
        {
            // Built-in embedded op (valve/mfc/pump/stage): route through the Worker
            // dispatch path exactly like a manual GUI click — see WorkerRoutedOps.
            object[] workerArgs = [type, deviceId, .. args];
            pool.WriteHW(new ParamMsg(MsgType.Command, DeviceType.Worker, opName, workerArgs));
            return;
        }

        if (type is DeviceType.Power or DeviceType.Drive or DeviceType.Controller)
        {
            // These three ARE routed by HWDispatcher's switch(msg.Target) — same
            // shape IHwCommandSink/HwCommandSinkAdapter uses (Core\Menus\Services\
            // HwCommandSinkAdapter.cs): Target=type, Operation=deviceId,
            // Args=[commandName, ...values].
            pool.WriteHW(new ParamMsg(MsgType.Command, type, deviceId, args));
            return;
        }

        throw new AgentOpException(AgentErrors.BadRequest,
            $"device type '{type}' does not support manual commands");
    }

    /// <summary>
    /// The ONE manual command a running recipe does not refuse on this path: a power supply's
    /// <c>OutputOff</c>. RULING R.
    ///
    /// <para>The PSU panel lets it through — <c>ProcessPowerCommand</c> gates every manual PSU
    /// command on <c>IsManualControlGated</c> except this one, because a kill switch must never
    /// sit behind a dialog. Refusing it on the wire protects nothing: the same caller already has
    /// <c>recipe.abort</c>, which is strictly MORE destructive (it tears down the whole run and
    /// drives FAILURE_PROTOCOL), while a single-supply kill is the surgical option. It is also the
    /// remote arc-recovery path Hold exists for, and — because <c>Core.RequestOperatorCut</c> has
    /// exactly one caller, the panel's OutputOff — the only entry point to the operator-cut pause
    /// an unattended harness can reach.</para>
    ///
    /// <para>Letting it past this gate needs no second arming path: it lands in
    /// <c>ProcessPowerCommand</c>'s own <c>OutputOff</c> case, which snapshots the supply, drops
    /// the output and calls <c>RequestOperatorCut</c> exactly as a panel press does.</para>
    ///
    /// <para><c>Reconnect</c> — which the panel also allows through, and which is handled before
    /// its Connected guard — is deliberately NOT carved out here. That is a separate decision and
    /// is not being taken as a side effect of this one.</para>
    ///
    /// <para>Channel-qualified forms count (<c>"OutputOff:2"</c> on a multi-channel analyser), split exactly the
    /// way <c>ProcessPowerCommand</c> splits them, so the two gates cannot disagree about what a
    /// kill switch is on a multi-channel supply.</para>
    /// </summary>
    internal static bool IsKillSwitch(DeviceType type, object[] args)
    {
        if (type is not DeviceType.Power || args.Length == 0 || args[0] is not string raw)
            return false;
        int colon = raw.IndexOf(':');
        string command = colon >= 0 ? raw[..colon] : raw;
        return command.Equals("OutputOff", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pure predicate for the deposition guard above: true when a manual command of
    /// this <paramref name="type"/> must be rejected while a deposition is active (PSU still
    /// drawing watts, no recipe running). Drive and Stage are always gated; Valve only when
    /// <paramref name="isShutter"/> says it is a source shutter. Power — and every other
    /// DeviceType — is never gated here: an operator must always be able to kill power.
    /// </summary>
    internal static bool IsDepositionGated(DeviceType type, bool isShutter) =>
        type is DeviceType.Drive or DeviceType.Stage || (type is DeviceType.Valve && isShutter);

    /// <summary>True when <paramref name="deviceId"/> is a source shutter valve per
    /// Sources.xml (case-insensitive). FlangeSourceInfo (used by DescribeHardware) has no
    /// shutter-id member, so this loads the cluster-panel projection instead — it carries
    /// ClusterSourceInfo.ShutterId. Best-effort: any failure (missing/malformed Sources.xml,
    /// missing ConfigDir) is swallowed and treated as "not a shutter", which narrows the
    /// deposition guard above to Drive/Stage only rather than blocking a Valve command on a
    /// resolution error.</summary>
    private bool IsShutterId(string deviceId)
    {
        try
        {
            return SourcesMapLoader.LoadChamberSources(Core.ConfigDir!)
                .Values
                .SelectMany(sources => sources)
                .Any(s => !string.IsNullOrWhiteSpace(s.ShutterId)
                          && string.Equals(s.ShutterId, deviceId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Why the deposition guard is refusing, as a clause completing "manual X commands
    /// are rejected while …". Names the power supplies actually drawing watts when there are any.
    /// <para>This guard fires in TWO situations, not one: no recipe running with a supply still
    /// energised, and a HELD run — the gate above it is <c>IsManualControlGated</c>, which a
    /// hold lets the caller past while <c>IsDepositionActive</c> stays true. During a hold with
    /// nothing energised, "a deposition is active" is false and would send a remote caller looking
    /// for film growth that is not happening.</para></summary>
    private string DepositionGuardReason()
    {
        if (Core.hwElements.Item.TryGetValue(DeviceType.Power, out var powers) && powers is not null)
        {
            var active = powers
                .Select(kv => kv.Value)
                .OfType<BasePowerSupply>()
                .Where(p => p.Connected && p.IsResponding && p.Power > Core.MinDepositionWatts)
                .Select(p => $"{p.Params.ID}: {p.Power:F0} W")
                .ToList();
            if (active.Count > 0)
                return $"power supply {string.Join(", ", active)} is still energised";
        }
        // Nothing drawing watts. The HELD branch is the one that actually fires: a hold lets the
        // caller past the first gate with IsDepositionActive still true purely because the run
        // owns the machine, and no supply need be energised for that.
        //
        // The other branch is DEFENSIVE, not a live case. Reaching it would need
        // IsDepositionActive true, the run not held, and no supply passing the filter above —
        // impossible today, because past the first gate a non-held caller has IsRecipeRunning
        // false, so Core.IsDepositionActive can only be true via EnumeratePsuWatts
        // (Core.Main.cs), whose filter is identical to the one above. It is kept because those
        // are two separate pieces of code that must agree, and if they ever drift this must say
        // something true rather than throw or return "".
        return Core.IsHeldForOperator
            ? "the run is held for the operator"
            : "a deposition is active";
    }
}
