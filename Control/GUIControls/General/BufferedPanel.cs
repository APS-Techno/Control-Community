using System.ComponentModel;

namespace APS.SystemControl.Controls.General;

/// <summary>
/// Paint-heavy schematic panels should use this to eliminate tearing.
/// </summary>
[ToolboxItem(false)]
public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
