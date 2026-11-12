using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class TurboPumpEmulatorTests
{
    private sealed class CapturingS7Server : IS7ProtocolServer
    {
        public List<DbRegion> DbRegions { get; } = [];

        public string Name => "test";
        public string PlcId => "main";
        public bool IsRunning => false;
        public void Start() { }
        public void Stop() { }
        public void Flush() { }
        public void Dispose() { }
        public void RegisterDbRegion(DbRegion region) => DbRegions.Add(region);
        public void RegisterEbBit(EbBitRegistration bit) { }
        public void RegisterMbBit(MbBitRegistration bit) { }
    }

    /// <summary>
    /// The status register is a SINGLE byte at the configured address (VB17 on
    /// the real firmware), not a 2-byte word: VB18 holds the
    /// auto-load/unload flags.
    /// </summary>
    [Fact]
    public void TurboPump_ServesStatusAsOneByteAtTheConfiguredAddress()
    {
        var turbo = new TurboPumpEmulator("TP", 17, 0, 0, 300f, "Turbo",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        var server = new CapturingS7Server();

        turbo.RegisterProtocol(server);

        var region = Assert.Single(server.DbRegions);
        Assert.Equal(17, region.Address);
        Assert.Equal(1, region.Length);
        Assert.Single(region.OnRead());
    }

    [Fact]
    public void TurboPump_IdleStatusByte_IsZero()
    {
        var turbo = new TurboPumpEmulator("TP", 17, 0, 0, 300f, "Turbo",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        var server = new CapturingS7Server();
        turbo.RegisterProtocol(server);

        Assert.Equal(0x00, server.DbRegions[0].OnRead()[0]);
    }

    [Fact]
    public void TurboPump_AtSpeedStatusByte_SetsRunningNormalRotation_NotAlarm()
    {
        var turbo = new TurboPumpEmulator("TP", 17, 0, 0, 300f, "Turbo",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        var server = new CapturingS7Server();
        turbo.RegisterProtocol(server);
        var state = TestHelpers.CreateTestState();

        turbo.Command(true);
        for (int i = 0; i < 350; i++)
        {
            state.Chambers["Main"].PumpSpeed = 0f;
            turbo.Update(0.1f, state);
        }

        byte status = server.DbRegions[0].OnRead()[0];
        Assert.True((status & (1 << 1)) != 0, "bit1 running");
        Assert.True((status & (1 << 3)) != 0, "bit3 at speed");
        Assert.True((status & (1 << 4)) != 0, "bit4 rotation");
        Assert.True((status & (1 << 5)) == 0, "bit5 alarm must stay clear");
    }

    [Fact]
    public void TurboPump_DefaultState_IsNotRunning()
    {
        var turbo = new TurboPumpEmulator("TP1", 0, 0, 0, 50f, "Test turbo pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        Assert.False(turbo.IsRunning);
        Assert.False(turbo.IsAtSpeed);
    }

    [Fact]
    public void TurboPump_After35sSpinUp_IsAtSpeed()
    {
        var turbo = new TurboPumpEmulator("TP1", 0, 0, 0, 50f, "Test turbo pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        var state = TestHelpers.CreateTestState();

        turbo.Command(true);

        float dt = 0.1f;
        for (float t = 0; t < 35f; t += dt)
        {
            // Reset PumpSpeed each tick so it doesn't accumulate across ticks
            state.Chambers["Main"].PumpSpeed = 0f;
            turbo.Update(dt, state);
        }

        Assert.True(turbo.IsRunning);
        Assert.True(turbo.IsAtSpeed);
    }

    [Fact]
    public void TurboPump_ContributesSpeedToMainChamber()
    {
        var turbo = new TurboPumpEmulator("TP1", 0, 0, 0, 50f, "Test turbo pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        var state = TestHelpers.CreateTestState();
        state.ValveStates["GVM"] = true;
        state.ValveStates["FL"] = true;  // turbo needs backing

        turbo.Command(true);

        // Set pressure well below crossover so turbo is effective
        state.Chambers["Main"].Pressure = 0.001f;

        float dt = 0.1f;
        for (float t = 0; t < 35f; t += dt)
        {
            state.Chambers["Main"].PumpSpeed = 0f;
            turbo.Update(dt, state);
        }

        // crossoverFactor = 1 / (1 + 0.001/0.5) ≈ 0.998, so speed ≈ 50 * 0.998 ≈ 49.9
        Assert.True(state.Chambers["Main"].PumpSpeed > 49f,
            $"Expected pump speed > 49, got {state.Chambers["Main"].PumpSpeed}");
    }

    [Fact]
    public void TurboPump_SpinsDown_WhenCommandedOff()
    {
        var turbo = new TurboPumpEmulator("TP1", 0, 0, 0, 50f, "Test turbo pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        var state = TestHelpers.CreateTestState();

        turbo.Command(true);
        float dt = 0.1f;
        for (float t = 0; t < 35f; t += dt)
        {
            state.Chambers["Main"].PumpSpeed = 0f;
            turbo.Update(dt, state);
        }
        Assert.True(turbo.IsAtSpeed);

        // full spindown takes 30s * 0.5 = 15s
        turbo.Command(false);
        for (float t = 0; t < 20f; t += dt)
        {
            state.Chambers["Main"].PumpSpeed = 0f;
            turbo.Update(dt, state);
        }

        Assert.False(turbo.IsRunning);
    }

    [Fact]
    public void TurboPump_NoEffectAboveCrossover()
    {
        var tp = new TurboPumpEmulator("TP", 17, 0, 0, 300f, "Turbo",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        tp.Command(true);
        var state = TestHelpers.CreateTestState();

        for (int i = 0; i < 350; i++) tp.Update(0.1f, state);

        state.Chambers["Main"].PumpSpeed = 0f;
        state.Chambers["Main"].Pressure = 10f;
        tp.Update(0.1f, state);
        Assert.Equal(0f, state.Chambers["Main"].PumpSpeed);
    }
}
