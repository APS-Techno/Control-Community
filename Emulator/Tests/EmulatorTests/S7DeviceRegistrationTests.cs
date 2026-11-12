using APS.Emulator.Devices.S7;
using EmulatorCore.Engine;
using static EmulatorTests.RegistryTestHelpers;

namespace EmulatorTests;

public class S7DeviceRegistrationTests
{
    private static readonly string[] S7Types =
    [
        "S7200Valve", "S7200GasValve", "S7200SequencedValve", "S7200PositionValve",
        "S7200MFC", "S7200Pump", "S7200TurboPump", "S7200InputValve",
        "S7200Sensor", "S7200Stage", "S7200ThrottleValve",
    ];

    private static string Xml(string type) => type switch
    {
        "S7200MFC" or "S7200Sensor" or "S7200Pump" or "S7200TurboPump" or "S7200ThrottleValve"
            => "<d address=\"2\" chamber=\"Main\">D1</d>",
        _ => "<d address=\"2\" index=\"1\">D1</d>",
    };

    [Fact]
    public void RegistersExactlyTheS7Types()
    {
        var registry = new DeviceRegistry([new S7DeviceRegistration()]);
        foreach (var type in S7Types)
        {
            Assert.True(registry.TryCreate(Ctx(Xml(type), type), out var device), type);
            Assert.NotNull(device);
        }
        Assert.False(registry.TryCreate(Ctx("<d>X</d>", "UnknownTypeA"), out _));
        Assert.False(registry.TryCreate(Ctx("<d>X</d>", "UnknownTypeB"), out _));
    }

    [Fact]
    public void RotationStage_IsRegisteredWithNullDevice()
    {
        var registry = new DeviceRegistry([new S7DeviceRegistration()]);
        Assert.True(registry.TryCreate(Ctx("<d address=\"2\">ROT</d>", "S7200RotationStage"), out var device));
        Assert.Null(device);
    }
}
