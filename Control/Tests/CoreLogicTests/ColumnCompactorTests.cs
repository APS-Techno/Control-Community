using APS.SystemControl.GUIControls.General;
using Xunit;

namespace CoreLogicTests;

public class ColumnCompactorTests
{
    private static ColumnCompactor.PanelBox B(int order, float left, float top, float h, bool vis = true)
        => new(order, left, top, h, vis);

    [Fact]
    public void Hidden_middle_panel_gap_is_closed()
    {
        var boxes = new[]
        {
            B(0, 82f, 0f,  10f),
            B(1, 82f, 12f, 10f, vis: false),
            B(2, 82f, 24f, 10f),
        };
        var tops = ColumnCompactor.Compute(boxes, gapPercent: 2f, columnTolerance: 5f);

        Assert.False(tops.ContainsKey(1));
        Assert.Equal(0f, tops[0]);            // anchor = column min top
        Assert.Equal(12f, tops[2]);           // 0 + 10 (height) + 2 (gap)
    }

    [Fact]
    public void Order_not_top_determines_stacking()
    {
        // Definition order 0,1 but listed with descending tops — must stack by Order.
        var boxes = new[] { B(1, 82f, 5f, 10f), B(0, 82f, 50f, 10f) };
        var tops = ColumnCompactor.Compute(boxes, gapPercent: 0f, columnTolerance: 5f);

        Assert.Equal(5f, tops[0]);   // Order 0 first, at anchor (min top = 5)
        Assert.Equal(15f, tops[1]);
    }

    [Fact]
    public void Columns_are_independent()
    {
        var boxes = new[] { B(0, 0.25f, 2f, 10f), B(1, 82f, 40f, 10f) };
        var tops = ColumnCompactor.Compute(boxes, gapPercent: 0f, columnTolerance: 5f);

        Assert.Equal(2f, tops[0]);    // left column keeps its own anchor
        Assert.Equal(40f, tops[1]);   // right column keeps its own anchor
    }
}
