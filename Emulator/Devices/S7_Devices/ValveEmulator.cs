using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

public class ValveEmulator : IDeviceEmulator, ISnapshotable, IValveEmulator
{
    private readonly int _mbAddress;
    private readonly int _bitIndex;
    private readonly float _openDelay;
    private readonly string _host;
    // Firmware-sequenced valves (PP, FL) are not commanded via the M-bit — the
    // S7-200 recomputes that bit every scan. They are commanded through a
    // V-memory flag (DB1.DBXn.b). When _cmdDbByte >= 0 this valve reacts to that
    // DB bit instead of its M-bit (which it still serves for status reads).
    // _cmdInvert handles PP's force-close logic (V5.3 = 1 ⇒ closed).
    private readonly int _cmdDbByte;
    private readonly int _cmdDbBit;
    private readonly bool _cmdInvert;
    private bool _commanded;
    private bool _isOpen;
    private float _delayRemaining;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public DeviceCategory Category => DeviceCategory.Valve;
    public bool IsOpen => _isOpen;

    public ValveEmulator(string id, int mbAddress, int bitIndex, string description, float openDelay = 0f, string host = "",
        int cmdDbByte = -1, int cmdDbBit = 0, bool cmdInvert = false)
    {
        Id = id;
        _mbAddress = mbAddress;
        _bitIndex = bitIndex;
        Description = description;
        _openDelay = openDelay;
        _host = host;
        _cmdDbByte = cmdDbByte;
        _cmdDbBit = cmdDbBit;
        _cmdInvert = cmdInvert;
    }

    public void SetState(bool open) => _isOpen = open;

    // IValveEmulator — used by RobotEmulator to drive slit valves without going through the wire protocol.
    void IValveEmulator.Command(bool open)
    {
        if (open != _commanded)
            EmulatorLog.Log(LogLevel.Info, $"Valve:{Id}", open ? "Opened (robot cmd)" : "Closed (robot cmd)");
        _commanded = open;
        if (_openDelay <= 0f || !open)
        {
            _isOpen = open;
            _delayRemaining = 0f;
        }
        else
        {
            _delayRemaining = _openDelay;
        }
    }

    private bool CommandMode => _cmdDbByte >= 0;

    private void ApplyCommand(bool open)
    {
        if (open != _commanded)
            EmulatorLog.Log(LogLevel.Info, $"Valve:{Id}", open ? "Opened" : "Closed");
        _commanded = open;
        if (_openDelay <= 0f)
            _isOpen = open;
        else if (!open)
        {
            _isOpen = false;
            _delayRemaining = 0f;
        }
        else
            _delayRemaining = _openDelay;
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            s7.RegisterMbBit(new MbBitRegistration
            {
                DeviceId = Id,
                Address = _mbAddress,
                BitIndex = _bitIndex,
                // Mirror the S7-200 command bit, not the physical valve. The
                // ladder's T38 timer delays the actual open, but the M-bit
                // Control wrote and reads back is set immediately on real
                // hardware. Returning _isOpen here causes A1 (delay=2) to
                // fail PlcVerifiedWrites' 5-attempt readback loop.
                OnRead = () => _commanded,
                // In command mode the M-bit is firmware-owned (read-only mirror):
                // Control commands via the DB bit below and only READS the M-bit.
                OnWrite = CommandMode ? _ => { } : ApplyCommand,
            });

            if (CommandMode)
            {
                // PP/FL command bit lives in V-memory (DB1.DBXn.b). SkipFlush so the
                // controller's write is not clobbered by an OnRead echo each tick.
                s7.RegisterDbRegion(new DbRegion
                {
                    DeviceId = Id,
                    DbNumber = 1,
                    Address = _cmdDbByte,
                    Length = 1,
                    SkipFlush = true,
                    OnRead = () =>
                    {
                        byte b = 0;
                        if (_cmdInvert ? !_commanded : _commanded)
                            b |= (byte)(1 << _cmdDbBit);
                        return [b];
                    },
                    OnWrite = data =>
                    {
                        bool bit = data.Length > 0 && (data[0] & (1 << _cmdDbBit)) != 0;
                        ApplyCommand(_cmdInvert ? !bit : bit);
                    },
                });
            }
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        if (_delayRemaining > 0f && _commanded)
        {
            _delayRemaining -= deltaTime;
            if (_delayRemaining <= 0f)
            {
                _isOpen = true;
                _delayRemaining = 0f;
                EmulatorLog.Log(LogLevel.Info, $"Valve:{Id}", $"Delay complete, valve now open");
            }
        }

        // Publish state so pumps can check valve positions for routing
        state.ValveStates[Id] = _isOpen;
    }

    public void Reset()
    {
        _commanded = false;
        _isOpen = false;
        _delayRemaining = 0f;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["commanded"] = _commanded,
        ["isOpen"] = _isOpen,
        ["delayRemaining"] = _delayRemaining,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _commanded = snapshot["commanded"]?.GetValue<bool>() ?? _commanded;
        _isOpen = snapshot["isOpen"]?.GetValue<bool>() ?? _isOpen;
        _delayRemaining = snapshot["delayRemaining"]?.GetValue<float>() ?? _delayRemaining;
    }

    public object[] GetStatus() => [Id, IsOpen, Description];
}
