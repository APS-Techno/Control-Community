using System.Collections.Concurrent;
using ScottPlot.WinForms;
using APS.Controls.General;
using APS.Controls.Vacuum;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.ProcessPanel.Contracts;
using Contracts = APS.ProcessPanel.Contracts;

namespace APS.SystemControl;

public partial class FrmMain
{
    // Concurrent: FindCachedControl/FindCustomPanel read and populate this from the
    // background GUI dispatcher thread, and the UI thread also resolves panels — a
    // plain Dictionary could corrupt its buckets under concurrent writes. (audit M-7)
    private readonly ConcurrentDictionary<string, Control> controlCache = new();

    // PSU device id -> magnetron-instance label, for lighting the matching schematic
    // source widget ("POW_<label>"). Rebuilt whenever sources are (re)loaded; the
    // reference is swapped wholesale (immutable map) and volatile so the GUI-dispatcher
    // thread always sees the latest. Empty until built.
    private volatile IReadOnlyDictionary<string, string> _psuToMagnetronLabel =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // Called on the UI thread after sources (re)load.
    private void RebuildPsuMagnetronGlowMap()
    {
        _psuToMagnetronLabel = sourceManager.BuildPsuToMagnetronLabel();
        // A widget is only revisited when its PSU is in the map, so a PSU that drops
        // out on reload (now ambiguous/removed) would otherwise stay lit forever.
        // Clear every magnetron's glow here; sources still mapped re-light on the next poll.
        var widgetIds = vacuumSchematic.Controls.OfType<MagnetronControl>()
            .Select(m => m.ID).Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var mag in vacuumSchematic.Controls.OfType<MagnetronControl>())
            mag.Energized = false;

        // Diagnostic: surface the PSU->magnetron-label glow map (and any mapped label
        // with no matching widget) so a config/id mismatch is visible. Shows on Reload
        // Sources; the startup call may be swallowed by the event-log boot gate, so also
        // trace it.
        string mapStr = _psuToMagnetronLabel.Count == 0
            ? "(empty)"
            : string.Join(", ", _psuToMagnetronLabel.Select(kv =>
                $"{kv.Key}->{kv.Value}{(widgetIds.Count > 0 && !widgetIds.Contains(kv.Value) ? " [NO WIDGET]" : "")}"));
        System.Diagnostics.Trace.TraceInformation($"Magnetron glow map: {mapStr}");
        DisplayLogMessage("Core", $"Magnetron glow map ({widgetIds.Count} widgets): {mapStr}");
    }

    private void ExecuteOnUIThread(Action action)
    {
        // Non-blocking marshal. The GUI dispatcher runs on a background thread and
        // posts one of these per device update. A blocking Invoke would make that
        // thread wait for the UI thread for every message, serialising the whole
        // poll cycle at UI speed (and freezing all updates for the duration of a
        // modal recipe-message dialog). BeginInvoke posts and returns; WinForms
        // coalesces the resulting repaints. Posts from this single dispatcher
        // thread stay FIFO-ordered, so update ordering is preserved.
        if (this.InvokeRequired)
        {
            try { this.BeginInvoke(action); }
            // Form closing/closed mid-poll — the handle is gone (ObjectDisposed
            // derives from InvalidOperation). Drop the late update instead of
            // faulting the dispatcher loop.
            catch (InvalidOperationException) { }
        }
        else
            action();
    }

    private Control? FindCachedControl(DeviceType type, string device)
    {
        string name = GUIConst.ControlName(type, device);
        if (controlCache.TryGetValue(name, out Control? cached))
            return cached;

        Control[] controls = vacuumSchematic.Controls.Find(name, false);
        if (controls.Length > 0)
        {
            controlCache[name] = controls[0];
            return controls[0];
        }
        return null;
    }

    private void UpdateValveControl(ThreadMsg msg, ValveControl valve)
    {
        if (msg.Type == MsgType.Status)
            valve.Connected = ((ValMsg<HWStatus>)msg).Value == HWStatus.Connected;
        if (msg.Type == MsgType.Value)
        {
            bool isOpen = ((ValMsg<bool>)msg).Value;
            valve.Open = isOpen;
            if (vacuumSchematic.SetComponentOpen(valve.ID, isOpen))
                vacuumSchematic.RecalculateFlow();
            _valveCache[valve.ID] = isOpen;
            DispatchValves();
        }
        if (msg.Type == MsgType.Log)
            DisplayLogMessage(msg);
    }

    private void UpdateGateValveControl(ThreadMsg msg, GateValveControl gateValve)
    {
        if (msg.Type == MsgType.Status)
            gateValve.Connected = ((ValMsg<HWStatus>)msg).Value == HWStatus.Connected;
        if (msg.Type == MsgType.Value)
        {
            bool isOpen = ((ValMsg<bool>)msg).Value;
            gateValve.Open = isOpen;
            if (vacuumSchematic.SetComponentOpen(gateValve.ID, isOpen))
                vacuumSchematic.RecalculateFlow();
            _valveCache[gateValve.ID] = isOpen;
            DispatchValves();
        }
        if (msg.Type == MsgType.Log)
            DisplayLogMessage(msg);
    }

    private void FindValveControls(ThreadMsg msg)
    {
        Control? control = FindCachedControl(DeviceType.Valve, msg.Device);
        if (control is ValveControl valve)
            ExecuteOnUIThread(() => UpdateValveControl(msg, valve));
        else if (control is GateValveControl gateValve)
            ExecuteOnUIThread(() => UpdateGateValveControl(msg, gateValve));
    }

    private void FindMFC(ThreadMsg msg)
    {
        if (FindCachedControl(DeviceType.MFC, msg.Device) is MFCControl mfc)
        {
            if (msg.Type == MsgType.Value)
            {
                float flow = ((ValMsg<float>)msg).Value;
                ExecuteOnUIThread(() =>
                {
                    mfc.UpdateState(flow);
                    if (vacuumSchematic.SetComponentOpen(mfc.ID, flow > 0f))
                        vacuumSchematic.RecalculateFlow();
                    _telemetryCollector?.Push(msg.Device, flow, ClassifyReading(msg.Device, flow));
                });
            }
        }
        if (msg.Type == MsgType.Status && _telemetryCollector != null)
        {
            bool connected = ((ValMsg<HWStatus>)msg).Value == HWStatus.Connected;
            if (!connected) ExecuteOnUIThread(() => _telemetryCollector.MarkOffline(msg.Device));
        }
    }

    private void FindSensor(ThreadMsg msg)
    {
        if (FindCachedControl(DeviceType.Sensor, msg.Device) is LEDPanel panel)
        {
            if (msg.Type == MsgType.Value)
                ExecuteOnUIThread(() => panel.UpdateState(((ValMsg<float>)msg).Value));
        }
        if (msg.Type == MsgType.Value)
        {
            float value = ((ValMsg<float>)msg).Value;
            ExecuteOnUIThread(() =>
            {
                UpdateGraph(msg.Device, value);
                vacuumSchematic.UpdateSensorPressure(msg.Device, value);
                _telemetryCollector?.Push(msg.Device, value, ClassifyReading(msg.Device, value));
            });
        }
        if (msg.Type == MsgType.Status && _telemetryCollector != null)
        {
            bool connected = ((ValMsg<HWStatus>)msg).Value == HWStatus.Connected;
            if (!connected) ExecuteOnUIThread(() => _telemetryCollector.MarkOffline(msg.Device));
        }
    }

    private void UpdateGraph(string sensor, float value)
    {
        if (graphs.TryGetValue(sensor, out SensorGraph? graph))
        {
            graph.Add(value);
        }
    }

    private void FindPump(ThreadMsg msg)
    {
        Control? control = FindCachedControl(DeviceType.Pump, msg.Device);
        if (control is TurboPumpControl pump)
        {
            if (msg.Type == MsgType.Status)
            {
            };
            if (msg.Type == MsgType.Value)
            {
                ExecuteOnUIThread(() =>
                {
                    pump.SetState(((ValMsg<bool[]>)msg).Value);
                    if (vacuumSchematic.SetComponentOpen(pump.ID, pump.IsRunning))
                        vacuumSchematic.RecalculateFlow();
                });
            }
        };
        if (control is PumpControl pump1)
        {
            if (msg.Type == MsgType.Status)
            {
            };
            if (msg.Type == MsgType.Value)
            {
                bool[] val = ((ValMsg<bool[]>)msg).Value;
                if (val.Length == 0) return;   // cold/empty status — nothing to render
                ExecuteOnUIThread(() =>
                {
                    pump1.Power = val[0];
                    if (vacuumSchematic.SetComponentOpen(pump1.ID, val[0]))
                        vacuumSchematic.RecalculateFlow();
                });
            }
        }
        if (control is ScrollPumpControl spump)
        {
            if (msg.Type == MsgType.Status)
            {
            };
            if (msg.Type == MsgType.Value)
            {
                bool[] val = ((ValMsg<bool[]>)msg).Value;
                if (val.Length == 0) return;   // cold/empty status — nothing to render
                ExecuteOnUIThread(() =>
                {
                    spump.Power = val[0];
                    if (vacuumSchematic.SetComponentOpen(spump.ID, val[0]))
                        vacuumSchematic.RecalculateFlow();
                });
            }
        }
    }

    private void FindStage(ThreadMsg msg)
    {
        if (FindCachedControl(DeviceType.Stage, msg.Device) is StageControl stage)
        {
            if (msg.Type == MsgType.Status)
            {
            };
            if (msg.Type == MsgType.Value)
                ExecuteOnUIThread(() => stage.StageHeight = ((ValMsg<float>)msg).Value);
        }
    }

    private void FindController(ThreadMsg msg)
    {
        // Update custom panel (on tab pages, not on vacuumSchematic)
        if (FindCustomPanel(msg.Device) is IBaseCustomPanel panel)
        {
            if (msg.Type == MsgType.Value)
            {
                ParamMsg paramMsg = (ParamMsg)msg;
                ExecuteOnUIThread(() => panel.Update(paramMsg.Args));
            }
        }

        if (msg.Type == MsgType.Status && _telemetryCollector != null)
        {
            bool connected = ((ValMsg<HWStatus>)msg).Value == HWStatus.Connected;
            if (!connected) ExecuteOnUIThread(() => _telemetryCollector.MarkOfflineByDevice(msg.Device));
        }

        if (msg.Type != MsgType.Value) return;
        ParamMsg pMsg = (ParamMsg)msg;

        // Process panel: throttle pressure (mTorr) and valve position (%) under
        // <device>_P / <device>_Pos. Shape-matched, so the PLC throttle's
        // [position, setpoint] readback below contributes nothing here.
        if (_telemetryCollector != null)
        {
            var samples = PsuTelemetryMapper.MapThrottleController(msg.Device ?? string.Empty, pMsg.Args);
            if (samples.Count > 0)
                ExecuteOnUIThread(() =>
                {
                    foreach (var s in samples) _telemetryCollector.Push(s.Id, s.Value, s.State);
                });
        }

        // Serial throttle controller: hardware ID differs from schematic widget ID (hard-coded "TV").
        // Measure shape: ["Data", pressure, position, ..].
        if (pMsg.Args is ["Data", float serialPressure, float serialPosition, ..]
            && (serialPressure > 0 || serialPosition > 0))
        {
            if (FindCachedControl(DeviceType.Valve, "TV") is ThrottleValveControl tv)
            {
                ExecuteOnUIThread(() =>
                {
                    tv.Position = serialPosition;
                    if (vacuumSchematic.SetComponentOpen(tv.ID, serialPosition > 5f))
                        vacuumSchematic.RecalculateFlow();
                });
            }
            return;
        }

        // S7200ThrottleValve (PLC): schematic widget ID matches hardware ID (e.g. C1_TV).
        // Measure shape: [position, lastSetpoint].
        if (pMsg.Args is [float plcPosition, float])
        {
            if (FindCachedControl(DeviceType.Valve, msg.Device ?? "") is ThrottleValveControl plcTv)
            {
                ExecuteOnUIThread(() =>
                {
                    plcTv.Position = plcPosition;
                    if (vacuumSchematic.SetComponentOpen(plcTv.ID, plcPosition > 5f))
                        vacuumSchematic.RecalculateFlow();
                });
            }
        }
    }

    private void FindDrive(ThreadMsg msg)
    {
        if (msg.Type != MsgType.Value) return;
        ParamMsg paramMsg = (ParamMsg)msg;

        // Build source info as simple arrays (panel DLL can't reference Source type).
        // Each flange is labelled with its effective source handle — name, else material.
        object[] sourceInfo = DriveSourceInfoBuilder.Build(sourceManager);

        // Guard cold/short Args before indexing [0..2] (drive producer should send
        // 4, but never crash the UI thread inside a BeginInvoke if it doesn't).
        if (paramMsg.Args.Length < 3) return;

        if (FindCustomPanel("StepperDrives") is IBaseCustomPanel panel)
        {
            // [5] = status register (22 moving / 51 dwelling / 52 returning / 99 done)
            // so the panel can label a deposit dwell "Dwell" instead of "Moving".
            object register = paramMsg.Args.Length > 3 ? paramMsg.Args[3] : 0;
            ExecuteOnUIThread(() => panel.Update([
                msg.Device ?? "",
                paramMsg.Args[0],
                paramMsg.Args[1],
                paramMsg.Args[2],
                sourceInfo,
                register
            ]));
        }

        if (FindCustomPanel("VacScheme") is IBaseCustomPanel vacPanel)
        {
            ExecuteOnUIThread(() => vacPanel.Update([
                msg.Device ?? "",
                paramMsg.Args[0],
                paramMsg.Args[1]
            ]));
        }

        string driveId = msg.Device ?? "";
        float pos = Convert.ToSingle(paramMsg.Args[0]);
        bool moving = Convert.ToBoolean(paramMsg.Args[1]);
        bool homed = Convert.ToBoolean(paramMsg.Args[2]);
        int reg = paramMsg.Args.Length > 3 ? Convert.ToInt32(paramMsg.Args[3]) : 0;
        int flange = StepperDriveFlange(pos);
        _driveCache[driveId] = (pos, moving, homed, reg);

        var pp = FindProcessPanel();
        if (pp != null)
        {
            var drives = _driveCache.Select(kvp =>
                new Contracts.DriveInfo(kvp.Key, kvp.Value.pos, kvp.Value.moving, kvp.Value.homed,
                    StepperDriveFlange(kvp.Value.pos), kvp.Value.reg)
            ).ToArray();
            ExecuteOnUIThread(() => pp.UpdateDrives(drives));
        }
    }

    private void FindPower(ThreadMsg msg)
    {
        if (FindCustomPanel(msg.Device) is IBaseCustomPanel panel)
        {
            if (msg.Type == MsgType.Value)
            {
                ParamMsg paramMsg = (ParamMsg)msg;
                ExecuteOnUIThread(() => panel.Update(paramMsg.Args));
            }
        }

        // Telemetry: every supply publishes <id>_V / _I / _W (single-channel
        // [Voltage, Current, Power, IsOutputOn, ..]) or <id>_V1/_I1/_V2/_I2
        // (a multi-channel analyser's per-channel readback); the shape decides — see
        // PsuTelemetryMapper. The panel's spec list picks which ids it draws.
        if (msg.Type == MsgType.Value && _telemetryCollector != null)
        {
            var samples = PsuTelemetryMapper.MapPowerSupply(
                msg.Device ?? string.Empty, ((ParamMsg)msg).Args, ClassifyReading);
            if (samples.Count > 0)
                ExecuteOnUIThread(() =>
                {
                    foreach (var s in samples) _telemetryCollector.Push(s.Id, s.Value, s.State);
                });
        }
        // Drive the magnetron source glow in the Vacuum scheme from live power.
        // The widget is matched via the Sources.xml linkage: PSU device id ->
        // magnetron-instance label -> widget named "POW_<label>". A PSU not in the
        // map (unmapped/ambiguous) doesn't glow. Single-channel supplies report
        // [V, I, Power, IsOutputOn]; a multi-channel readback doesn't fit that
        // shape, so it is skipped (Args[2] would be a channel-2 voltage, not watts).
        if (msg.Type == MsgType.Value
            && _psuToMagnetronLabel.TryGetValue(msg.Device, out string? magLabel))
        {
            ParamMsg pm = (ParamMsg)msg;
            if (PsuTelemetryMapper.TryGetSingleChannelWatts(pm.Args, out float powerW))
            {
                bool energized = powerW > APS.SystemControl.Core.Core.MinDepositionWatts;
                ExecuteOnUIThread(() =>
                {
                    if (FindCachedControl(DeviceType.Power, magLabel) is MagnetronControl mag)
                        mag.Energized = energized;
                });
            }
        }
        if (msg.Type == MsgType.Status)
        {
            bool connected = ((ValMsg<HWStatus>)msg).Value == HWStatus.Connected;
            if (!connected)
            {
                _psuToMagnetronLabel.TryGetValue(msg.Device, out string? offLabel);
                ExecuteOnUIThread(() =>
                {
                    _telemetryCollector?.MarkOfflineByDevice(msg.Device);
                    if (offLabel != null && FindCachedControl(DeviceType.Power, offLabel) is MagnetronControl mag)
                        mag.Energized = false;
                });
            }
        }
    }

    private IProcessPanel? _processPanel;
    private volatile bool _processPanelSearched;
    private readonly object _processPanelLock = new();
    private readonly ConcurrentDictionary<string, (float pos, bool moving, bool homed, int reg)> _driveCache = new();
    private readonly ConcurrentDictionary<string, bool> _valveCache = new(StringComparer.Ordinal);

    private ProcessTelemetryCollector? _telemetryCollector;
    private System.Windows.Forms.Timer? _telemetryTimer;

    private readonly HashSet<string> _connectedDevices = new();
    private LedControl? _powerLed;
    private LedControl? _onlineLed;
    private int _totalDeviceCount;

    private void UpdateSystemLeds(ThreadMsg msg)
    {
        if (msg.Device == null) return;
        var status = ((ValMsg<HWStatus>)msg).Value;
        string key = $"{msg.Target}_{msg.Device}";

        if (status == HWStatus.Connected)
            _connectedDevices.Add(key);
        else
            _connectedDevices.Remove(key);

        if (_totalDeviceCount < _connectedDevices.Count)
            _totalDeviceCount = _connectedDevices.Count;

        bool power = _connectedDevices.Count > 0;
        bool online = _totalDeviceCount > 0 && _connectedDevices.Count >= _totalDeviceCount;

        _powerLed ??= FindLedControl("POWER");
        _onlineLed ??= FindLedControl("ONLINE");

        if (_powerLed != null) ExecuteOnUIThread(() => _powerLed.On = power);
        if (_onlineLed != null) ExecuteOnUIThread(() => _onlineLed.On = online);
    }

    private LedControl? FindLedControl(string id)
    {
        string name = GUIConst.ControlName(DeviceType.System, id);
        Control[] controls = vacuumSchematic.Controls.Find(name, true);
        return controls.Length > 0 ? controls[0] as LedControl : null;
    }

    private IBaseCustomPanel? FindCustomPanel(string deviceId)
    {
        string cacheKey = $"PANEL_{deviceId}";
        if (controlCache.TryGetValue(cacheKey, out Control? cached))
            return cached as IBaseCustomPanel;

        foreach (Control ctrl in tabSystem.Controls)
        {
            if (ctrl is IBaseCustomPanel panel && panel.ID == deviceId)
            {
                controlCache[cacheKey] = ctrl;
                return panel;
            }
        }
        return null;
    }

    private IProcessPanel? FindProcessPanel()
    {
        // Fast path (volatile read publishes the _processPanel written below).
        if (_processPanelSearched) return _processPanel;

        // Lock the check-then-set + one-time init so the GUI dispatcher thread and
        // the UI thread (DispatchValves, ButtonLoadRecipe_Click, toolbar) cannot
        // both run InitTelemetry/ApplyProcessPanelLayout, or miss/duplicate it. (audit M-7)
        lock (_processPanelLock)
        {
            if (_processPanelSearched) return _processPanel;

            IProcessPanel? found = null;
            var tabProcess = FindControlByName("tabProcess");
            if (tabProcess != null)
            {
                foreach (Control ctrl in tabProcess.Controls)
                {
                    if (ctrl is IProcessPanel pp) { found = pp; break; }
                    // Also check children (panel might be inside a TableLayoutPanel)
                    foreach (Control child in ctrl.Controls)
                        if (child is IProcessPanel pp2) { found = pp2; break; }
                    if (found != null) break;
                }
            }

            if (found != null)
            {
                _processPanel = found;
                InitTelemetry();
                ApplyProcessPanelLayout();
                PushSourcesToProcessPanel(found);
            }
            // Publish last (volatile write) so the fast path sees a consistent
            // _processPanel. Latch unconditionally to preserve the original
            // search-once semantics (avoid re-enumerating every dispatch tick).
            _processPanelSearched = true;
            return _processPanel;
        }
    }

    /// <summary>
    /// Runs once, on the first successful <see cref="FindProcessPanel"/> hit: materials
    /// don't change during a recipe, so the flange → material map is applied once at idle.
    /// </summary>
    private void PushSourcesToProcessPanel(IProcessPanel pp)
    {
        string? configDir = systemThread?.Core?.ConfigDir;
        if (string.IsNullOrEmpty(configDir)) return;
        var map = SourcesMapLoader.LoadFlangeSourceMap(configDir);
        if (map.Count == 0) return;
        ExecuteOnUIThread(() => pp.UpdateSources(map));
        ApplyPsuPanelVisibility();
    }

    /// <summary>
    /// Computes which magnetron-source PSU panels should be visible (master toggle off
    /// ⇒ all visible) and applies it to the System-tab instrument panels and the Process
    /// readings; then column-compacts when auto-align is on. Idempotent; safe to call
    /// before the form is shown (compaction self-guards on parent size).
    /// </summary>
    private void ApplyPsuPanelVisibility()
    {
        string? configDir = systemThread?.Core?.ConfigDir;
        if (string.IsNullOrEmpty(configDir)) return;

        var sources = APS.SystemControl.Core.Classes.SourcesMapLoader.LoadPowerSources(configDir);
        if (sources.Count == 0) return;   // no magnetron-source PSUs declared

        bool hideInactive = hideInactiveSuppliesToolStripMenuItem?.Checked ?? true;
        bool autoAlign = autoAlignOnHideToolStripMenuItem?.Checked ?? true;
        IReadOnlyDictionary<string, bool> vis;
        if (hideInactive)
        {
            var map = APS.SystemControl.Core.Classes.SourcesMapLoader.LoadFlangeSourceMap(configDir);
            vis = APS.SystemControl.Core.Classes.PsuPanelVisibilityPolicy.Compute(map, sources);
        }
        else
        {
            vis = sources.ToDictionary(p => p, _ => true, StringComparer.OrdinalIgnoreCase);
        }

        ExecuteOnUIThread(() =>
        {
            foreach (var (id, visible) in vis)
                if (FindCustomPanel(id) is Control c)
                    c.Visible = visible;

            _processPanel?.SetPowerSupplyVisibility(vis);

            if (autoAlign)
                _panelDragManager?.CompactColumns(persist: true);
        });
    }

    private void DispatchValves()
    {
        var pp = FindProcessPanel();
        if (pp == null) return;
        var snapshot = _valveCache
            .Select(kvp => new Contracts.ValveInfo(kvp.Key, kvp.Value))
            .ToArray();
        ExecuteOnUIThread(() => pp.UpdateValves(snapshot));
    }

    private void InitTelemetry()
    {
        if (_telemetryCollector != null) return;

        // Must run on the UI thread — the Timer's hidden window attaches to the
        // calling thread's message pump. FindProcessPanel is called from the
        // background GUI dispatcher, so marshal before touching WinForms state.
        if (this.InvokeRequired)
        {
            this.BeginInvoke((Action)InitTelemetry);
            return;
        }

        // The panel's DefineReadings() is the single source of the ids the
        // collector caches — anything a channel handler pushes under another
        // id is dropped at the collector.
        _telemetryCollector = new ProcessTelemetryCollector();
        if (_processPanel != null)
            _telemetryCollector.RegisterMany(_processPanel.ReadingIds);

        // Core change-gates poll values, so a readback that has not changed
        // since its first (pre-panel) message never arrives here — prime the
        // cache from the devices' own caches. See TelemetrySeeder.
        if (systemThread?.Core is { } core)
            TelemetrySeeder.Seed(core.AllDevices(), _telemetryCollector, ClassifyReading);

        _telemetryTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _telemetryTimer.Tick += (_, _) =>
        {
            if (_processPanel == null || _telemetryCollector == null) return;
            var snapshot = _telemetryCollector.Snapshot();
            _processPanel.UpdateReadings(snapshot);

            // Refresh the current-step elapsed counter so long dwells (deposit,
            // warmup, pwait) tick every 250 ms instead of waiting for the next
            // step-boundary Progress message. When the Worker has reported a
            // bounded dwell window via StepProgress, draw the progress bar
            // over exactly that window (e.g. a 15 s deposition dwell).
            if (_lastRecipeInfo is { State: APS.ProcessPanel.Contracts.RecipeState.Running } info)
            {
                double stepElapsed = (DateTime.UtcNow - _stepStartTimeUtc).TotalSeconds;
                string desc = _stepDescriptionOverride ?? info.StepDescription;
                _processPanel.UpdateStep(new APS.ProcessPanel.Contracts.ProcessStepInfo(
                    StepType: desc,
                    Material: null,
                    ElapsedSeconds: stepElapsed,
                    TotalSeconds: _stepTotalSeconds,
                    Parameters: new Dictionary<string, APS.ProcessPanel.Contracts.ProcessParameter>()));
            }
        };
        _telemetryTimer.Start();

        // Stop the timer before the form closes so the Tick closure doesn't
        // fire against a disposed _processPanel.
        this.FormClosing += (_, e) =>
        {
            // Don't tear down telemetry if the exit was cancelled (operator chose
            // No) — there is no restart path and all process-panel readings would
            // freeze for the rest of the session.
            if (e.Cancel) return;
            _telemetryTimer?.Stop();
            _telemetryTimer?.Dispose();
            _telemetryTimer = null;
        };
    }

    /// <summary>
    /// Map a raw reading value to a ReadingState. v1 uses hardcoded per-id
    /// thresholds — this can move to XML config later (see spec Open Questions).
    /// </summary>
    private static ReadingState ClassifyReading(string id, double value) =>
        id switch
        {
            "pm" when value > 1e-1 => ReadingState.Alarm,
            "pm" when value > 1e-2 => ReadingState.Warning,
            "pl" when value > 5e-2 => ReadingState.Warning,
            _ => ReadingState.Ok,
        };

    private static readonly float[] _flangeStepPositions = [0, 4024, 8048, 12072, 16096, 20120];

    private static int StepperDriveFlange(float position)
    {
        float minDist = float.MaxValue;
        int nearest = -1;
        for (int i = 0; i < _flangeStepPositions.Length; i++)
        {
            float dist = Math.Abs(position - _flangeStepPositions[i]);
            if (dist < minDist) { minDist = dist; nearest = i + 1; }
        }
        return minDist <= 2100f ? nearest : -1;
    }
}
