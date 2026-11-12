using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

public class MFCEmulator : IDeviceEmulator, ISnapshotable
{
    private readonly int _dbAddress;
    private readonly string _chamberId;
    private readonly string _host;
    private readonly string _inletValveId;
    private readonly string[] _inletValveIds;
    private float _setpoint;
    private float _actualFlow;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public string ChamberId => _chamberId;
    public DeviceCategory Category => DeviceCategory.MFC;
    public float ActualFlow => _actualFlow;
    public float Setpoint => _setpoint;

    /// <summary>
    /// Gas inlet valve(s) gating this MFC's line (valve= attribute in
    /// HWConf.Emulator.xml). Comma-separated list = shared-manifold line: the MFC
    /// flows while ANY listed valve is open. Empty = ungated: flow follows the
    /// setpoint directly.
    /// </summary>
    public string InletValveId => _inletValveId;

    public MFCEmulator(string id, int dbAddress, string description, string chamberId, string host = "",
        string inletValveId = "")
    {
        Id = id;
        _dbAddress = dbAddress;
        _chamberId = chamberId ?? throw new ArgumentNullException(nameof(chamberId));
        Description = description;
        _host = host;
        _inletValveId = inletValveId ?? "";
        _inletValveIds = _inletValveId
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void WriteSetpoint(float value) => _setpoint = value;

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = Id,
                Address = _dbAddress,
                Length = 8,
                OnRead = () =>
                {
                    byte[] buffer = new byte[8];
                    WriteFloatBE(buffer, 0, _setpoint);
                    WriteFloatBE(buffer, 4, _actualFlow);
                    return buffer;
                },
                OnWrite = data =>
                {
                    if (data.Length >= 4)
                    {
                        float newSetpoint = ReadFloatBE(data, 0);
                        if (MathF.Abs(newSetpoint - _setpoint) > 0.1f)
                            EmulatorLog.Log(LogLevel.Info, $"MFC:{Id}", $"Setpoint: {_setpoint:F1} \u2192 {newSetpoint:F1} sccm");
                        _setpoint = newSetpoint;
                    }
                },
            });
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        // Log replay override
        if (state.DataSource.TryGetValue(Id, "Flow", state.SimTime, out float replayFlow))
        {
            _actualFlow = replayFlow;
        }
        else
        {
            // A closed inlet line blocks the gas regardless of the setpoint —
            // a real MFC indicates ~0 flow against a closed downstream valve.
            // Multiple ids = shared manifold (any open admits gas). Unknown
            // valve ids read as closed (fail-safe).
            bool inletOpen = _inletValveIds.Length == 0;
            foreach (var v in _inletValveIds)
                inletOpen |= state.ValveStates.GetValueOrDefault(v);
            float target = inletOpen ? _setpoint : 0f;

            float tau = 0.5f;
            float alpha = 1.0f - MathF.Exp(-deltaTime / tau);
            _actualFlow += (target - _actualFlow) * alpha;
        }

        if (state.Chambers.TryGetValue(_chamberId, out var chamber))
            chamber.GasLoad += _actualFlow;
    }

    public void Reset()
    {
        _setpoint = 0f;
        _actualFlow = 0f;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["setpoint"] = _setpoint,
        ["actualFlow"] = _actualFlow,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _setpoint = snapshot["setpoint"]?.GetValue<float>() ?? _setpoint;
        _actualFlow = snapshot["actualFlow"]?.GetValue<float>() ?? _actualFlow;
    }

    public object[] GetStatus() => [Id, _setpoint, _actualFlow, Description];

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
