using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Tests.Overrides;

public class IdSynthesisTests
{
    [Theory]
    [InlineData("pl", SystemState.Processing,  1, 0.05f, "alarm:Main|pl|Processing|>|0.05")]
    [InlineData("pl", SystemState.Idle,       -1, 1e-4f, "alarm:Main|pl|Idle|<|0.0001")]
    [InlineData("pm", SystemState.Startup,     0, 0f,    "alarm:Main|pm|Startup|=|0")]
    public void AlarmId_Is_Stable_And_Canonical(string sensor, SystemState state, int condition, float value, string expected)
    {
        var id = OverrideIds.Alarm("Main", sensor, state, condition, value);
        Assert.Equal(expected, id);
    }

    [Fact]
    public void AlarmLabel_Shows_Message_Then_Rule()
    {
        var label = OverrideIds.AlarmLabel("Main", "Pressure spike", "pl", SystemState.Processing, 1, 0.05f);
        Assert.Equal("Pressure spike — pl > 0.05 in Processing", label);
    }

    [Fact]
    public void AlarmId_Changes_When_Any_Field_Changes()
    {
        var id1 = OverrideIds.Alarm("Main", "pl", SystemState.Processing, 1, 0.05f);
        var id2 = OverrideIds.Alarm("Main", "pl", SystemState.Processing, 1, 0.06f);
        var id3 = OverrideIds.Alarm("Main", "pm", SystemState.Processing, 1, 0.05f);
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void Alarm_id_includes_chamber_prefix()
    {
        // Chamber id is the FIRST segment after the type prefix. All other
        // segments retain their legacy order ({sensor}|{state}|{op}|{value})
        // so the back-compat reader only has to strip the chamber segment.
        Assert.Equal("alarm:Main|pm|Idle|>|0.0001",
            OverrideIds.Alarm("Main", "pm", SystemState.Idle, 1, 1e-4f));
    }

    [Fact]
    public void DeviceStatus_id_includes_chamber_prefix()
    {
        Assert.Equal("devstatus:Main|TP|5|Idle",
            OverrideIds.DeviceStatus("Main", "TP", 5, SystemState.Idle));
    }

    [Fact]
    public void Alarm_id_uses_non_default_chamber_when_supplied()
    {
        Assert.Equal("alarm:LoadLock|pl|Processing|>|0.05",
            OverrideIds.Alarm("LoadLock", "pl", SystemState.Processing, 1, 0.05f));
    }

    [Fact]
    public void OverrideRegistry_reads_legacy_unprefixed_alarm_id()
    {
        var log = new FakeLogWriter();
        var reg = new OverrideRegistry(log, clock: () => new DateTime(2026, 5, 12, 12, 0, 0));

        // Seed with a legacy entry (no chamber segment).
        reg.SetAlarmOverride("alarm:pm|Idle|>|0.0001", enabled: true,
            persist: false, reason: "legacy", by: "test");

        // Runtime synthesises chamber-prefixed IDs. Reader must still match.
        Assert.True(reg.IsAlarmOverridden("alarm:Main|pm|Idle|>|0.0001"));
    }

    [Fact]
    public void OverrideRegistry_reads_legacy_unprefixed_devstatus_id()
    {
        var log = new FakeLogWriter();
        var reg = new OverrideRegistry(log, clock: () => new DateTime(2026, 5, 12, 12, 0, 0));

        reg.SetAlarmOverride("devstatus:TP|5|Idle", enabled: true,
            persist: false, reason: "legacy", by: "test");

        Assert.True(reg.IsAlarmOverridden("devstatus:Main|TP|5|Idle"));
    }

    [Fact]
    public void OverrideRegistry_does_not_match_legacy_form_for_non_main_chamber()
    {
        // Legacy entries are unprefixed — under back-compat we treat them as Main.
        // A query for a non-default chamber must NOT match a legacy entry.
        var log = new FakeLogWriter();
        var reg = new OverrideRegistry(log, clock: () => new DateTime(2026, 5, 12, 12, 0, 0));

        reg.SetAlarmOverride("alarm:pm|Idle|>|0.0001", enabled: true,
            persist: false, reason: "legacy", by: "test");

        Assert.False(reg.IsAlarmOverridden("alarm:LoadLock|pm|Idle|>|0.0001"));
    }
}
