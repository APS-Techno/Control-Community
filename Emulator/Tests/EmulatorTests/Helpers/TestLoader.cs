using APS.Emulator.Devices.S7;
using APS.SystemEmulator.Engine;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public sealed class TestLoader(PhysicsConfig? physics = null)
{
    private readonly DeviceRegistry _registry = new([new S7DeviceRegistration()]);
    private readonly PhysicsConfig _physics = physics ?? new PhysicsConfig();

    public List<IDeviceEmulator> LoadHWConfig(string path)
    {
        var devices = new ConfigLoader(_registry, _physics).LoadHWConfig(path);
        _registry.WireAll(devices);
        return devices;
    }
}
