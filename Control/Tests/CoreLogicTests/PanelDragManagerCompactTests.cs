using APS.SystemControl.XML.GUI;
using APS.SystemControl.GUIControls.General;
using Xunit;

namespace CoreLogicTests;

public class PanelDragManagerCompactTests
{
    private static (Control parent, PanelDragManager mgr, XML_Panel[] xml) Build()
    {
        // A borderless Panel reports ClientSize == Size without a handle, so no
        // window needs to be shown for the percent math.
        var parent = new Panel { Size = new Size(1000, 1000) };

        var xml = new[]
        {
            new XML_Panel { ID = "A", Left = 82f, Top = 0f  },
            new XML_Panel { ID = "B", Left = 82f, Top = 30f },
            new XML_Panel { ID = "C", Left = 82f, Top = 60f },
        };
        var panels = new XML_Panels { Panels = new List<XML_Panel>(xml) };
        var mgr = new PanelDragManager(parent, panels, Path.Combine(Path.GetTempPath(),
            "aps_dragtest_" + Guid.NewGuid().ToString("N") + ".xml"));

        for (int i = 0; i < xml.Length; i++)
        {
            var c = new Panel { Height = 100, Width = 200 };
            parent.Controls.Add(c);
            mgr.Register(c, xml[i]);
        }
        return (parent, mgr, xml);
    }

    [Fact]
    public void CompactColumns_closes_gap_from_hidden_panel()
    {
        var (parent, mgr, xml) = Build();
        using (parent)
        {
            parent.Controls[1].Visible = false;

            mgr.CompactColumns(persist: false);

            // Parent is 1000px tall: 100px panel = 10%. A stays at anchor 0,
            // C moves directly under A (10% + a small gap), B untouched.
            Assert.Equal(0f, xml[0].Top, precision: 3);
            Assert.Equal(30f, xml[1].Top, precision: 3);
            Assert.True(xml[2].Top < 30f, $"C should compact up, was {xml[2].Top}");
            Assert.True(xml[2].Top >= 10f, $"C should clear A's height, was {xml[2].Top}");
        }
    }

    [Fact]
    public void RepositionAllPanels_keeps_right_column_panel_inside_parent()
    {
        var (parent, mgr, xml) = Build();
        using (parent)
        {
            var panel = parent.Controls[0];

            mgr.RepositionAllPanels();
            Assert.Equal(800, panel.Left);
            Assert.Equal(82f, xml[0].Left, precision: 3);

            parent.Width = 2000;
            mgr.RepositionAllPanels();
            Assert.Equal(1640, panel.Left);
        }
    }

    /// <summary>
    /// After <see cref="PanelDragManager.Dispose"/> the parent's Resize event
    /// must not fire the old handler even when the managed child controls are
    /// already disposed.
    /// </summary>
    [Fact]
    public void Dispose_detaches_resize_handler_no_throw_after_panels_disposed()
    {
        var (parent, mgr, _) = Build();

        // Dispose all managed child panels first (simulates ClearDynamicPanels
        // disposing them before _panelDragManager?.Dispose() is called).
        foreach (Control child in parent.Controls)
            child.Dispose();

        mgr.Dispose();

        // Raising Resize on the parent must not throw, even though the managed
        // panels are disposed (the handler should no longer be subscribed).
        var ex = Record.Exception(() =>
            parent.GetType()
                  .GetMethod("OnResize",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                  .Invoke(parent, [EventArgs.Empty]));

        Assert.Null(ex);
        parent.Dispose();
    }
}
