using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

/// <summary>
/// PLC-hosted throttle valve. Exposes an 8-byte DB region at <c>address</c>
/// (setpoint Torr at +0, position % at +4) and runs a simple PI loop that
/// drives <see cref="ChamberState.ThrottlePosition"/> for the named chamber.
///
/// Unlike a serial throttle controller it has no stored setpoint slots,
/// no PID activation, and no serial framing.
/// </summary>
public class VacPLCThrottleEmulator : IDeviceEmulator, ISnapshotable
{
    // Setpoint sentinels — match S7200ThrottleValve.
    private const float OpenSentinel = 0f;
    private const float CloseSentinel = 9999f;
    private const float MinPosition = 0.5f;          // can't seal fully
    private const float OpenRate = 33f;              // %/s — open in ~3s
    private const float CloseRate = 20f;             // %/s — close in ~5s (fast-demo regulation)

    // PI gains, in mTorr space.
    private const float Kp = 8f;
    private const float Ki = 0.5f;

    private readonly int _dbAddress;
    private readonly string _chamberId;
    private readonly string _host;

    private float _setpointTorr;
    private float _position = 100f;       // % open
    private float _integral;
    private float _prevError;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public string ChamberId => _chamberId;
    public DeviceCategory Category => DeviceCategory.Controller;

    public float Position => _position;
    public float LastSetpoint => _setpointTorr;

    public VacPLCThrottleEmulator(string id, int dbAddress, string description,
        string chamberId, string host = "")
    {
        Id = id;
        _dbAddress = dbAddress;
        Description = description;
        _chamberId = chamberId ?? throw new ArgumentNullException(nameof(chamberId));
        _host = host;
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is not IS7ProtocolServer s7) return;
        s7.RegisterDbRegion(new DbRegion
        {
            DeviceId = Id,
            Address = _dbAddress,
            Length = 8,
            OnRead = () =>
            {
                byte[] buffer = new byte[8];
                WriteFloatBE(buffer, 0, _setpointTorr);
                WriteFloatBE(buffer, 4, _position);
                return buffer;
            },
            OnWrite = data =>
            {
                if (data.Length >= 4)
                {
                    float incoming = ReadFloatBE(data, 0);
                    if (MathF.Abs(incoming - _setpointTorr) > 1e-7f)
                        EmulatorLog.Log(LogLevel.Info, $"PLCThrottle:{Id}",
                            $"Setpoint: {_setpointTorr:F4} → {incoming:F4} Torr");
                    _setpointTorr = incoming;
                    _integral = 0f;
                    _prevError = 0f;
                }
            },
        });
    }

    public void Update(float deltaTime, SimulationState state)
    {
        state.Chambers.TryGetValue(_chamberId, out var chamber);

        float target = ResolveTarget(chamber, deltaTime);
        target = Math.Clamp(target, MinPosition, 100f);

        if (MathF.Abs(_position - target) > 0.01f)
        {
            float rate = target > _position ? OpenRate : CloseRate;
            float step = rate * deltaTime;
            _position = target > _position
                ? MathF.Min(_position + step, target)
                : MathF.Max(_position - step, target);
        }
        else
        {
            _position = target;
        }

        if (chamber != null)
        {
            chamber.ThrottlePosition = _position;
            chamber.ThrottleSetpointTorr = _setpointTorr;
        }
    }

    private float ResolveTarget(ChamberState? chamber, float deltaTime)
    {
        if (_setpointTorr <= OpenSentinel + 1e-7f)
            return 100f;
        if (_setpointTorr >= CloseSentinel)
            return MinPosition;
        if (chamber == null)
            return _position;

        // PI in mTorr space.
        float chamberMTorr = chamber.Pressure * 1000f;
        float setpointMTorr = _setpointTorr * 1000f;
        float error = chamberMTorr - setpointMTorr;
        _integral = Math.Clamp(_integral + error * deltaTime, -50f, 50f);
        _prevError = error;

        float output = Kp * error + Ki * _integral;
        return _position + output;
    }

    public void Reset()
    {
        _setpointTorr = 0f;
        _position = 100f;
        _integral = 0f;
        _prevError = 0f;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["setpointTorr"] = _setpointTorr,
        ["position"] = _position,
        ["integral"] = _integral,
        ["prevError"] = _prevError,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _setpointTorr = snapshot["setpointTorr"]?.GetValue<float>() ?? _setpointTorr;
        _position = snapshot["position"]?.GetValue<float>() ?? _position;
        _integral = snapshot["integral"]?.GetValue<float>() ?? _integral;
        _prevError = snapshot["prevError"]?.GetValue<float>() ?? _prevError;
    }

    public object[] GetStatus() => [Id, _setpointTorr, _position, Description];

    internal void SetPositionForTest(float pos) => _position = pos;
    internal void WriteSetpointForTest(float torr) { _setpointTorr = torr; _integral = 0f; _prevError = 0f; }

    private static void WriteFloatBE(byte[] buffer, int offset, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        buffer[offset] = bytes[3]; buffer[offset + 1] = bytes[2];
        buffer[offset + 2] = bytes[1]; buffer[offset + 3] = bytes[0];
    }

    private static float ReadFloatBE(byte[] buffer, int offset)
    {
        byte[] bytes = [buffer[offset + 3], buffer[offset + 2], buffer[offset + 1], buffer[offset]];
        return BitConverter.ToSingle(bytes);
    }
}
