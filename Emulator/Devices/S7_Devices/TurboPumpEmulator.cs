using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

public class TurboPumpEmulator : IDeviceEmulator, ISnapshotable
{
    private readonly int _dbAddress;
    private readonly int _writeAddr;
    private readonly int _writeIndex;
    private readonly float _fullSpeed;
    private readonly float _spinUpTime;
    private readonly float _crossoverPressure;
    private readonly string _chamberId;
    private readonly string _gateValveId;
    private readonly string _forelineValveId;
    private readonly string _host;
    private readonly float _forelineVolume;
    private readonly float _forelineLimit;
    private PumpEmulator? _backingPump;
    private bool _commanded;
    private float _speedFraction;
    private bool _wasAtSpeed;
    private bool _wasRunning;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public DeviceCategory Category => DeviceCategory.Pump;
    public bool IsRunning => _speedFraction > 0.01f;
    public bool IsAtSpeed => _speedFraction > 0.99f;
    public float SpeedFraction => _speedFraction;

    public string ChamberId => _chamberId;
    public string GateValveId => _gateValveId;
    public string ForelineValveId => _forelineValveId;
    public string BackingPumpId { get; }
    public float ForelinePressure { get; private set; } = 760f;

    public TurboPumpEmulator(string id, int dbAddress, int writeAddr, int writeIndex, float fullSpeed, string description,
        string chamberId, string gateValveId, string forelineValveId,
        float spinUpTime = 30f, float crossoverPressure = 0.5f, string host = "",
        string backingPumpId = "", float forelineVolume = 2f, float forelineLimit = 5f)
    {
        Id = id;
        _dbAddress = dbAddress;
        _writeAddr = writeAddr;
        _writeIndex = writeIndex;
        _fullSpeed = fullSpeed;
        _spinUpTime = spinUpTime;
        _crossoverPressure = crossoverPressure;
        _chamberId = chamberId ?? throw new ArgumentNullException(nameof(chamberId));
        _gateValveId = gateValveId ?? "";
        _forelineValveId = forelineValveId ?? "";
        Description = description;
        _host = host;
        if (!float.IsFinite(forelineVolume) || forelineVolume <= 0f ||
            !float.IsFinite(forelineLimit) || forelineLimit <= 0f)
            throw new ArgumentOutOfRangeException(nameof(forelineVolume), "Foreline volume and pressure limit must be positive and finite.");
        BackingPumpId = backingPumpId;
        _forelineVolume = forelineVolume;
        _forelineLimit = forelineLimit;
    }

    public void Command(bool enable) => _commanded = enable;

    public void BindBackingPump(PumpEmulator pump)
    {
        if (!pump.Id.Equals(BackingPumpId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Turbo '{Id}' requires backing pump '{BackingPumpId}', not '{pump.Id}'.");
        _backingPump = pump;
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            // ONE byte at _dbAddress, matching the real machine: the firmware packs
            // the whole turbo status into VB17 and the controller reads that single
            // byte (S7200TurboPump.ReadStatus). On real hardware VB18 holds the
            // auto-load/unload flags.
            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = Id,
                Address = _dbAddress,
                Length = 1,
                OnRead = () =>
                {
                    byte status = 0;
                    if (IsRunning) status |= (1 << 1);      // bit 1: running
                    if (_speedFraction > 0.01f && !IsAtSpeed) status |= (1 << 2); // bit 2: accelerating
                    if (IsAtSpeed) status |= (1 << 3);      // bit 3: at normal speed
                    if (IsRunning) status |= (1 << 4);      // bit 4: rotation detected
                    // bit 5: alarm — not set during normal operation

                    return [status];
                },
                OnWrite = _ => { },
            });

            s7.RegisterMbBit(new MbBitRegistration
            {
                DeviceId = Id,
                Address = _writeAddr,
                BitIndex = _writeIndex,
                OnRead = () => _commanded,
                OnWrite = v =>
                {
                    if (v != _commanded)
                        EmulatorLog.Log(LogLevel.Info, $"Turbo:{Id}", v ? "Commanded ON" : "Commanded OFF");
                    _commanded = v;
                },
            });
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        if (_commanded)
        {
            _speedFraction += deltaTime / _spinUpTime;
            if (_speedFraction > 1.0f) _speedFraction = 1.0f;
        }
        else
        {
            _speedFraction -= deltaTime / (_spinUpTime * 0.5f);
            if (_speedFraction < 0f) _speedFraction = 0f;
        }

        // Pipeline: Chamber → gate valve → TP → foreline → backing pump.
        // Turbo contributes pumping speed only when BOTH gate AND foreline are open.
        bool gateOpen = string.IsNullOrEmpty(_gateValveId)
                     || state.ValveStates.GetValueOrDefault(_gateValveId);
        bool forelineOpen = string.IsNullOrEmpty(_forelineValveId)
                         || state.ValveStates.GetValueOrDefault(_forelineValveId);

        if (!state.Chambers.TryGetValue(_chamberId, out var chamber))
            throw new KeyNotFoundException(
                $"Turbo pump '{Id}' references unknown chamber '{_chamberId}'");

        float pressure = chamber.Pressure;
        // Smooth pumping-speed-vs-inlet-pressure curve. Asymptotes to full
        // nameplate speed as p → 0 and to 0 as p grows large; no hard cliff.
        float crossoverFactor = 1f / (1f + pressure / _crossoverPressure);
        float turboSpeed = _fullSpeed * _speedFraction * crossoverFactor;
        if (BackingPumpId.Length > 0)
        {
            if (_backingPump == null)
                throw new InvalidOperationException($"Turbo '{Id}' has no bound backing pump '{BackingPumpId}'.");
            float throttle = chamber.ThrottlePosition / 100f;
            float throughput = gateOpen && forelineOpen && ForelinePressure < _forelineLimit
                ? turboSpeed * throttle * throttle * MathF.Max(0f, pressure - chamber.BasePressure) : 0f;
            float backingSpeed = forelineOpen && _backingPump.IsRunning ? _backingPump.NominalSpeed : 0f;
            if (backingSpeed > 0f)
            {
                float target = _backingPump.UltimatePressure + throughput / backingSpeed;
                ForelinePressure += (target - ForelinePressure) *
                    (1f - MathF.Exp(-backingSpeed * deltaTime / _forelineVolume));
            }
            else
                ForelinePressure += throughput * deltaTime / _forelineVolume;
            if (ForelinePressure >= _forelineLimit)
                turboSpeed = 0f;
        }

        if (gateOpen && forelineOpen)
            chamber.PumpSpeed += turboSpeed;

        if (IsAtSpeed && !_wasAtSpeed)
            EmulatorLog.Log(LogLevel.Info, $"Turbo:{Id}", "Reached full speed");
        if (!IsRunning && _wasRunning)
            EmulatorLog.Log(LogLevel.Info, $"Turbo:{Id}", "Stopped");
        if (_commanded && !_wasRunning && IsRunning)
            EmulatorLog.Log(LogLevel.Info, $"Turbo:{Id}", "Spinning up");
        _wasAtSpeed = IsAtSpeed;
        _wasRunning = IsRunning;
    }

    public void Reset()
    {
        _commanded = false;
        _speedFraction = 0f;
        _wasAtSpeed = false;
        _wasRunning = false;
        ForelinePressure = 760f;
    }

    public JsonObject GetSnapshot()
    {
        var snapshot = new JsonObject
        {
            ["commanded"] = _commanded,
            ["speedFraction"] = _speedFraction,
            ["wasAtSpeed"] = _wasAtSpeed,
            ["wasRunning"] = _wasRunning,
        };
        if (BackingPumpId.Length > 0)
            snapshot["forelinePressure"] = ForelinePressure;
        return snapshot;
    }

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _commanded = snapshot["commanded"]?.GetValue<bool>() ?? _commanded;
        _speedFraction = snapshot["speedFraction"]?.GetValue<float>() ?? _speedFraction;
        _wasAtSpeed = snapshot["wasAtSpeed"]?.GetValue<bool>() ?? _wasAtSpeed;
        _wasRunning = snapshot["wasRunning"]?.GetValue<bool>() ?? _wasRunning;
        ForelinePressure = snapshot["forelinePressure"]?.GetValue<float>() ?? ForelinePressure;
    }

    public object[] GetStatus() => [Id, IsRunning, IsAtSpeed, _speedFraction, Description];
}
