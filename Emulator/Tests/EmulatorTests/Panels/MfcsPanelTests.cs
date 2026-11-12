using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Panels;
using APS.Emulator.Devices.S7;

namespace EmulatorTests.Panels;

public class MfcsPanelTests
{
    private static SimulationEngine MakeEngineWithMfcs(int count)
    {
        var engine = new SimulationEngine();
        for (int i = 0; i < count; i++)
            engine.AddDevice(new MFCEmulator($"MFC{i + 1}", 0, $"MFC #{i + 1}", "Main"));
        return engine;
    }

    [Fact]
    public void NoEngine_HasContent_IsFalse()
    {
        var panel = new MfcsPanel();
        Assert.False(panel.HasContent);
    }

    [Fact]
    public void Attach_ZeroMfcs_HasContent_IsFalse()
    {
        var panel = new MfcsPanel();
        panel.AttachEngine(new SimulationEngine());
        Assert.False(panel.HasContent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Attach_NMfcs_ColumnCountMatches(int n)
    {
        var panel = new MfcsPanel();
        panel.AttachEngine(MakeEngineWithMfcs(n));
        Assert.True(panel.HasContent);
        Assert.Equal(n, panel.MfcColumnCount);
    }

    [Fact]
    public void Reattach_RebuildsColumns()
    {
        var panel = new MfcsPanel();
        panel.AttachEngine(MakeEngineWithMfcs(4));
        Assert.Equal(4, panel.MfcColumnCount);

        panel.AttachEngine(MakeEngineWithMfcs(2));
        Assert.Equal(2, panel.MfcColumnCount);
    }
}
