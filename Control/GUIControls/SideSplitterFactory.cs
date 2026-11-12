namespace APS.SystemControl.GUIControls;

/// <summary>
/// WinForms gotcha: <see cref="Splitter"/> obeys reverse z-order docking.
/// To get [LeftPanel | LeftSplitter | Fill | RightSplitter | RightPanel],
/// add the splitter to its parent's <see cref="Control.Controls"/>
/// collection BEFORE adding the side panel — see the <see cref="Install"/>
/// helper which encodes that order.
/// </summary>
internal static class SideSplitterFactory
{
    private static readonly Color ColorSplitterBg    = Color.FromArgb(28, 44, 66);
    private static readonly Color ColorSplitterGrip  = Color.FromArgb(120, 150, 180);
    private static readonly Color ColorSplitterHover = Color.FromArgb(50, 80, 120);

    private const int SplitterWidth = 4;
    private const int MinExtraDefault = 240;

    /// <param name="minPanelWidth">Minimum width of the adjacent docked panel.</param>
    /// <param name="minExtra">Minimum width of the remaining fill area.</param>
    public static Splitter Create(DockStyle dock, int minPanelWidth, int minExtra = MinExtraDefault)
    {
        if (dock != DockStyle.Left && dock != DockStyle.Right)
            throw new ArgumentException("SideSplitter only supports Dock.Left or Dock.Right", nameof(dock));

        var splitter = new Splitter
        {
            Dock = dock,
            Width = SplitterWidth,
            BackColor = ColorSplitterBg,
            MinExtra = minExtra,
            MinSize = minPanelWidth,
            Cursor = Cursors.VSplit,
        };

        splitter.MouseEnter += (_, _) => splitter.BackColor = ColorSplitterHover;
        splitter.MouseLeave += (_, _) => splitter.BackColor = ColorSplitterBg;

        splitter.Paint += (_, e) =>
        {
            var g = e.Graphics;
            var rc = splitter.ClientRectangle;
            int cx = rc.Width / 2;
            int cy = rc.Height / 2;
            using SolidBrush b = new(ColorSplitterGrip);
            g.FillRectangle(b, cx - 1, cy - 7, 2, 2);
            g.FillRectangle(b, cx - 1, cy - 1, 2, 2);
            g.FillRectangle(b, cx - 1, cy + 5, 2, 2);
        };

        return splitter;
    }

    /// <summary>
    /// The side panel must already be in <paramref name="parent"/>.Controls.
    /// </summary>
    public static Splitter Install(Control parent, Control sidePanel, int minPanelWidth, int minExtra = MinExtraDefault)
    {
        var splitter = Create(sidePanel.Dock, minPanelWidth, minExtra);
        parent.Controls.Add(splitter);

        // The splitter must be processed AFTER the side panel during dock
        // layout, which means it needs a LOWER z-index than the panel.
        // Move the splitter to one position BEFORE the panel in the
        // controls collection.
        int panelIndex = parent.Controls.GetChildIndex(sidePanel);
        parent.Controls.SetChildIndex(splitter, panelIndex);
        return splitter;
    }
}
