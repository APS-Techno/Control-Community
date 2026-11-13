using APS.Controls.Generic;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.GUIControls.Process;
using Xunit;
using DriveInfo = APS.ProcessPanel.Contracts.DriveInfo;

namespace CoreLogicTests;

[Collection("ProcessPanelSpec")]
public class ChamberProcessPanelTests
{
    private static Control One(Control root, string name)
    {
        var hits = root.Controls.Find(name, true);
        Assert.Single(hits);
        return hits[0];
    }

    private static IEnumerable<ValveLedControl> Leds(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => c is ValveLedControl l ? [l] : Leds(c));

    private static ChamberProcessPanel Build(ProcessPanelSpec spec)
    {
        var saved = ProcessPanelSpec.Current;
        ProcessPanelSpec.Current = spec;
        try { return new ChamberProcessPanel(); }
        finally { ProcessPanelSpec.Current = saved; }
    }

    private static readonly ReadingSpec Pm = new("pm", ReadingGroup.Pressure, "pm", "E1", "Torr");

    private static ProcessPanelSpec Spec(
        ReadingSpec[]? readings = null,
        ValveGroupSpec[]? valves = null,
        DriveSpec[]? drives = null) =>
        new(readings ?? [], valves ?? [], drives ?? [], []);

    [Fact]
    public void Ctor_NoSpec_BuildsWithoutRows()
    {
        using var panel = Build(ProcessPanelSpec.Empty);
        Assert.Empty(panel.ReadingIds);
    }

    [Fact]
    public void Ctor_WithSpec_CreatesSeparateRowForEverySpecReading()
    {
        var ids = new[] { "PSU-A_V", "PSU-A_I", "GAUGE_A#1_P" };
        using var panel = Build(Spec(
        [
            new("PSU-A_V", ReadingGroup.Power, "PSU-A V", "F0", "V", Device: "PSU-A"),
            new("PSU-A_I", ReadingGroup.Power, "PSU-A I", "F0", "mA", Device: "PSU-A"),
            new("GAUGE_A#1_P", ReadingGroup.Process, "Pressure", "F2", "mTorr"),
        ]));

        Assert.Equal(ids.Order(), panel.ReadingIds.Order());
        foreach (var id in ids)
            Assert.Null(Assert.IsType<ReadingRowControl>(One(panel, id)).Spec.Secondary);
    }

    [Fact]
    public void Drive_pills_overlay_the_schematic_not_the_side_columns()
    {
        using var panel = Build(Spec(drives: [new("Holder", "Holder"), new("Shutter", "Shutter")]));
        var overlay = One(panel, ChamberProcessPanel.DrivesOverlayName);

        Assert.Contains(overlay.Parent!.Controls.Cast<Control>(), c => c is ChamberSchematicControl);
        Assert.Equal(0, overlay.Parent.Controls.GetChildIndex(overlay));
        Assert.NotEmpty(overlay.Controls.Find("HolderPill", true));
        Assert.NotEmpty(overlay.Controls.Find("ShutterPill", true));
    }

    [Fact]
    public void UpdateDrives_HomedDriveAtFlange_ShowsFlangeOnItsPill()
    {
        using var panel = Build(Spec(drives: [new("Holder", "Holder")]));
        panel.UpdateDrives([new DriveInfo("Holder", 10f, false, true, 2)]);
        Assert.Equal("FL 2", One(panel, "HolderPill").Text);
    }

    [Fact]
    public void UpdateDrives_DriveNotInSpec_DoesNotThrow()
    {
        using var panel = Build(Spec(drives: [new("Holder", "Holder")]));
        panel.UpdateDrives([new DriveInfo("Shutter", 0f, false, true, 1)]);
    }

    [Fact]
    public void UpdateValves_SpecValve_LightsItsLed()
    {
        using var panel = Build(Spec(readings: [Pm], valves:
        [
            new("Gas inlets", [new("V1", "N2 inlet"), new("V2", "Ar inlet")]),
        ]));
        panel.UpdateValves([new ValveInfo("V2", true)]);

        var leds = Leds(panel).ToDictionary(l => l.ValveId);
        Assert.True(leds["V2"].IsOpen);
        Assert.False(leds["V1"].IsOpen);
    }

    [Fact]
    public void UpdateValves_IdNotInSpec_DoesNotThrow()
    {
        using var panel = Build(Spec(readings: [Pm], valves: [new("G", [new("V1", "N2 inlet")])]));
        panel.UpdateValves([new ValveInfo("ZZ", true)]);
        Assert.False(Leds(panel).Single().IsOpen);
    }
}

[Collection("ProcessPanelSpec")]
public class AdsProcessPanelProfileTests
{
    [Fact]
    public void ProcessPanelXml_LoadsCleanAndReferencesDevicesDeclaredInHwConf()
    {
        var spec = ProcessPanelSpec.Load(APS.SystemControl.Tests.Shared.ConfigsDir.Path("ADS/ProcessPanel.xml"));
        Assert.Empty(spec.Errors);
        Assert.NotEmpty(spec.Readings);

        var hw = System.Xml.Linq.XDocument.Load(APS.SystemControl.Tests.Shared.ConfigsDir.Path("ADS/HWConf.xml"));
        var declared = hw.Descendants().Where(e => !e.HasElements).Select(e => e.Value.Trim())
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(spec.ValveGroups.SelectMany(g => g.Valves), v => Assert.Contains(v.Id, declared));
        Assert.All(spec.Readings.Select(r => r.Device).OfType<string>(), d => Assert.Contains(d, declared));
        Assert.All(spec.Drives, d => Assert.Contains(d.Id, declared));
    }
}
