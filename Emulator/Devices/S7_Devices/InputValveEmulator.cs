using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.S7;

/// <summary>
/// Registers an EB (process input) bit. Signal is inverted: EB 0 = valve open.
/// </summary>
public class InputValveEmulator : IDeviceEmulator, ISnapshotable
{
    private readonly int _ebAddress;
    private readonly int _bitIndex;
    private readonly string _host;
    private bool _isOpen;

    public string Id { get; }
    public string Description { get; }
    public string Host => _host;
    public DeviceCategory Category => DeviceCategory.Valve;
    public bool IsOpen => _isOpen;

    public InputValveEmulator(string id, int ebAddress, int bitIndex, string description, string host = "")
    {
        Id = id;
        _ebAddress = ebAddress;
        _bitIndex = bitIndex;
        Description = description;
        _host = host;
    }

    public void SetState(bool open) => _isOpen = open;

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is IS7ProtocolServer s7)
        {
            s7.RegisterEbBit(new EbBitRegistration
            {
                DeviceId = Id,
                Address = _ebAddress,
                BitIndex = _bitIndex,
                // Inverted: EB bit 0 = valve open, EB bit 1 = valve closed
                OnRead = () => !_isOpen,
                OnWrite = v =>
                {
                    bool newState = !v;
                    if (newState != _isOpen)
                        EmulatorLog.Log(LogLevel.Info, $"Valve:{Id}", newState ? "Opened" : "Closed");
                    _isOpen = newState;
                },
            });
        }
    }

    public void Update(float deltaTime, SimulationState state)
    {
        // Turbo pump checks GVM.
        state.ValveStates[Id] = _isOpen;
    }

    public void Reset()
    {
        _isOpen = false;
    }

    public JsonObject GetSnapshot() => new()
    {
        ["isOpen"] = _isOpen,
    };

    public void RestoreSnapshot(JsonObject snapshot)
    {
        _isOpen = snapshot["isOpen"]?.GetValue<bool>() ?? _isOpen;
    }

    public object[] GetStatus() => [Id, IsOpen, Description];
}
