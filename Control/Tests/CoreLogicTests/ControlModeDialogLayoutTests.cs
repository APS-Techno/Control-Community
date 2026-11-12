using APS.SystemControl.Core;
using APS.SystemControl.Forms;

namespace CoreLogicTests;

/// <summary>
/// The dialog fits its height to the content in OnLoad. Whatever the DPI, every
/// control must lie fully inside the client area and the note must sit above the
/// button strip.
/// </summary>
public class ControlModeDialogLayoutTests
{
    private static bool CanCreateHandles()
    {
        try { using var c = new Control(); c.CreateControl(); return true; }
        catch { return false; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_control_fits_inside_the_client_area_after_load(bool canSwitch)
    {
        if (!CanCreateHandles()) return;   // headless session: nothing to measure

        using var dlg = new frmControlMode(new ControlModeService(), canSwitch, "test");
        dlg.StartPosition = FormStartPosition.Manual;
        dlg.Show();                        // creates the handle and runs OnLoad (the fit)
        try
        {
            var table = dlg.Controls.OfType<TableLayoutPanel>().Single();
            var buttons = dlg.Controls.OfType<FlowLayoutPanel>().Single();
            var note = table.Controls.OfType<Label>().Last();

            Assert.True(note.Bottom <= table.Bottom, $"note bottom {note.Bottom} > table bottom {table.Bottom}");
            Assert.True(table.Bottom < buttons.Top, $"table bottom {table.Bottom} overlaps buttons top {buttons.Top}");

            Assert.True(buttons.Bottom <= dlg.ClientSize.Height,
                $"buttons bottom {buttons.Bottom} > client height {dlg.ClientSize.Height}");
            foreach (var b in buttons.Controls.OfType<Button>())
            {
                Assert.True(b.Height >= b.Font.Height + 6, $"button '{b.Text}' height {b.Height} cannot hold its font ({b.Font.Height})");
                Assert.True(buttons.Top + b.Bottom <= dlg.ClientSize.Height, $"button '{b.Text}' is clipped");
            }

            // Descriptions wrap inside the client width rather than being clipped on the right.
            foreach (var l in table.Controls.OfType<Label>())
                Assert.True(l.Right <= dlg.ClientSize.Width - dlg.Padding.Right, $"label clipped on the right: {l.Text[..20]}");
        }
        finally { dlg.Close(); }
    }
}
