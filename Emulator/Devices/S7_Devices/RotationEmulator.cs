using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

/// <summary>
/// Delphi writes speed setpoint to DB1:setpointAddr, reads feedback from DB1:feedbackAddr,
/// and toggles enable via MB:mbAddr bit mbBit.
/// </summary>
public class RotationEmulator : IDeviceEmulator, ISnapshotable
{
    private readonly int _setpointAddr;
    private readonly int _feedbackAddr;
    private readonly int _mbAddr;
    private readonly int _mbBit;

    private float _setpoint;
    private float _speed;
    private bool _enabled;

    public string Id { get; }
    public string Description { get; }
    /// <summary>
    /// Substrate rotation is hardcoded; its host (PLC plcId) is supplied by
    /// ConfigLoader from HWConf's first &lt;plc&gt; entry so the rotation registers
    /// with the matching S7 server in the multi-PLC routing filter.
    /// </summary>
    public string Host => _host;
    private readonly string _host;
    public DeviceCategory Category => DeviceCategory.Stage;
    public float Speed => _speed;
    public bool Enabled => _enabled;

    public RotationEmulator(string id, int setpointAddr, int feedbackAddr,
        int mbAddr, int mbBit, string description, string host = "")
    {
        Id = id;
        _setpointAddr = setpointAddr;
        _feedbackAddr = feedbackAddr;
        _mbAddr = mbAddr;
        _mbBit = mbBit;
        Description = description;
        _host = host;
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            EmulatorLog.Log(LogLevel.Info, $"Rotation:{Id}",
                $"Registered DB1:{_setpointAddr}/{_feedbackAddr} (sp/fb), enable MB{_mbAddr}:{_mbBit}");

            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = $"{Id}_sp",
                Address = _setpointAddr,
                Length = 4,
                OnRead = () =>
                {
                    byte[] buf = new byte[4];
                    WriteFloatBE(buf, 0, _setpoint);
                    return buf;
                },
                OnWrite = data =>
                {
                    if (data.Length >= 4)
                    {
                        float newSp = ReadFloatBE(data, 0);
                        if (MathF.Abs(newSp - _setpoint) > 0.1f)
                            EmulatorLog.Log(LogLevel.Info, $"Rotation:{Id}",
                                $"Speed setpoint: {newSp:F1} rpm");
                        _setpoint = newSp;
                    }
                },
            });

            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = $"{Id}_fb",
                Address = _feedbackAddr,
                Length = 4,
                OnRead = () =>
                {
                    byte[] buf = new byte[4];
                    WriteFloatBE(buf, 0, _speed);
                    return buf;
                },
                OnWrite = _ => { },
            });

            s7.RegisterMbBit(new MbBitRegistration
            {
                DeviceId = Id,
                Address = _mbAddr,
                BitIndex = _mbBit,
                OnRead = () => _enabled,
                OnWrite = v =>
                {
                    if (v != _enabled)
                        EmulatorLog.Log(LogLevel.Info, $"Rotation:{Id}",
                            v ? "Enabled" : "Disabled");
                    _enabled = v;
                },
            });
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        float target = _enabled ? _setpoint : 0f;
        float tau = 1.0f;
        float alpha = 1.0f - MathF.Exp(-deltaTime / tau);
        _speed += (target - _speed) * alpha;
    }

    public void Reset()
    {
        _setpoint = 0f;
        _speed = 0f;
        _enabled = false;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["setpoint"] = _setpoint,
        ["speed"] = _speed,
        ["enabled"] = _enabled,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _setpoint = snapshot["setpoint"]?.GetValue<float>() ?? _setpoint;
        _speed = snapshot["speed"]?.GetValue<float>() ?? _speed;
        _enabled = snapshot["enabled"]?.GetValue<bool>() ?? _enabled;
    }

    public object[] GetStatus() => [Id, _enabled, _speed, _setpoint, Description];

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
