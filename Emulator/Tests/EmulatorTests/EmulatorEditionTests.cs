using APS.Emulator.Devices.Demo;
using APS.Emulator.Devices.S7;
using APS.SystemEmulator.Hosting;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;

namespace EmulatorTests;

public class EmulatorEditionTests
{
    [Fact]
    public void Community_RegistersOnlyOpenDeviceSets()
        => Assert.Equal(new[] { typeof(S7DeviceRegistration), typeof(DemoDeviceRegistration) }, EmulatorEdition.Community.Registrations.Select(r => r.GetType()));

    [Fact]
    public void Community_AuthorizesWithoutLicense()
        => Assert.True(EmulatorEdition.Community.AuthorizeProduct());

    [Fact]
    public void Registry_CanCreate_RegisteredTypeAndSectionOnly()
    {
        var registry = new DeviceRegistry([new S7DeviceRegistration(), new SectionRegistration()]);
        Assert.True(registry.CanCreate("S7200Valve"));
        Assert.True(registry.CanCreate("s7200valve"));
        Assert.True(registry.CanCreate("robots"));
        Assert.False(registry.CanCreate("UnknownType"));
    }

    private sealed class SectionRegistration : IDeviceRegistration
    {
        public void Register(IDeviceRegistry registry) => registry.AddSection("robots", (_, _) => []);
    }
}
