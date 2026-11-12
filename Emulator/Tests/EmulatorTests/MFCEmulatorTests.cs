using APS.Emulator.Devices.S7;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class MFCEmulatorTests
{
    [Fact]
    public void MFC_Setpoint40_SettlesNear40()
    {
        var mfc = new MFCEmulator("MFC1", 0, "Test MFC", "Main");
        var state = TestHelpers.CreateTestState();

        mfc.WriteSetpoint(40f);

        float dt = 0.1f;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }

        Assert.InRange(mfc.ActualFlow, 39.5f, 40.5f);
    }

    [Fact]
    public void MFC_ZeroSetpoint_Decays()
    {
        var mfc = new MFCEmulator("MFC1", 0, "Test MFC", "Main");
        var state = TestHelpers.CreateTestState();

        mfc.WriteSetpoint(40f);
        float dt = 0.1f;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }

        float flowBefore = mfc.ActualFlow;
        Assert.True(flowBefore > 39f);

        mfc.WriteSetpoint(0f);
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }

        Assert.True(mfc.ActualFlow < 1f, $"Expected flow < 1, got {mfc.ActualFlow}");
    }

    [Fact]
    public void MFC_ContributesToChamberGasLoad()
    {
        var mfc = new MFCEmulator("MFC1", 0, "Test MFC", "Main");
        var state = TestHelpers.CreateTestState();

        mfc.WriteSetpoint(40f);

        state.Chambers["Main"].GasLoad = 0f;
        mfc.Update(0.1f, state);

        Assert.True(state.Chambers["Main"].GasLoad > 0f);
    }

    [Fact]
    public void MFC_InletValveClosed_FlowDecaysToZero_NoGasLoad()
    {
        var mfc = new MFCEmulator("MFC1", 0, "Test MFC", "Main", inletValveId: "V1");
        var state = TestHelpers.CreateTestState();
        state.ValveStates["V1"] = true;

        mfc.WriteSetpoint(40f);
        float dt = 0.1f;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }
        Assert.InRange(mfc.ActualFlow, 39.5f, 40.5f);

        // Close the inlet valve — setpoint stays at 40, flow must die anyway
        state.ValveStates["V1"] = false;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }

        Assert.True(mfc.ActualFlow < 1f, $"Expected flow < 1 with inlet valve closed, got {mfc.ActualFlow}");
        Assert.True(state.Chambers["Main"].GasLoad < 1f,
            $"Expected gas load < 1 with inlet valve closed, got {state.Chambers["Main"].GasLoad}");
    }

    [Fact]
    public void MFC_ManifoldList_FlowsWhileAnyListedValveOpen()
    {
        // Four MFCs share a manifold behind inlets V1-V4 —
        // any open inlet admits the line's gas.
        var mfc = new MFCEmulator("MFC1", 0, "Test MFC", "Main", inletValveId: "V1,V2");
        var state = TestHelpers.CreateTestState();
        state.ValveStates["V1"] = false;
        state.ValveStates["V2"] = true;

        mfc.WriteSetpoint(40f);
        float dt = 0.1f;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }
        Assert.InRange(mfc.ActualFlow, 39.5f, 40.5f);

        state.ValveStates["V2"] = false;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }
        Assert.True(mfc.ActualFlow < 1f, $"Expected flow < 1 with all inlets closed, got {mfc.ActualFlow}");
    }

    [Fact]
    public void MFC_InletValveNeverRegistered_TreatedAsClosed()
    {
        // Valve id configured but absent from ValveStates (e.g. valve never updated yet,
        // or a typo in HWConf.Emulator.xml) — fail safe: no flow.
        var mfc = new MFCEmulator("MFC1", 0, "Test MFC", "Main", inletValveId: "NOPE");
        var state = TestHelpers.CreateTestState();

        mfc.WriteSetpoint(40f);
        float dt = 0.1f;
        for (float t = 0; t < 5f; t += dt)
        {
            state.Chambers["Main"].GasLoad = 0f;
            mfc.Update(dt, state);
        }

        Assert.True(mfc.ActualFlow < 0.5f, $"Expected no flow for unknown inlet valve, got {mfc.ActualFlow}");
    }
}
