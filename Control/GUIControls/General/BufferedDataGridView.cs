using System.ComponentModel;

namespace APS.SystemControl.Controls.General;

/// <summary>
/// Without buffering, every scroll step and selection change repaints the grid
/// un-buffered — visibly janky on weaker GPUs and when the UI thread is busy
/// servicing live hardware updates behind a modal dialog.
/// </summary>
[ToolboxItem(false)]
public class BufferedDataGridView : DataGridView
{
    public BufferedDataGridView()
    {
        DoubleBuffered = true;
    }
}
