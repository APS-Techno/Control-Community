using APS.ProcessPanel.Contracts;
using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Three-column process panel: Process-group readings build a third stack
/// inside the centre host when the subclass opts in with CenterPanelWidth;
/// a spec may carry a second trace on the same chart (DC voltage + current),
/// and PSU visibility matches rows by their hardware device, not their id.
/// </summary>
public class BaseProcessPanelLayoutTests
{
    private sealed class ThreeColumnPanel : BaseProcessPanel
    {
        protected override int CenterPanelWidth => 200;
        public Panel LeftColumn => LeftPanel;
        public Panel RightColumn => RightPanel;
        public Control CenterHost => SchematicPanel.Parent!;

        protected override IEnumerable<ReadingSpec> DefineReadings() =>
        [
            new("pm", ReadingGroup.Pressure, "pm", "E1", "Torr"),
            new("PSU-A_V", ReadingGroup.Power, "PSU-A", "F0", "V",
                Device: "PSU-A", Secondary: new ReadingTrace("PSU-A_I", "F0", "mA")),
            new("RF301_W", ReadingGroup.Power, "RF301", "F0", "W", Device: "RF301"),
            new("GAUGE_A#1_P",   ReadingGroup.Process, "Pressure", "F2", "mTorr", Section: "Throttle (GAUGE_A)"),
            new("GAUGE_A#1_Pos", ReadingGroup.Process, "Position", "F1", "%",     Section: "Throttle (GAUGE_A)"),
            new("BIAS-1_V1",  ReadingGroup.Process, "Ch1", "F2", "V", Section: "Bias (BIAS-1)",
                Secondary: new ReadingTrace("BIAS-1_I1", "F3", "A")),
        ];
    }

    private sealed class NoCenterPanel : BaseProcessPanel
    {
        public Panel LeftColumn => LeftPanel;
        protected override IEnumerable<ReadingSpec> DefineReadings() =>
        [
            new("pm", ReadingGroup.Pressure, "pm", "E1", "Torr"),
            new("GAUGE_A#1_P", ReadingGroup.Process, "Pressure", "F2", "mTorr", Section: "Throttle (GAUGE_A)"),
        ];
    }

    private static Control Row(Control panel, string id)
    {
        var hits = panel.Controls.Find(id, searchAllChildren: true);
        Assert.Single(hits);
        return hits[0];
    }

    [Fact]
    public void Process_rows_live_in_the_centre_host_not_in_a_side_panel()
    {
        using var panel = new ThreeColumnPanel();

        var pos = Row(panel, "GAUGE_A#1_Pos");
        Assert.True(IsInside(pos, panel.CenterHost), "process row should be inside the centre host");
        Assert.False(IsInside(pos, panel.LeftColumn));
        Assert.False(IsInside(pos, panel.RightColumn));

        var pm = Row(panel, "pm");
        Assert.True(IsInside(pm, panel.LeftColumn));
    }

    [Fact]
    public void Section_labels_are_emitted_once_per_run_of_specs()
    {
        using var panel = new ThreeColumnPanel();
        var texts = Walk(panel).OfType<Label>().Select(l => l.Text).ToList();

        Assert.Equal(1, texts.Count(t => t == "Throttle (GAUGE_A)"));
        Assert.Equal(1, texts.Count(t => t == "Bias (BIAS-1)"));
    }

    [Fact]
    public void Without_a_centre_panel_process_rows_fall_back_to_the_left_column()
    {
        using var panel = new NoCenterPanel();
        Assert.True(IsInside(Row(panel, "GAUGE_A#1_P"), panel.LeftColumn));
    }

    [Fact]
    public void Secondary_reading_is_routed_to_the_primary_row()
    {
        using var panel = new ThreeColumnPanel();
        panel.UpdateReadings(
        [
            new ProcessReading("PSU-A_V", 412, ReadingState.Ok),
            new ProcessReading("PSU-A_I", 730, ReadingState.Ok),
        ]);

        var row = (ReadingRowControl)Row(panel, "PSU-A_V");
        Assert.Equal(412, row.PrimaryValue);
        Assert.Equal(730, row.SecondaryValue);
        Assert.Empty(panel.Controls.Find("PSU-A_I", true));   // no separate row for the second trace
    }

    [Fact]
    public void ReadingIds_include_secondary_trace_ids()
    {
        using var panel = new ThreeColumnPanel();
        Assert.Contains("PSU-A_V", panel.ReadingIds);
        Assert.Contains("PSU-A_I", panel.ReadingIds);
        Assert.Contains("BIAS-1_I1", panel.ReadingIds);
        Assert.Contains("GAUGE_A#1_Pos", panel.ReadingIds);
    }

    [Fact]
    public void PowerSupplyVisibility_matches_rows_by_device()
    {
        using var panel = new ThreeColumnPanel();
        panel.SetPowerSupplyVisibility(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["psu-a"] = false,
            ["RF301"] = true,
        });

        Assert.False(Row(panel, "PSU-A_V").Visible);
        Assert.True(Row(panel, "RF301_W").Visible);
        Assert.True(Row(panel, "BIAS-1_V1").Visible);   // not a magnetron PSU row → untouched
    }

    private static bool IsInside(Control c, Control ancestor)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    private static IEnumerable<Control> Walk(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in Walk(c)) yield return d;
        }
    }
}
