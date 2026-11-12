using System.Runtime.InteropServices;

namespace APS.RecipeModel.Controls.ScriptEditor;

/// <summary>
/// WM_SETREDRAW pause/resume reduces flicker during bulk character-format updates.
/// </summary>
public class BufferedRichTextBox : RichTextBox
{
    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public void BeginUpdate() => SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

    public void EndUpdate()
    {
        SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
        Invalidate();
    }
}
