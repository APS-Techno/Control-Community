using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using APS.Emulator.Devices.Demo;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class LanThrottleEmulatorTests
{
    private const string ChamberId = "C1";
    private const float Dt = 0.05f;

    private static LanThrottleEmulator NewDevice() => new("TC1", "Test throttle", ChamberId);

    private static SimulationState NewState(float pressure = 0.05f, float gasSccm = 0f)
    {
        var state = TestHelpers.CreateTestState();
        state.SetChamber(new ChamberState
        {
            Id = ChamberId,
            Type = ChamberType.Process,
            Pressure = pressure,
            Volume = 20f,
            PumpSpeed = 250f,
            BasePressure = 1e-7f,
            BaseLeakRate = 1e-6f,
            LeakRate = 1e-6f,
            GasLoad = gasSccm,
        });
        return state;
    }

    private static string Send(LanThrottleEmulator tv, string line)
        => Encoding.ASCII.GetString(tv.HandleCommand(Encoding.ASCII.GetBytes(line + "\n")));

    private static void Run(LanThrottleEmulator tv, SimulationState state, float seconds)
    {
        var chamber = state.Chambers[ChamberId];
        for (float t = 0f; t < seconds; t += Dt)
        {
            tv.Update(Dt, state);
            chamber.Pressure = state.Physics.ComputePressure(chamber, Dt);
        }
    }

    [Fact]
    public void Idn_ReturnsProductId()
        => Assert.Equal("APS,LANTHROT-100,0,1.0\n", Send(NewDevice(), "*IDN?"));

    [Fact]
    public void Floor_ParsesToOneEMinusFive()
    {
        string reply = Send(NewDevice(), "FLOOR?");
        Assert.EndsWith("\n", reply);
        Assert.Equal(1e-5, double.Parse(reply.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Park_OpensFully_ChamberPumpsToBase()
    {
        var tv = NewDevice();
        var state = NewState(gasSccm: 20f);
        var chamber = state.Chambers[ChamberId];

        Send(tv, "TARG 0.003");
        Run(tv, state, 20f);
        Assert.InRange(tv.Position, 30f, 90f);

        chamber.GasLoad = 0f;
        Assert.Equal("", Send(tv, "PARK"));
        Run(tv, state, 60f);

        Assert.Equal(100f, tv.Position, precision: 1);
        Assert.Equal("100\n", Send(tv, "POS?"));
        Assert.InRange(chamber.Pressure, 1e-7f, 1.2e-7f);
        Assert.Equal("0\n", Send(tv, "TARG?"));
    }

    [Fact]
    public void TargZero_IsPark()
    {
        var tv = NewDevice();
        var state = NewState(gasSccm: 20f);
        Send(tv, "TARG 0.003");
        Run(tv, state, 10f);
        Send(tv, "TARG 0");
        Run(tv, state, 10f);
        Assert.Equal(100f, tv.Position, precision: 1);
        Assert.Equal("0\n", Send(tv, "TARG?"));
    }

    [Fact]
    public void TargAtFloor_Regulates_NotParked()
    {
        var tv = NewDevice();
        var state = NewState(pressure: 1e-6f);
        Assert.Equal("", Send(tv, "TARG 1e-5"));
        Run(tv, state, 30f);
        Assert.Equal("0,\"No error\"\n", Send(tv, "SYST:ERR?"));
        Assert.True(tv.Position < 10f, $"Expected a regulating (closed-down) valve, got {tv.Position}%");
        Assert.Equal(1e-5, double.Parse(Send(tv, "TARG?").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TargBelowFloor_Queues222()
    {
        var tv = NewDevice();
        Send(tv, "TARG 0.003");
        Assert.Equal("", Send(tv, "TARG 1e-6"));
        Assert.Equal("-222,\"Data out of range\"\n", Send(tv, "SYST:ERR?"));
        Assert.Equal("0,\"No error\"\n", Send(tv, "SYST:ERR?"));
        Assert.Equal(0.003, double.Parse(Send(tv, "TARG?").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("TARG")]
    [InlineData("TARG abc")]
    [InlineData("TARG -1")]
    [InlineData("TARG NaN")]
    public void BadTargetArgument_Queues222(string line)
    {
        var tv = NewDevice();
        Assert.Equal("", Send(tv, line));
        Assert.Equal("-222,\"Data out of range\"\n", Send(tv, "SYST:ERR?"));
    }

    [Fact]
    public void UnknownHeader_Queues113()
    {
        var tv = NewDevice();
        Assert.Equal("", Send(tv, "FOO 1"));
        Assert.Equal("-113,\"Undefined header\"\n", Send(tv, "SYST:ERR?"));
    }

    [Fact]
    public void Targ_HoldsNearTargetWithGas()
    {
        var tv = NewDevice();
        var state = NewState(pressure: 0.05f, gasSccm: 20f);
        Send(tv, "TARG 0.003");
        Run(tv, state, 60f);
        float pressure = state.Chambers[ChamberId].Pressure;
        Assert.InRange(pressure, 0.003f * 0.9f, 0.003f * 1.1f);
        Assert.InRange(tv.Position, 1f, 99f);
    }

    [Fact]
    public void CloseSentinel_ClosesValve()
    {
        var tv = NewDevice();
        var state = NewState();
        Send(tv, "TARG 9999");
        Run(tv, state, 30f);
        Assert.True(tv.Position <= 1f, $"Expected near-closed, got {tv.Position}%");
    }

    [Fact]
    public void Update_WritesThrottleIntoChamber_AndPresReportsChamberPressure()
    {
        var tv = NewDevice();
        var state = NewState(pressure: 0.02f);
        var chamber = state.Chambers[ChamberId];
        Send(tv, "TARG 0.003");
        tv.Update(Dt, state);
        Assert.Equal(tv.Position, chamber.ThrottlePosition);
        Assert.Equal(0.003f, chamber.ThrottleSetpointTorr);
        Assert.Equal(0.02f, float.Parse(Send(tv, "PRES?").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Update_WithoutChamber_HoldsPosition()
    {
        var tv = NewDevice();
        var state = TestHelpers.CreateTestState();
        Send(tv, "TARG 0.003");
        tv.Update(Dt, state);
        Assert.Equal(100f, tv.Position);
    }

    [Fact]
    public void Snapshot_RoundTripsTargetPositionAndIntegral()
    {
        var tv = NewDevice();
        var state = NewState(gasSccm: 20f);
        Send(tv, "TARG 0.003");
        Run(tv, state, 10f);

        JsonObject snapshot = tv.GetSnapshot();
        var restored = NewDevice();
        restored.RestoreSnapshot(snapshot);

        Assert.Equal(tv.Position, restored.Position);
        Assert.Equal(tv.TargetTorr, restored.TargetTorr);
        Assert.Equal(snapshot.ToJsonString(), restored.GetSnapshot().ToJsonString());
    }

    [Fact]
    public void Reset_ParksAndClearsErrors()
    {
        var tv = NewDevice();
        var state = NewState();
        Send(tv, "TARG 0.003");
        Send(tv, "TARG 1e-6");
        Run(tv, state, 5f);
        tv.Reset();
        Assert.Equal(100f, tv.Position);
        Assert.Equal("0\n", Send(tv, "TARG?"));
        Assert.Equal("0,\"No error\"\n", Send(tv, "SYST:ERR?"));
    }

    [Fact]
    public void Create_ReadsTrimmedIdChamberAndDescription()
    {
        var ctx = new DeviceCreateContext(
            XElement.Parse("<controller chamber=\"C1\" descr=\"Throttle\">\n TC1 \n</controller>"),
            "controllers", "LanThrottleController", "", RegistryTestHelpers.DefaultPhysics());
        var device = Assert.IsType<LanThrottleEmulator>(LanThrottleEmulator.Create(ctx));
        Assert.Equal("TC1", device.Id);
        Assert.Equal("Throttle", device.Description);
        Assert.Equal(ChamberId, device.ChamberId);
        Assert.Equal(DeviceCategory.Controller, device.Category);
    }

    [Fact]
    public void Create_WithoutChamber_ReturnsNull()
    {
        var ctx = new DeviceCreateContext(
            XElement.Parse("<controller>TC1</controller>"),
            "controllers", "LanThrottleController", "", RegistryTestHelpers.DefaultPhysics());
        Assert.Null(LanThrottleEmulator.Create(ctx));
    }
}
