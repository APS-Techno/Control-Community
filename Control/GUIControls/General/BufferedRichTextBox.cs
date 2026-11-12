using System.Windows.Forms;

namespace APS.SystemControl;

public class BufferedRichTextBox : RichTextBox
{
    public BufferedRichTextBox()
    {
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        this.UpdateStyles();
    }
}
