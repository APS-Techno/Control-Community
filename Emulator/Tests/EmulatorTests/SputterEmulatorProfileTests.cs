using APS.SystemEmulator.Hosting;
using EmulatorCore.Engine;

namespace EmulatorTests;

public class SputterEmulatorProfileTests
{
    [Fact]
    public void CommunityEditionLoadsEverySputterDevice()
    {
        var registry = new DeviceRegistry(EmulatorEdition.Community.Registrations);
        var missing = HwConfScan.ShortTypes(ConfigsDir.Path("Sputter/HWConf.Emulator.xml")).Where(t => !registry.CanCreate(t));
        Assert.Empty(missing);
    }
}
