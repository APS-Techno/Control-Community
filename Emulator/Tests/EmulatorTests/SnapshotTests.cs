using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class SnapshotTests
{
    [Fact]
    public void ValveEmulator_RoundTrips()
    {
        var valve = new ValveEmulator("GV1", 0, 3, "Gate Valve", openDelay: 0.5f);
        valve.SetState(true);

        var snap = valve.GetSnapshot();

        var valve2 = new ValveEmulator("GV1", 0, 3, "Gate Valve", openDelay: 0.5f);
        valve2.RestoreSnapshot(snap);

        var snap2 = valve2.GetSnapshot();
        Assert.Equal(snap.ToJsonString(), snap2.ToJsonString());
    }

    [Fact]
    public void ValveEmulator_RoundTrips_ClosedState()
    {
        var valve = new ValveEmulator("GV2", 1, 0, "Vent Valve");
        // Valve stays closed (default)

        var snap = valve.GetSnapshot();
        var valve2 = new ValveEmulator("GV2", 1, 0, "Vent Valve");
        valve2.RestoreSnapshot(snap);

        var snap2 = valve2.GetSnapshot();
        Assert.Equal(snap.ToJsonString(), snap2.ToJsonString());
        Assert.False(valve2.IsOpen);
    }

    [Fact]
    public void MFCEmulator_RoundTrips()
    {
        var mfc = new MFCEmulator("Ar1", 160, "Argon MFC", "Main");
        var state = TestHelpers.CreateTestState();

        mfc.WriteSetpoint(20f);
        // Run a few ticks so actualFlow diverges from setpoint
        for (float t = 0; t < 0.5f; t += 0.1f)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(0.1f, state);
        }

        float flowBeforeSnap = mfc.ActualFlow;
        var snap = mfc.GetSnapshot();

        var mfc2 = new MFCEmulator("Ar1", 160, "Argon MFC", "Main");
        mfc2.RestoreSnapshot(snap);

        Assert.Equal(20f, mfc2.Setpoint);
        Assert.Equal(flowBeforeSnap, mfc2.ActualFlow, precision: 4);

        var snap2 = mfc2.GetSnapshot();
        Assert.Equal(snap.ToJsonString(), snap2.ToJsonString());
    }

    [Fact]
    public void TurboPumpEmulator_RoundTrips()
    {
        var turbo = new TurboPumpEmulator("TP1", dbAddress: 100, writeAddr: 0, writeIndex: 4,
            fullSpeed: 800f, description: "Main Turbo",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        turbo.Command(true);
        var state = TestHelpers.CreateTestState();

        // Spin up partway
        for (float t = 0; t < 3f; t += 0.2f)
            turbo.Update(0.2f, state);

        var snap = turbo.GetSnapshot();

        var turbo2 = new TurboPumpEmulator("TP1", dbAddress: 100, writeAddr: 0, writeIndex: 4,
            fullSpeed: 800f, description: "Main Turbo",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        turbo2.RestoreSnapshot(snap);

        var snap2 = turbo2.GetSnapshot();
        Assert.Equal(snap.ToJsonString(), snap2.ToJsonString());
    }

    [Fact]
    public void SnapshotService_CaptureRestore_RoundTrips()
    {
        var state = TestHelpers.CreateTestState();
        state.Chambers["Main"].Pressure = 0.05f;
        state.SimTime = 42f;
        state.ValveStates["GV1"] = true;
        state.Thermal.Temperature = 120f;

        var mfc = new MFCEmulator("Ar1", 160, "Argon MFC", "Main");
        mfc.WriteSetpoint(30f);
        var state2 = TestHelpers.CreateTestState();
        for (float t = 0; t < 1f; t += 0.1f)
        {
            state2.Chambers["Main"].GasLoad = 0f;
            mfc.Update(0.1f, state2);
        }

        IReadOnlyList<IDeviceEmulator> devices = [mfc];
        var snap = SnapshotService.Capture(state, devices);

        var freshState = TestHelpers.CreateTestState();
        var freshMfc = new MFCEmulator("Ar1", 160, "Argon MFC", "Main");
        IReadOnlyList<IDeviceEmulator> freshDevices = [freshMfc];

        SnapshotService.Restore(snap, freshState, freshDevices);

        Assert.Equal(0.05f, freshState.Chambers["Main"].Pressure);
        Assert.Equal(42f, freshState.SimTime);
        Assert.True(freshState.ValveStates["GV1"]);
        Assert.Equal(120f, freshState.Thermal.Temperature);
        Assert.Equal(mfc.ActualFlow, freshMfc.ActualFlow, precision: 4);
        Assert.Equal(30f, freshMfc.Setpoint);
    }

    [Fact]
    public void SnapshotService_SaveLoadFile_RoundTrips()
    {
        var state = TestHelpers.CreateTestState();
        state.Chambers["Main"].Pressure = 1.23e-4f;
        state.Chambers["Loadlock"].Pressure = 5.0f;
        state.SimTime = 99f;

        var mfc = new MFCEmulator("Ar1", 160, "Argon MFC", "Main");
        mfc.WriteSetpoint(10f);
        IReadOnlyList<IDeviceEmulator> devices = [mfc];

        var snap = SnapshotService.Capture(state, devices);

        string tempFile = Path.Combine(Path.GetTempPath(), $"aps_snap_{Guid.NewGuid():N}.json");
        try
        {
            SnapshotService.SaveToFile(snap, tempFile);
            Assert.True(File.Exists(tempFile), "Snapshot file was not created.");

            var loaded = SnapshotService.LoadFromFile(tempFile);

            Assert.Equal(snap.SimTime, loaded.SimTime);
            Assert.Equal(snap.Chambers["Main"].Pressure, loaded.Chambers["Main"].Pressure);
            Assert.Equal(snap.Chambers["Loadlock"].Pressure, loaded.Chambers["Loadlock"].Pressure);
            Assert.True(loaded.DeviceStates.ContainsKey("Ar1"));

            var freshState = TestHelpers.CreateTestState();
            var freshMfc = new MFCEmulator("Ar1", 160, "Argon MFC", "Main");
            IReadOnlyList<IDeviceEmulator> freshDevices = [freshMfc];
            SnapshotService.Restore(loaded, freshState, freshDevices);

            Assert.Equal(10f, freshMfc.Setpoint);
            Assert.Equal(99f, freshState.SimTime);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
