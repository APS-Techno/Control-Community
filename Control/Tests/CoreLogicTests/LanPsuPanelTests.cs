using APS.Controls.Generic;
using APS.Hardware.Demo;
using CoreLogicTests.Fixtures;

namespace CoreLogicTests;

public class LanPsuPanelTests
{
    private static LanPsuPanel Bound(string mode, string id = "PSU1")
    {
        var hardware = new StubMenuContext.StubHardware();
        hardware.Devices[id] = new LanPowerSupply(id, "127.0.0.1", 5025, mode, "test supply");
        var panel = new LanPsuPanel { ID = id };
        panel.OnHardwareAvailable(hardware);
        return panel;
    }

    private static List<object[]> Capture(LanPsuPanel panel)
    {
        var sent = new List<object[]>();
        panel.SendCommandEvent += (_, e) => sent.Add(e.Data);
        return sent;
    }

    [Fact]
    public void RfSupply_HidesFrequencyAndDuty()
    {
        using var panel = Bound("RF");
        Assert.False(panel.FrequencyInput.Visible);
        Assert.False(panel.DutyInput.Visible);
        Assert.True(panel.PowerInput.Visible);
    }

    [Fact]
    public void PdcSupply_ShowsFrequencyAndDuty()
    {
        using var panel = Bound("PDC");
        Assert.True(panel.FrequencyInput.Visible);
        Assert.True(panel.DutyInput.Visible);
        Assert.True(panel.PowerInput.Visible);
    }

    [Fact]
    public void UnboundPanel_HidesFrequencyAndDuty()
    {
        using var panel = new LanPsuPanel();
        Assert.False(panel.FrequencyInput.Visible);
        Assert.False(panel.DutyInput.Visible);
    }

    [Fact]
    public void MissingDevice_LeavesFrequencyAndDutyHidden()
    {
        using var panel = new LanPsuPanel { ID = "absent" };
        panel.OnHardwareAvailable(new StubMenuContext.StubHardware());
        Assert.False(panel.FrequencyInput.Visible);
        Assert.False(panel.DutyInput.Visible);
    }

    [Fact]
    public void PowerInput_SendsSetPowerAsFloat()
    {
        using var panel = Bound("RF");
        var sent = Capture(panel);
        panel.PowerInput.Value = 250;
        var cmd = Assert.Single(sent);
        Assert.Equal(new object[] { "SetPower", 250f }, cmd);
    }

    [Fact]
    public void FrequencyAndDutyInputs_SendIntegerCommands()
    {
        using var panel = Bound("PDC");
        var sent = Capture(panel);
        panel.FrequencyInput.Value = 120;
        panel.DutyInput.Value = 40;
        Assert.Equal(2, sent.Count);
        Assert.Equal(new object[] { "SetFrequency", 120 }, sent[0]);
        Assert.Equal(new object[] { "SetDutyCycle", 40 }, sent[1]);
    }

    [Fact]
    public void OutputButton_TogglesOutputOnAndOff()
    {
        using var panel = Bound("RF");
        var sent = Capture(panel);
        panel.OutputButton.PerformClick();
        panel.OutputButton.PerformClick();
        Assert.Equal(2, sent.Count);
        Assert.Equal(new object[] { "OutputOn" }, sent[0]);
        Assert.Equal(new object[] { "OutputOff" }, sent[1]);
    }

    [Fact]
    public void Update_ShowsReadbackWithoutRaisingCommands()
    {
        using var panel = Bound("PDC");
        var sent = Capture(panel);
        panel.Update([312.456f, 1.25f, 390f, true]);
        Assert.Equal("312.5", panel.VoltageDisplay.Text);
        Assert.Equal("1.25", panel.CurrentDisplay.Text);
        Assert.Equal("390", panel.PowerDisplay.Text);
        Assert.True(panel.OutputButton.Selected);
        Assert.Empty(sent);

        panel.Update([0f, 0f, 0f, false]);
        Assert.False(panel.OutputButton.Selected);
    }

    [Fact]
    public void Update_IgnoresShortReadback()
    {
        using var panel = Bound("RF");
        panel.Update([1f, 2f]);
        Assert.Equal("0.0", panel.VoltageDisplay.Text);
    }

    [Fact]
    public void Update_SilentSupply_TurnsComOffAndDimsReadouts()
    {
        using var panel = Bound("PDC");
        panel.Update([400f, 1f, 400f, true, true]);
        Assert.True(panel.ComLed.LedOn);
        var live = panel.PowerDisplay.ForeColor;

        panel.Update([400f, 1f, 400f, true, false]);
        Assert.False(panel.ComLed.LedOn);
        Assert.Equal(APS.Controls.Theme.VacuumTheme.TextDim, panel.VoltageDisplay.ForeColor);
        Assert.Equal(APS.Controls.Theme.VacuumTheme.TextDim, panel.CurrentDisplay.ForeColor);
        Assert.Equal(APS.Controls.Theme.VacuumTheme.TextDim, panel.PowerDisplay.ForeColor);

        panel.Update([400f, 1f, 400f, true, true]);
        Assert.True(panel.ComLed.LedOn);
        Assert.Equal(live, panel.PowerDisplay.ForeColor);
    }

    [Fact]
    public void ReportsPowerDeviceType()
    {
        using var panel = new LanPsuPanel();
        Assert.Equal(APS.Controls.Core.DeviceType.Power, panel.DeviceType);
    }

    [Fact]
    public void Seeding_ShowsDeviceSetpointsWithoutRaisingCommands()
    {
        var supply = new LanPowerSupplyStub("PSU1");
        var hardware = new StubMenuContext.StubHardware();
        hardware.Devices["PSU1"] = supply;
        using var panel = new LanPsuPanel { ID = "PSU1" };
        var sent = Capture(panel);

        panel.OnHardwareAvailable(hardware);
        Assert.Equal(1m, panel.FrequencyInput.Value);

        supply.Connected = true;
        panel.Update([0f, 0f, 0f, false]);

        Assert.Equal(300m, panel.PowerInput.Value);
        Assert.Equal(100m, panel.FrequencyInput.Value);
        Assert.Equal(40m, panel.DutyInput.Value);
        Assert.Empty(sent);

        panel.FrequencyInput.Value = 101;
        Assert.Single(sent);
    }

    [Fact]
    public void Update_FollowsSetpointChangedOutsidePanel()
    {
        var supply = new LanPowerSupplyStub("PSU1") { Connected = true };
        var hardware = new StubMenuContext.StubHardware();
        hardware.Devices["PSU1"] = supply;
        using var panel = new LanPsuPanel { ID = "PSU1" };
        panel.OnHardwareAvailable(hardware);
        var sent = Capture(panel);
        Assert.Equal(300m, panel.PowerInput.Value);

        supply.PowerSetpoint = 400f;
        panel.Update([0f, 0f, 0f, false]);

        Assert.Equal(400m, panel.PowerInput.Value);
        Assert.Empty(sent);
    }
}

internal class LanPowerSupplyStub : APS.SystemControl.BaseHWClasses.BasePowerSupply
{
    public LanPowerSupplyStub(string id)
        : base(APS.SystemControl.Core.HWClasses.Base.ParamsConverter.Params(id, 0, "stub")) { }

    public override IReadOnlyCollection<string> SetpointParameterNames => ["Power", "Frequency", "DutyCycle"];

    public float PowerSetpoint { get; set; } = 300f;

    public override bool TryGetSetpoint(string parameter, out float value)
    {
        value = parameter switch { "Power" => PowerSetpoint, "Frequency" => 100f, "DutyCycle" => 40f, _ => 0f };
        return Connected;
    }
}
