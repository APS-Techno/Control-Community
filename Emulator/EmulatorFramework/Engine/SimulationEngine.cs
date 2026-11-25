using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;
using APS.Emulator.Devices.S7;

namespace APS.SystemEmulator.Engine;

public class SimulationEngine : IDisposable
{
    private readonly List<IDeviceEmulator> _devices = [];
    private readonly List<IProtocolServer> _servers = [];
    private readonly SimulationState _state;
    private readonly TimeManager _time = new();
    private Thread? _simThread;
    private volatile bool _running;
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private bool _disposed;
    private volatile bool _paused;
    private readonly Dictionary<string, float> _lastLoggedPressure
        = new(StringComparer.OrdinalIgnoreCase);
    private List<SourceEntry> _sources = [];
    private string? _sourcesPath;
    private IReadOnlyList<MagnetronConfig>? _cachedMagnetrons;
    private readonly PhysicsConfig _physicsConfig;
    private readonly Dictionary<(string Chamber, int Flange), float> _theta = new();

    public int TickIntervalMs { get; set; } = 100;

    // How long the loop blocks while paused before re-checking _paused / the stop signal.
    private const int PausedPollMs = 20;
    public SimulationState State => _state;
    public TimeManager Time => _time;
    public IReadOnlyList<IDeviceEmulator> Devices => _devices;
    public IReadOnlyList<IProtocolServer> Servers => _servers;
    public event Action<SimulationState>? StateChanged;

    public SimulationEngine(PhysicsConfig? physics = null)
    {
        _physicsConfig = physics ?? new PhysicsConfig();
        _state = new SimulationState
        {
            Physics = new VacuumPhysicsModel(
                mfcResponseTau: _physicsConfig.MfcResponseTau,
                substrateHeatingCoeff: _physicsConfig.SubstrateHeatingCoeff,
                substrateCoolingCoeff: _physicsConfig.SubstrateCoolingCoeff,
                substrateRadiativeCoeff: _physicsConfig.SubstrateRadiativeCoeff,
                chamberHeatingCoeff: _physicsConfig.ChamberHeatingCoeff,
                chamberCoolingCoeff: _physicsConfig.ChamberCoolingCoeff),
            DataSource = new NullDataSource(),
        };

        // Seed Chambers from the passed-in physics config so tests that construct
        // a SimulationEngine directly (without going through ProjectLifecycleController)
        // see their declared chambers in state.Chambers. Production reload via
        // ProjectLifecycleController calls ClearChambers() before re-populating, so
        // there's no double-seeding hazard.
        foreach (var ch in _physicsConfig.Chambers)
        {
            _state.SetChamber(new ChamberState
            {
                Id = ch.Id,
                Type = ch.Type,
                Pressure = ch.InitialPressure,
                Volume = ch.Volume,
                BasePressure = ch.BasePressure,
                BaseLeakRate = ch.LeakRate,
                LeakRate = ch.LeakRate,
            });
        }
    }

    public void SetDataSource(IDataSource source) => _state.SetDataSource(source);
    public void AddDevice(IDeviceEmulator device) => _devices.Add(device);
    public void AddServer(IProtocolServer server) => _servers.Add(server);

    /// <summary>
    /// Load and resolve magnetron sources from Sources.xml. Safe to call multiple times
    /// (e.g. on Reset) — each call replaces the current source list.
    /// </summary>
    public void LoadSources(string sourcesXmlPath, IReadOnlyList<MagnetronConfig> magnetrons)
    {
        _sourcesPath = sourcesXmlPath;
        _cachedMagnetrons = magnetrons;
        _sources = SourcesLoader.Load(sourcesXmlPath, _devices, magnetrons);
    }

    public void RegisterDevicesWithServers()
    {
        foreach (var device in _devices)
        {
            foreach (var server in _servers)
            {
                // For S7-protocol servers, filter by host/PlcId match to prevent the
                // cartesian-product mirroring bug in multi-PLC configs like Cluster_Demo.
                //
                // Rules:
                //   • Device has a non-empty Host and server has a non-empty PlcId:
                //     register only when Host == PlcId (case-insensitive).
                //   • Device has a non-empty Host but server PlcId is "" (legacy single-
                //     PLC server without explicit id): skip — don't attach named-host
                //     devices to an unidentified server.
                //   • Device has empty Host and server PlcId is "": single-PLC legacy
                //     path — allow registration so backward-compat configs continue to work.
                //   • Device has empty Host but server has a non-empty PlcId: skip —
                //     a device with no host assignment must not broadcast to all PLCs.
                //
                // Non-S7 servers are never gated here; devices match them by name/type
                // inside their own RegisterProtocol implementations.
                if (server is IS7ProtocolServer s7)
                {
                    bool deviceHasHost = !string.IsNullOrEmpty(device.Host);
                    bool serverHasPlcId = !string.IsNullOrEmpty(s7.PlcId);

                    if (deviceHasHost && serverHasPlcId)
                    {
                        if (!device.Host.Equals(s7.PlcId, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    else if (deviceHasHost && !serverHasPlcId)
                    {
                        continue;
                    }
                    else if (!deviceHasHost && serverHasPlcId)
                    {
                        continue;
                    }
                    // else: both empty → single-PLC legacy path → fall through and register.
                }
                device.RegisterProtocol(server);
            }
        }
    }

    public void Start()
    {
        if (_simThread is { IsAlive: true }) return; // already running — ignore double-start (would spawn a 2nd tick thread)
        foreach (var server in _servers) server.Start();
        _time.Start();
        _running = true;
        _stopSignal.Reset();
        _simThread = new Thread(RunLoop) { IsBackground = true, Name = "EmulatorSim" };
        _simThread.Start();
        EmulatorLog.Log(LogLevel.Info, "Engine", "Simulation engine started");
    }

    // Dedicated simulation loop. One tick in flight at a time (no re-entrancy),
    // off the threadpool so async S7 I/O continuations are never starved.
    private void RunLoop()
    {
        var sw = new System.Diagnostics.Stopwatch();
        while (_running)
        {
            if (_paused)
            {
                // Idle-poll while paused; _stopSignal wakes us promptly on Stop().
                _stopSignal.Wait(PausedPollMs);
                continue;
            }

            sw.Restart();
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                // Backstop: a tick must never kill the sim thread. If it did, the
                // protocol servers would keep answering with stale buffers and
                // Control would see "connected but frozen" hardware.
                EmulatorLog.Log(LogLevel.Error, "Engine",
                    $"Unhandled exception in simulation tick; loop continues: {ex}");
            }
            sw.Stop();

            // A tick that overruns the interval yields sleep <= 0, so we skip the wait
            // and the while(_running) check exits the loop — the thread never touches
            // _stopSignal after an over-budget tick, so Dispose()'s signal disposal is safe.
            int sleep = TickIntervalMs - (int)sw.ElapsedMilliseconds;
            if (sleep > 0)
                _stopSignal.Wait(sleep); // returns early if Stop() signals
        }
    }

    public void RestartServers()
    {
        foreach (var server in _servers) server.Stop();
        foreach (var server in _servers) server.Start();
        EmulatorLog.Log(LogLevel.Info, "Engine", "All protocol servers restarted");
    }

    // Start/Stop/Pause/Resume/Dispose are expected to be called from a single (UI) thread.
    public void Stop()
    {
        _running = false;
        _stopSignal.Set();
        bool joined = _simThread?.Join(TimeSpan.FromSeconds(2)) ?? true;
        _simThread = null;
        _time.Stop();
        StopServersParallel();
        if (joined)
            EmulatorLog.Log(LogLevel.Info, "Engine", "Simulation engine stopped");
        else
            EmulatorLog.Log(LogLevel.Warning, "Engine",
                "Simulation engine stop: sim thread did not exit within 2s; abandoning join");
    }

    public void ResetSimulation()
    {
        bool wasPaused = _paused;
        Pause();

        _time.Reset();
        _time.SpeedMultiplier = 1.0f;

        foreach (var device in _devices)
            device.Reset();

        _state.SimTime = 0f;
        _state.FilmThickness = 0f;
        _theta.Clear();
        _state.DeltaTime = 0f;
        _state.ValveStates.Clear();

        // Reset all chambers. Initial pressure is restored on the next OpenProject;
        // for an in-session reset we use 760f as a neutral starting point.
        foreach (var c in _state.Chambers.Values)
        {
            c.Pressure = 760f;
            c.PumpSpeed = 0f;
            c.GasLoad = 0f;
            c.LeakRate = c.BaseLeakRate;
            c.ThrottlePosition = 100f;
        }

        _state.Thermal.Temperature = _state.Thermal.AmbientTemp;
        _state.Thermal.ChamberTemperature = _state.Thermal.AmbientTemp;
        _state.Thermal.HeatLoad = 0f;
        _state.Thermal.SubstrateHeatLoad = 0f;

        _lastLoggedPressure.Clear();

        // Rebuild source list so edits to Sources.xml take effect on reset
        if (_sourcesPath != null && _cachedMagnetrons != null)
            _sources = SourcesLoader.Load(_sourcesPath, _devices, _cachedMagnetrons);

        foreach (var server in _servers) server.Flush();

        EmulatorLog.Log(LogLevel.Info, "Engine", "Simulation reset to fresh state");

        if (!wasPaused)
            Resume();
    }

    public void RestoreSnapshot(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SnapshotService.Restore(snapshot, _state, _devices);
        _time.SetSimTime(_state.SimTime);
    }

    public void Pause()
    {
        _paused = true;
        _time.Stop();
        EmulatorLog.Log(LogLevel.Info, "Engine", "Simulation paused");
    }

    public void Resume()
    {
        _time.Start();
        _paused = false;
        EmulatorLog.Log(LogLevel.Info, "Engine", "Simulation resumed");
    }

    public void Tick()
    {
        if (_paused) return;
        float dt = _time.Advance(TickIntervalMs / 1000f);
        _state.SimTime = _time.SimTime;
        _state.DeltaTime = dt;

        foreach (var c in _state.Chambers.Values)
        {
            c.PumpSpeed = 0f;
            c.LeakRate = c.BaseLeakRate;
            c.GasLoad = 0f;
        }

        // Pumps + MFCs write into their chamber directly.
        // Per-device guard: one throwing device (e.g. a config typo or a command
        // edge case) is logged and skipped for this tick, never fatal to the loop.
        foreach (var device in _devices)
        {
            try
            {
                device.Update(dt, _state);
            }
            catch (Exception ex)
            {
                EmulatorLog.Log(LogLevel.Error, "Engine",
                    $"Device '{device.Id}' Update threw; skipping it this tick: {ex.Message}");
            }
        }

        // Magnetron source loop: gate discharges on pressure + output enable,
        // compute V/I/P per regulation mode, push back to PSUs, accumulate heat load.
        // Pressure sampled here is the end-of-previous-tick value (chamber physics
        // runs below). This gives a ~1 tick (~100 ms) delay between a pressure rise
        // and discharge ignition, which is a reasonable approximation of real strike
        // latency and avoids a two-pass physics evaluation.
        _state.Thermal.HeatLoad = 0f;
        _state.Thermal.SubstrateHeatLoad = 0f;
        float dischargeThreshold = _physicsConfig.DischargePressureThreshold;
        bool substrateExposed = IsSubstrateExposed();
        var deposition = _physicsConfig.Deposition;
        float reactiveFlow = deposition is null ? 0f : GetReactiveFlow(deposition);

        foreach (var src in _sources)
        {
            var psu = src.PowerSupply;
            float chamberPressure = _state.Chambers.TryGetValue(src.ChamberId, out var ch)
                ? ch.Pressure
                : float.MaxValue; // unknown chamber → never strikes

            float theta = 0f;
            if (deposition is not null)
            {
                var key = (src.ChamberId, src.Flange);
                bool dischargingNow = psu.OutputEnabled
                    && chamberPressure >= dischargeThreshold
                    && psu.RegulationMode == PowerRegulationMode.Current
                    && psu.CommandedCurrent > 0f;
                theta = StepPoisoning(deposition, _theta.GetValueOrDefault(key), reactiveFlow,
                    dischargingNow ? psu.CommandedCurrent : 0f, dt);
                _theta[key] = theta;
            }

            if (!psu.OutputEnabled)
            {
                psu.SetActualOutput(0f, 0f, 0f);
                continue;
            }

            if (chamberPressure < dischargeThreshold)
            {
                // Output enabled but pressure too low to strike a discharge.
                // Current-mode supplies rail to their open-circuit voltage with I=0, P=0;
                // Power-mode supplies fall back to "no readback" until we model reflected power.
                if (psu.RegulationMode == PowerRegulationMode.Current)
                    psu.SetActualOutput(psu.OpenCircuitVoltage, 0f, 0f);
                else
                    psu.SetActualOutput(0f, 0f, 0f);
                continue;
            }

            float v, i, p;
            if (psu.RegulationMode == PowerRegulationMode.Current)
            {
                v = src.MagnetronVoltage;
                i = psu.CommandedCurrent;
                if (deposition is not null && i > 0f)
                {
                    v += deposition.PoisonedVoltageShift * theta;
                    if (substrateExposed)
                        _state.FilmThickness += deposition.RatePerAmp * i
                            * (1f - theta * (1f - deposition.PoisonedRateFraction)) * dt;
                }
                p = v * i;
            }
            else // Power
            {
                v = 0f;
                i = 0f;
                p = psu.CommandedPower;
            }

            psu.SetActualOutput(v, i, p);

            // Total discharge power always heats the chamber walls — even with the shutter
            // closed, the plume hits chamber shielding or the shutter blade. Substrate heat
            // load is gated by the rotating shutter.
            _state.Thermal.HeatLoad += p;
            if (substrateExposed)
                _state.Thermal.SubstrateHeatLoad += p;
        }

        foreach (var vent in _state.VentMappings)
        {
            if (!_state.ValveStates.GetValueOrDefault(vent.ValveId)) continue;
            if (!_state.Chambers.TryGetValue(vent.Chamber, out var ch)) continue;
            if (ch.Pressure < 760f)
                ch.LeakRate += vent.Conductance * (760f - ch.Pressure);
        }

        foreach (var c in _state.Chambers.Values)
            c.Pressure = _state.Physics.ComputePressure(c, dt);

        _state.Thermal.Temperature = _state.Physics.ComputeTemperature(_state.Thermal, dt);
        _state.Thermal.ChamberTemperature = _state.Physics.ComputeChamberTemperature(_state.Thermal, dt);

        foreach (var (id, c) in _state.Chambers)
        {
            float last = _lastLoggedPressure.TryGetValue(id, out var v) ? v : float.MaxValue;
            CheckPressureMilestone(id, c.Pressure, ref last);
            _lastLoggedPressure[id] = last;
        }

        // Flush to protocols. Guard per-server so a single server fault doesn't
        // abort the tick after physics has already advanced.
        foreach (var server in _servers)
        {
            try
            {
                server.Flush();
            }
            catch (Exception ex)
            {
                EmulatorLog.Log(LogLevel.Error, "Engine",
                    $"Protocol server flush threw; skipping it this tick: {ex.Message}");
            }
        }

        StateChanged?.Invoke(_state);
    }

    private float GetReactiveFlow(DepositionConfig deposition)
    {
        var mfc = _devices.OfType<MFCEmulator>()
            .FirstOrDefault(m => m.Id.Equals(deposition.ReactiveMfc, StringComparison.OrdinalIgnoreCase));
        return mfc?.ActualFlow ?? 0f;
    }

    // Exact solution of dθ/dt = a(1-θ) - bθ for constant a, b over dt: unconditionally stable.
    private static float StepPoisoning(DepositionConfig d, float theta, float flowSccm, float dischargeAmps, float dt)
    {
        float a = d.PoisonRate * flowSccm;
        float k = a + d.CleanRate * dischargeAmps;
        if (k <= 0f) return theta;
        float steady = a / k;
        return Math.Clamp(steady + (theta - steady) * MathF.Exp(-k * dt), 0f, 1f);
    }

    private IShutterEmulator? _shutterDrive;
    private bool _shutterLookupDone;

    // Distance from the homed (zero) position above which the shutter is considered
    // to expose the substrate to a source. Tolerates small jitter around Home.
    private const float ShutterHomeToleranceSteps = 50f;

    private bool IsSubstrateExposed()
    {
        if (!_shutterLookupDone)
        {
            _shutterDrive = _devices.OfType<IShutterEmulator>()
                .FirstOrDefault(d => d.Id.Equals("Shutter", StringComparison.OrdinalIgnoreCase));
            _shutterLookupDone = true;
        }

        // No shutter installed -> assume the substrate is always exposed (preserves
        // behaviour on test rigs and systems without a rotating shutter).
        if (_shutterDrive is null)
            return true;

        // Before homing, the step counter is meaningless. Treat as closed so we don't
        // dump heat into the substrate based on a random power-on offset.
        if (!_shutterDrive.IsHomed)
            return false;

        return MathF.Abs(_shutterDrive.Position) > ShutterHomeToleranceSteps;
    }

    private static readonly float[] PressureThresholds = [100f, 10f, 1f, 0.1f, 0.01f, 1e-3f, 1e-4f, 1e-5f];

    private static void CheckPressureMilestone(string chamber, float pressure, ref float lastLogged)
    {
        foreach (float threshold in PressureThresholds)
        {
            if (lastLogged > threshold && pressure <= threshold)
            {
                EmulatorLog.Log(LogLevel.Info, "Physics", $"{chamber} chamber crossed {threshold:E1} Torr (pumpdown)");
                break;
            }
            if (lastLogged < threshold && pressure >= threshold)
            {
                EmulatorLog.Log(LogLevel.Info, "Physics", $"{chamber} chamber rose above {threshold:E1} Torr");
                break;
            }
        }
        lastLogged = pressure;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Stop();
        _stopSignal.Dispose();
        // Dispose servers concurrently for the same reason Stop() does (see
        // StopServersParallel). Stop() already null-checked/closed each port, so
        // these Dispose() calls are cheap, but we keep them off the serial path.
        Parallel.ForEach(_servers, server =>
        {
            try { server.Dispose(); }
            catch (Exception ex)
            {
                EmulatorLog.Log(LogLevel.Warning, "Engine",
                    $"Server '{server.Name}' Dispose threw during shutdown: {ex.Message}");
            }
        });
        sw.Stop();
        EmulatorLog.Log(LogLevel.Info, "Engine",
            $"Engine disposed in {sw.ElapsedMilliseconds} ms ({_servers.Count} servers)");
    }

    // Stop all protocol servers concurrently. They're independent objects with no
    // shared state, and SerialPort.Close() is slow — it signals and joins an internal
    // event-loop thread parked in an overlapped read. Closing N ports in series makes
    // shutdown the SUM of every close; in parallel it's the slowest single close.
    // A faulting port is logged and skipped, never blocking the others.
    private void StopServersParallel()
    {
        Parallel.ForEach(_servers, server =>
        {
            try { server.Stop(); }
            catch (Exception ex)
            {
                EmulatorLog.Log(LogLevel.Warning, "Engine",
                    $"Server '{server.Name}' Stop threw during shutdown: {ex.Message}");
            }
        });
    }
}
