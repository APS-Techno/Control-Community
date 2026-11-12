using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Panels;
using APS.Emulator.Devices.S7;
using EmulatorTests.Helpers;

namespace EmulatorTests.Panels;

public class PumpsPanelTests
{
    private static SimulationEngine MakeEngineWithPumps(params (string id, bool turbo)[] pumps)
    {
        var engine = new SimulationEngine();
        foreach (var (id, turbo) in pumps)
        {
            if (turbo)
                engine.AddDevice(new TurboPumpEmulator(id, 0, 0, 0, 1000f, "Turbo " + id,
                    chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL"));
            else
                engine.AddDevice(new PumpEmulator(id, 0, 0, 10f, "Scroll " + id,
                    chamberId: "Main", gateValveId: ""));
        }
        return engine;
    }

    [Fact]
    public void NoEngine_HasContent_IsFalse()
    {
        var panel = new PumpsPanel();
        Assert.False(panel.HasContent);
    }

    [Fact]
    public void Attach_ZeroPumps_HasContent_IsFalse()
    {
        var panel = new PumpsPanel();
        panel.AttachEngine(new SimulationEngine());
        Assert.False(panel.HasContent);
        Assert.Equal(0, panel.RowCount);
    }

    [Fact]
    public void Attach_ThreePumps_RowCount_IsThree()
    {
        var panel = new PumpsPanel();
        panel.AttachEngine(MakeEngineWithPumps(("TP", true), ("SP1", false), ("SP2", false)));
        Assert.True(panel.HasContent);
        Assert.Equal(3, panel.RowCount);
    }

    [Fact]
    public void Click_TogglesPumpRunningState()
    {
        var engine = MakeEngineWithPumps(("SP1", false));
        var panel = new PumpsPanel();
        panel.AttachEngine(engine);

        var pump = (PumpEmulator)engine.Devices.Single(d => d.Id == "SP1");
        Assert.False(pump.IsRunning);

        panel.SimulateRowClick("SP1");

        Assert.True(pump.IsRunning);
    }

    [Fact]
    public void Click_TogglesTurboCommandState()
    {
        var engine = MakeEngineWithPumps(("TP", true));
        var panel = new PumpsPanel();
        panel.AttachEngine(engine);

        var turbo = (TurboPumpEmulator)engine.Devices.Single(d => d.Id == "TP");

        panel.SimulateRowClick("TP");

        // Tick once so the commanded->speed transition begins.
        // Use 0.5s: at default spinUpTime=30s this yields speedFraction ~0.0167,
        // which crosses the IsRunning threshold (0.01). 0.1s only yields 0.0033,
        // below threshold.
        turbo.Update(0.5f, TestHelpers.CreateTestState());
        Assert.True(turbo.IsRunning);
    }

    [Fact]
    public void Click_InReplayMode_DoesNotToggle()
    {
        var engine = MakeEngineWithPumps(("SP1", false));
        engine.SetDataSource(new ReplayStub());
        var panel = new PumpsPanel();
        panel.AttachEngine(engine);
        var pump = (PumpEmulator)engine.Devices.Single(d => d.Id == "SP1");

        panel.SimulateRowClick("SP1");

        Assert.False(pump.IsRunning);
    }

    private sealed class ReplayStub : EmulatorCore.Interfaces.IDataSource
    {
        public EmulatorCore.Interfaces.DataSourceMode Mode => EmulatorCore.Interfaces.DataSourceMode.LogReplay;
        public bool TryGetValue(string deviceId, string property, float simTime, out float value)
        {
            value = 0f;
            return false;
        }
    }

    [Fact]
    public void Reattach_RebuildsRows()
    {
        var panel = new PumpsPanel();
        panel.AttachEngine(MakeEngineWithPumps(("SP1", false)));
        Assert.Equal(1, panel.RowCount);

        panel.AttachEngine(MakeEngineWithPumps(("TP", true), ("SP1", false), ("SP2", false)));
        Assert.Equal(3, panel.RowCount);
    }
}
