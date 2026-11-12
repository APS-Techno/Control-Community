using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

/// <summary>
/// Delphi writes setpoint to DB1:address (4-byte float), pulses trigger at DB1:triggerAddr bit triggerBit,
/// and reads position feedback from DB1:address+4.
/// </summary>
public class StageEmulator : IDeviceEmulator, ISnapshotable, IStageEmulator, IRobotLockable, IAudibleMotion
{
    private readonly int _dbAddress;       // feedback at +4
    private readonly int _triggerDbAddr;
    private readonly int _triggerBitIndex;
    private readonly int _stopDbAddr;       // -1 = none
    private readonly int _stopBitIndex;     // -1 = none
    private readonly float _moveSpeed;     // mm/s
    private readonly string _host;

    private float _setpoint;
    private float _position;
    private bool _moving;
    private byte _triggerByte;
    private byte _stopByte;
    private Func<bool>? _isLockedByRobot;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public DeviceCategory Category => DeviceCategory.Stage;
    public float Position => _position;
    public float Setpoint => _setpoint;
    public bool IsMoving => _moving;
    bool IAudibleMotion.IsInMotion => _moving;

    // IStageEmulator — used by RobotEmulator to direct the ARM rotation stage.
    // This path bypasses the robot-lock guard: the robot is the one writing, not the operator.
    void IStageEmulator.SetSetpoint(float position)
    {
        if (MathF.Abs(position - _setpoint) > 0.01f)
            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                $"Setpoint (robot cmd): {_setpoint:F2} → {position:F2}");
        _setpoint = position;
        _moving = true;
    }

    /// <summary>
    /// Called by <see cref="ProjectLifecycleController"/> after Bind to wire a lock-provider
    /// that gates wire-side setpoint writes during robot transfer.
    /// </summary>
    public void SetRobotLockProvider(Func<bool> isLocked) => _isLockedByRobot = isLocked;

    /// <summary>
    /// Test seam — simulate a wire-side setpoint write without a real protocol server.
    /// Also used internally by the OnWrite callback.
    /// </summary>
    internal void WriteSetpointFromWire(float newSetpoint)
    {
        if (_isLockedByRobot?.Invoke() == true)
        {
            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                "Setpoint write rejected — robot transfer in progress");
            return;
        }
        if (MathF.Abs(newSetpoint - _setpoint) > 0.01f)
            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                $"Setpoint: {_setpoint:F2} → {newSetpoint:F2} mm");
        _setpoint = newSetpoint;
    }

    public StageEmulator(string id, int dbAddress, int triggerDbAddr, int triggerBitIndex, string description, string host = "", int stopDbAddr = -1, int stopBitIndex = -1)
    {
        Id = id;
        _dbAddress = dbAddress;
        _triggerDbAddr = triggerDbAddr;
        _triggerBitIndex = triggerBitIndex;
        _stopDbAddr = stopDbAddr;
        _stopBitIndex = stopBitIndex;
        _moveSpeed = 20f;
        Description = description;
        _host = host;
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                $"Registered DB1:{_dbAddress} (setpoint+feedback), trigger DB1:{_triggerDbAddr} bit {_triggerBitIndex}");

            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = Id,
                Address = _dbAddress,
                Length = 8,
                OnRead = () =>
                {
                    byte[] buffer = new byte[8];
                    WriteFloatBE(buffer, 0, _setpoint);
                    WriteFloatBE(buffer, 4, _position);
                    return buffer;
                },
                OnWrite = data =>
                {
                    if (data.Length >= 4)
                        WriteSetpointFromWire(ReadFloatBE(data, 0));
                },
            });

            s7.RegisterDbRegion(new DbRegion
            {
                DeviceId = $"{Id}_trig",
                Address = _triggerDbAddr,
                Length = 1,
                OnRead = () => [_triggerByte],
                OnWrite = data =>
                {
                    if (data.Length >= 1)
                    {
                        byte prev = _triggerByte;
                        _triggerByte = data[0];
                        bool wasSet = (prev & (1 << _triggerBitIndex)) != 0;
                        bool isSet = (_triggerByte & (1 << _triggerBitIndex)) != 0;
                        if (isSet && !wasSet)
                        {
                            _moving = true;
                            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                                $"Moving to {_setpoint:F2} mm");
                        }
                    }
                },
            });

            // Optional stop bit (ZDQL_TZ / ZDSL_TZ). A rising edge
            // freezes motion where it is; a fresh trigger pulse is required to
            // move again — mirrors the real halt.
            if (_stopBitIndex >= 0 && _stopDbAddr >= 0)
            {
                s7.RegisterDbRegion(new DbRegion
                {
                    DeviceId = $"{Id}_stop",
                    Address = _stopDbAddr,
                    Length = 1,
                    // Write-through command bit: the real V18.1/V18.4 stop flags
                    // share the DB byte that the (unverified) turbo status word
                    // occupies in the emulator. SkipFlush keeps this region from
                    // echoing over the TP status byte on reads, while the write
                    // path still fires OnWrite so Stop is detected.
                    SkipFlush = true,
                    OnRead = () => [_stopByte],
                    OnWrite = data =>
                    {
                        if (data.Length < 1) return;
                        byte prev = _stopByte;
                        _stopByte = data[0];
                        bool wasSet = (prev & (1 << _stopBitIndex)) != 0;
                        bool isSet = (_stopByte & (1 << _stopBitIndex)) != 0;
                        if (isSet && !wasSet && _moving)
                        {
                            _moving = false;
                            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                                $"STOP — halted at {_position:F2} mm");
                        }
                    },
                });
            }
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        if (!_moving) return;

        float diff = _setpoint - _position;
        float step = _moveSpeed * deltaTime;
        if (MathF.Abs(diff) <= step)
        {
            _position = _setpoint;
            _moving = false;
            EmulatorLog.Log(LogLevel.Info, $"Stage:{Id}",
                $"Reached {_position:F2} mm");
        }
        else
        {
            _position += MathF.Sign(diff) * step;
        }
    }

    public void Reset()
    {
        _setpoint = 0f;
        _position = 0f;
        _moving = false;
        _triggerByte = 0;
        _stopByte = 0;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["setpoint"] = _setpoint,
        ["position"] = _position,
        ["moving"] = _moving,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _setpoint = snapshot["setpoint"]?.GetValue<float>() ?? _setpoint;
        _position = snapshot["position"]?.GetValue<float>() ?? _position;
        _moving = snapshot["moving"]?.GetValue<bool>() ?? _moving;
    }

    public object[] GetStatus() => [Id, _position, _setpoint, IsMoving, Description];

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
