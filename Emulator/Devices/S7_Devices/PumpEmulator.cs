using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

public class PumpEmulator : IDeviceEmulator, ISnapshotable
{
    private readonly int _mbAddress;
    private readonly int _bitIndex;
    private readonly float _pumpSpeed;
    private readonly float _ultimatePressure;
    private readonly string _chamberId;
    private readonly string _gateValveId;
    private readonly string _host;
    private bool _isRunning;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public string ChamberId => _chamberId;
    public string GateValveId => _gateValveId;
    public DeviceCategory Category => DeviceCategory.Pump;
    public bool IsRunning => _isRunning;
    public float NominalSpeed => _pumpSpeed;
    public float UltimatePressure => _ultimatePressure;

    public PumpEmulator(string id, int mbAddress, int bitIndex, float pumpSpeed, string description,
        string chamberId, string gateValveId,
        float ultimatePressure = 1e-3f, string host = "")
    {
        Id = id;
        _mbAddress = mbAddress;
        _bitIndex = bitIndex;
        _pumpSpeed = pumpSpeed;
        _ultimatePressure = ultimatePressure;
        _chamberId = chamberId ?? throw new ArgumentNullException(nameof(chamberId));
        _gateValveId = gateValveId ?? "";
        Description = description;
        _host = host;
    }

    public void SetRunning(bool running) => _isRunning = running;

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            s7.RegisterMbBit(new MbBitRegistration
            {
                DeviceId = Id,
                Address = _mbAddress,
                BitIndex = _bitIndex,
                OnRead = () => _isRunning,
                OnWrite = v =>
                {
                    if (v != _isRunning)
                        EmulatorLog.Log(LogLevel.Info, $"Pump:{Id}", v ? "Started" : "Stopped");
                    _isRunning = v;
                },
            });
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        if (!_isRunning) return;

        if (!state.Chambers.TryGetValue(_chamberId, out var chamber))
            throw new KeyNotFoundException(
                $"Pump '{Id}' references unknown chamber '{_chamberId}'");

        // Gate the pump's contribution: speed only reaches the chamber when its
        // isolation/roughing valve is open (e.g. TM_SP → TM via TM_RL, SP1 → Main
        // via PP, TP → Main via GVM). An empty gate id means "no isolation" — a
        // hardwired pump that always credits its chamber when running (e.g. the
        // loadlock backing pump SP2, whose gate= is left empty: GVL is a
        // chamber-to-chamber transfer gate, not an isolation valve for SP2).
        if (!string.IsNullOrEmpty(_gateValveId)
            && !state.ValveStates.GetValueOrDefault(_gateValveId))
            return;

        float pressure = chamber.Pressure;
        if (pressure > _ultimatePressure)
        {
            // Rotary vane / scroll pump: speed drops at low pressure, zero at ultimate.
            // S(P) = S_max * (P - P_ult) / (P + P_knee)
            const float P_knee = 0.1f; // Torr
            float effectiveSpeed = _pumpSpeed * (pressure - _ultimatePressure) / (pressure + P_knee);
            chamber.PumpSpeed += effectiveSpeed;
        }
    }

    public void Reset()
    {
        _isRunning = false;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["isRunning"] = _isRunning,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _isRunning = snapshot["isRunning"]?.GetValue<bool>() ?? _isRunning;
    }

    public object[] GetStatus() => [Id, IsRunning, _pumpSpeed, Description];
}
