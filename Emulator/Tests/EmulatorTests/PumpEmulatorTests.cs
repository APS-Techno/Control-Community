using APS.Emulator.Devices.S7;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class PumpEmulatorTests
{
    [Fact]
    public void Pump_DefaultState_IsOff()
    {
        var pump = new PumpEmulator("P1", 0, 0, 10f, "Test pump",
            chamberId: "Main", gateValveId: "");
        Assert.False(pump.IsRunning);
    }

    [Fact]
    public void Pump_WhenRunning_ContributesPumpSpeed()
    {
        var pump = new PumpEmulator("P1", 0, 0, 10f, "Test pump",
            chamberId: "Main", gateValveId: "");
        var state = TestHelpers.CreateTestState();

        // At atmospheric pressure (760 Torr), pump should be near full speed
        pump.SetRunning(true);
        pump.Update(1.0f, state);

        // S(P) = 10 * 760 / (760 + 0.1) ≈ 9.9987
        Assert.True(state.Chambers["Main"].PumpSpeed > 9.9f,
            $"Expected chamber pump speed > 9.9, got {state.Chambers["Main"].PumpSpeed}");
    }

    [Fact]
    public void Pump_WhenNotRunning_DoesNotContributePumpSpeed()
    {
        var pump = new PumpEmulator("P1", 0, 0, 10f, "Test pump",
            chamberId: "Main", gateValveId: "");
        var state = TestHelpers.CreateTestState();

        pump.Update(1.0f, state);

        Assert.Equal(0f, state.Chambers["Main"].PumpSpeed);
    }

    [Fact]
    public void Pump_SpeedDropsAtLowPressure()
    {
        var pump = new PumpEmulator("SP1", 0, 5, 10f, "Test",
            chamberId: "Main", gateValveId: "");
        pump.SetRunning(true);
        var state = TestHelpers.CreateTestState();

        // At high pressure, pump is near full speed
        state.Chambers["Main"].Pressure = 760f;
        pump.Update(0.1f, state);
        float highPSpeed = state.Chambers["Main"].PumpSpeed;

        // At low pressure, pump speed is much lower
        state.Chambers["Main"].PumpSpeed = 0f;
        state.Chambers["Main"].Pressure = 0.01f;
        pump.Update(0.1f, state);
        float lowPSpeed = state.Chambers["Main"].PumpSpeed;

        Assert.True(highPSpeed > lowPSpeed * 5, $"High P speed ({highPSpeed}) should be >> low P speed ({lowPSpeed})");
    }

    [Fact]
    public void Pump_GateValveClosed_DoesNotContributePumpSpeed()
    {
        var pump = new PumpEmulator("SP1", 0, 5, 10f, "Test",
            chamberId: "Main", gateValveId: "PP");
        pump.SetRunning(true);
        var state = TestHelpers.CreateTestState();

        // PP valve not in ValveStates → defaults to false (closed)
        pump.Update(1.0f, state);

        Assert.Equal(0f, state.Chambers["Main"].PumpSpeed);
    }

    [Fact]
    public void Pump_GateValveOpen_ContributesPumpSpeed()
    {
        var pump = new PumpEmulator("SP1", 0, 5, 10f, "Test",
            chamberId: "Main", gateValveId: "PP");
        pump.SetRunning(true);
        var state = TestHelpers.CreateTestState();

        state.ValveStates["PP"] = true;
        pump.Update(1.0f, state);

        Assert.True(state.Chambers["Main"].PumpSpeed > 9.9f,
            $"Expected chamber pump speed > 9.9, got {state.Chambers["Main"].PumpSpeed}");
    }

    [Fact]
    public void Pump_LoadlockChamber_ContributesToCorrectedChamber()
    {
        var pump = new PumpEmulator("SP2", 0, 6, 10f, "LL pump",
            chamberId: "Loadlock", gateValveId: "");
        pump.SetRunning(true);
        var state = TestHelpers.CreateTestState();

        pump.Update(1.0f, state);

        Assert.True(state.Chambers["Loadlock"].PumpSpeed > 9.9f,
            $"Expected loadlock pump speed > 9.9, got {state.Chambers["Loadlock"].PumpSpeed}");
        Assert.Equal(0f, state.Chambers["Main"].PumpSpeed);
    }
}
