using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Panels;
using APS.Emulator.Devices.S7;

namespace EmulatorTests.Panels;

public class ValvesPanelTests
{
    private static SimulationEngine MakeEngine(int regular, int input)
    {
        var engine = new SimulationEngine();
        for (int i = 0; i < regular; i++)
            engine.AddDevice(new ValveEmulator($"V{i + 1}", 0, 0, "Test valve " + i));
        for (int i = 0; i < input; i++)
            engine.AddDevice(new InputValveEmulator($"GV{i + 1}", 0, 0, "Test input valve " + i));
        return engine;
    }

    [Fact]
    public void NoEngine_HasContent_IsFalse()
    {
        var panel = new ValvesPanel();
        Assert.False(panel.HasContent);
    }

    [Fact]
    public void Attach_NoValves_HasContent_IsFalse()
    {
        var panel = new ValvesPanel();
        panel.AttachEngine(new SimulationEngine());
        Assert.False(panel.HasContent);
        Assert.Equal(0, panel.ChipCount);
    }

    [Fact]
    public void Attach_MixedValves_RendersAll()
    {
        var panel = new ValvesPanel();
        panel.AttachEngine(MakeEngine(regular: 3, input: 2));
        Assert.Equal(5, panel.ChipCount);
        Assert.True(panel.HasContent);
    }

    [Fact]
    public void Click_TogglesRegularValve()
    {
        var engine = MakeEngine(regular: 1, input: 0);
        var panel = new ValvesPanel();
        panel.AttachEngine(engine);
        var valve = engine.Devices.OfType<ValveEmulator>().Single();
        Assert.False(valve.IsOpen);

        panel.SimulateChipClick(valve.Id);

        Assert.True(valve.IsOpen);
    }

    [Fact]
    public void Click_TogglesInputValve()
    {
        var engine = MakeEngine(regular: 0, input: 1);
        var panel = new ValvesPanel();
        panel.AttachEngine(engine);
        var valve = engine.Devices.OfType<InputValveEmulator>().Single();
        Assert.False(valve.IsOpen);

        panel.SimulateChipClick(valve.Id);

        Assert.True(valve.IsOpen);
    }

    [Fact]
    public void Click_InReplayMode_DoesNotToggle()
    {
        var engine = MakeEngine(regular: 1, input: 0);
        engine.SetDataSource(new ReplayStub());
        var panel = new ValvesPanel();
        panel.AttachEngine(engine);
        var valve = engine.Devices.OfType<ValveEmulator>().Single();

        panel.SimulateChipClick(valve.Id);

        Assert.False(valve.IsOpen);
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
    public void Reattach_RebuildsChips()
    {
        var panel = new ValvesPanel();
        panel.AttachEngine(MakeEngine(regular: 3, input: 0));
        Assert.Equal(3, panel.ChipCount);
        panel.AttachEngine(MakeEngine(regular: 1, input: 1));
        Assert.Equal(2, panel.ChipCount);
    }
}
