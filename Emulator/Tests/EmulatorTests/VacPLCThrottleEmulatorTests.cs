using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class VacPLCThrottleEmulatorTests
{
    private const int Addr = 200;
    private const string Chamber = "C1";
    private const string Host = "c1";

    private static VacPLCThrottleEmulator NewTV() =>
        new("C1_TV", Addr, "Test PLC throttle", Chamber, Host);

    private static SimulationState NewState()
    {
        var state = TestHelpers.CreateTestState();
        if (!state.Chambers.ContainsKey(Chamber))
        {
            state.SetChamber(new ChamberState
            {
                Id = Chamber,
                Type = ChamberType.Process,
                Pressure = 0.05f,
                Volume = 20f,
                PumpSpeed = 250f,
                BasePressure = 2.2e-4f,
                BaseLeakRate = 1e-5f,
                LeakRate = 1e-5f,
            });
        }
        return state;
    }

    private static void WriteSetpoint(VacPLCThrottleEmulator tv, float torr)
    {
        tv.WriteSetpointForTest(torr);
    }

    [Fact]
    public void Setpoint_zero_drives_position_to_full_open()
    {
        var tv = NewTV();
        var state = NewState();
        tv.SetPositionForTest(20f);
        WriteSetpoint(tv, 0f);
        for (int i = 0; i < 100; i++) tv.Update(0.1f, state);
        Assert.Equal(100f, tv.Position, precision: 1);
    }

    [Fact]
    public void Setpoint_above_close_sentinel_drives_position_toward_min()
    {
        var tv = NewTV();
        var state = NewState();
        tv.SetPositionForTest(100f);
        WriteSetpoint(tv, 9999f);
        // Closing is intentionally slow — give it plenty of sim time.
        for (int i = 0; i < 600; i++) tv.Update(0.1f, state);
        Assert.True(tv.Position <= 1f, $"Expected near-closed, got {tv.Position}%");
    }

    [Fact]
    public void Regulating_setpoint_moves_position_toward_target_pressure()
    {
        var tv = NewTV();
        var state = NewState();
        state.Chambers[Chamber].Pressure = 0.05f; // 50 mTorr
        tv.SetPositionForTest(20f);
        WriteSetpoint(tv, 0.005f);                 // 5 mTorr target
        for (int i = 0; i < 100; i++) tv.Update(0.1f, state);
        Assert.True(tv.Position > 30f,
            $"Expected throttle to open toward target, got {tv.Position}%");
    }

    [Fact]
    public void Update_writes_position_into_chamber_state()
    {
        var tv = NewTV();
        var state = NewState();
        tv.SetPositionForTest(42f);
        WriteSetpoint(tv, 0f);  // stay near 42 for one tick by using 0 sentinel and very small dt
        tv.Update(0.001f, state);
        Assert.InRange(state.Chambers[Chamber].ThrottlePosition, 41.9f, 100f);
    }

    [Fact]
    public void Snapshot_round_trips_position_setpoint_pid_state()
    {
        var tv = NewTV();
        var state = NewState();
        tv.SetPositionForTest(55f);
        WriteSetpoint(tv, 0.003f);
        for (int i = 0; i < 5; i++) tv.Update(0.1f, state);

        var snap = tv.GetSnapshot();
        var tv2 = NewTV();
        tv2.RestoreSnapshot(snap);

        Assert.Equal(tv.Position, tv2.Position, precision: 3);
        Assert.Equal(tv.LastSetpoint, tv2.LastSetpoint, precision: 3);
    }

    [Fact]
    public void Reset_returns_to_defaults()
    {
        var tv = NewTV();
        tv.SetPositionForTest(33f);
        WriteSetpoint(tv, 0.001f);
        tv.Reset();
        Assert.Equal(100f, tv.Position);
        Assert.Equal(0f, tv.LastSetpoint);
    }
}
