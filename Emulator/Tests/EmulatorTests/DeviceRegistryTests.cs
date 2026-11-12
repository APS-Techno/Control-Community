using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using static EmulatorTests.RegistryTestHelpers;

namespace EmulatorTests;

public class DeviceRegistryTests
{
    private sealed class Reg(string key) : IDeviceRegistration
    {
        public void Register(IDeviceRegistry r) => r.Add(key, _ => null);
    }

    [Fact]
    public void DuplicateRegistration_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new DeviceRegistry([new Reg("S7200Valve"), new Reg("s7200valve")]));
        Assert.Contains("s7200valve", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownType_TryCreateFalse()
        => Assert.False(new DeviceRegistry([]).TryCreate(Ctx("<valve address=\"2\">V1</valve>", "Nope"), out _));
}

public class ConfigLoaderRegistryTests
{
    [Fact]
    public void UnregisteredType_SkippedWithWarning()
    {
        using var log = EmulatorLogCapture.Start();
        var loader = new ConfigLoader(new DeviceRegistry([]), DefaultPhysics());
        var devices = loader.LoadHWConfig(TestConfigs.Write("<hardware><powersupplies lib=\"Vendor\" hwtype=\"Vendor.UnknownSupply\"><powersupply port=\"COM7\">PSU_B</powersupply></powersupplies></hardware>"));
        Assert.DoesNotContain(devices, d => d.Id == "PSU_B");
        Assert.Single(log.Warnings, w => w.Contains("UnknownSupply"));
    }

    [Fact]
    public void RotationStageSection_NoWarning()
    {
        using var log = EmulatorLogCapture.Start();
        var loader = new ConfigLoader(new DeviceRegistry([new S7DeviceRegistration()]), DefaultPhysics());
        loader.LoadHWConfig(TestConfigs.Write("<hardware><stages lib=\"Vendor\" hwtype=\"Vendor.S7200RotationStage\" host=\"main\"><stage address=\"1\">ROTX</stage></stages></hardware>"));
        Assert.DoesNotContain(log.Warnings, w => w.Contains("S7200RotationStage"));
    }

    [Fact]
    public void TwoSectionsSameUnregisteredType_OneWarningWithTotal()
    {
        using var log = EmulatorLogCapture.Start();
        var loader = new ConfigLoader(new DeviceRegistry([]), DefaultPhysics());
        loader.LoadHWConfig(TestConfigs.Write("<hardware><a hwtype=\"X.Foo\"><d>1</d><d>2</d></a><b hwtype=\"X.Foo\"><d>3</d></b></hardware>"));
        var w = Assert.Single(log.Warnings, w => w.Contains("Foo"));
        Assert.Contains("3 device(s)", w);
    }

    [Fact]
    public void RegisteredType_CreatedThroughFactory()
    {
        var loader = new ConfigLoader(new DeviceRegistry([new S7DeviceRegistration()]), DefaultPhysics());
        var devices = loader.LoadHWConfig(TestConfigs.Write("<hardware><plcs><plc type=\"Vendor.S7200PLC\" address=\"127.0.0.1\">main</plc></plcs><valves lib=\"Vendor\" hwtype=\"Vendor.S7200Valve\" host=\"main\"><valve address=\"2\" index=\"4\">PP</valve></valves></hardware>"));
        Assert.Contains(devices, d => d.Id == "PP");
    }
}
