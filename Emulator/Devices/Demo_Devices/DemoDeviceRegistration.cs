using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;

namespace APS.Emulator.Devices.Demo;

public sealed class DemoDeviceRegistration : IDeviceRegistration
{
    public void Register(IDeviceRegistry registry)
    {
        registry.Add("LanPowerSupply", CreateLanPowerSupply);
        registry.Add("LanThrottleController", LanThrottleEmulator.Create);
        registry.Add("LanRotaryDrive", LanRotaryDriveEmulator.Create);
        registry.Add("LanTransferRobot", LanTransferRobotEmulator.Create);
    }

    private static IDeviceEmulator? CreateLanPowerSupply(DeviceCreateContext ctx)
    {
        string id = ctx.Element.Value.Trim();
        string mode = (ctx.Element.Attribute("mode")?.Value ?? "").Trim().ToUpperInvariant();
        if (mode != "DC" && mode != "PDC" && mode != "RF")
        {
            EmulatorLog.Log(LogLevel.Warning, "LanPowerSupply",
                $"Device '{id}' has invalid mode='{mode}'. Allowed: DC, PDC, RF. Device skipped.");
            return null;
        }
        return new LanPsuEmulator(id, mode, ctx.Element.Attribute("descr")?.Value ?? "");
    }
}
