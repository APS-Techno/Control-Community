using APS.ProcessPanel.Contracts;
using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

public class BaseProcessPanelVisibilityTests
{
    private sealed class TwoPowerPanel : BaseProcessPanel
    {
        protected override IEnumerable<ReadingSpec> DefineReadings() =>
        [
            new("PSU-A", ReadingGroup.Power, "PSU-A", "F0", "W"),
            new("BIAS-1",  ReadingGroup.Power, "BIAS-1",  "F0", "W"),
        ];
    }

    private static bool RowVisible(Control panel, string id)
    {
        var hits = panel.Controls.Find(id, searchAllChildren: true);
        Assert.NotEmpty(hits);
        return hits[0].Visible;
    }

    [Fact]
    public void SetPowerSupplyVisibility_hides_only_listed_rows()
    {
        using var panel = new TwoPowerPanel();
        panel.SetPowerSupplyVisibility(new Dictionary<string, bool> { ["PSU-A"] = false });

        Assert.False(RowVisible(panel, "PSU-A"));
        Assert.True(RowVisible(panel, "BIAS-1"));    // not listed -> untouched (visible)
    }
}
