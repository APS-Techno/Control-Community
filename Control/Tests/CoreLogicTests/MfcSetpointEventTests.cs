using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

/// <summary>
/// BaseMFC caches its last-written setpoint and raises SetpointChanged so
/// Core can re-evaluate group transitions when an MFC is zeroed or set.
/// </summary>
public class MfcSetpointEventTests
{
    private sealed class FakeMfc : BaseMFC
    {
        public FakeMfc() : base(new BaseDeviceParams("MFC1", 100, "test")) { Connected = true; }
        protected override void WriteHW(float newvalue) { }
    }

    [Fact]
    public void Setpoint_defaults_to_zero()
    {
        Assert.Equal(0f, new FakeMfc().Setpoint);
    }

    [Fact]
    public void SetFlow_updates_setpoint()
    {
        var m = new FakeMfc();
        m.SetFlow(5.2f);
        Assert.Equal(5.2f, m.Setpoint);
    }

    [Fact]
    public void SetFlow_raises_SetpointChanged_on_change()
    {
        var m = new FakeMfc();
        int fires = 0;
        m.SetpointChanged += _ => fires++;
        m.SetFlow(5f);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void SetFlow_does_not_raise_when_value_unchanged()
    {
        var m = new FakeMfc();
        m.SetFlow(5f);
        int fires = 0;
        m.SetpointChanged += _ => fires++;
        m.SetFlow(5f);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void SetFlow_when_disconnected_does_not_update_setpoint()
    {
        var m = new FakeMfc { Connected = false };
        m.SetFlow(3f);
        Assert.Equal(0f, m.Setpoint);
    }
}
