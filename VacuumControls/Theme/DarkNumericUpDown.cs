using System.Runtime.InteropServices;

namespace APS.Controls.Theme;

/// <summary>
/// Default WinForms NumericUpDown ignores BackColor on the spin buttons —
/// they render via Win32 themed parts that stay light. The internal
/// UpDownButtons control doesn't reliably raise managed Paint events, so we
/// subclass its HWND with a NativeWindow and OWNER-DRAW the buttons.
///
/// We deliberately do NOT let the default (light) paint run and then draw over
/// it: on a light-themed OS (where <c>Application.SetColorMode(Dark)</c> does
/// not take on native chrome) that "draw over" loses the race whenever the
/// window is repainted while inactive, leaving bright buttons. Instead we fully
/// handle WM_PAINT (own BeginPaint/EndPaint), swallow WM_ERASEBKGND, and mirror
/// the paint on WM_PRINTCLIENT — so there is no light paint to race, and the
/// result is deterministic regardless of OS theme or window activation.
/// </summary>
public sealed class DarkNumericUpDown : NumericUpDown
{
    private static readonly Color BgInput = Color.FromArgb(18, 30, 48);
    private static readonly Color Border = Color.FromArgb(40, 60, 90);
    private static readonly Color TextPrimary = Color.FromArgb(200, 214, 229);

#pragma warning disable IDE0052 // kept alive for the lifetime of the control
    private readonly ButtonsPainter? _painter;
#pragma warning restore IDE0052

    public DarkNumericUpDown()
    {
        // BorderStyle.None — we paint our own border in WndProc. FixedSingle draws
        // a system grey frame that ignores BackColor.
        BorderStyle = BorderStyle.None;
        BackColor = BgInput;
        ForeColor = TextPrimary;

        // Controls[0] is the UpDownButtons subcontrol; Controls[1] is the inner TextBox
        // (documented order in UpDownBase.Controls).
        if (Controls.Count > 0)
        {
            _painter = new ButtonsPainter(Controls[0]);
        }
    }

    private const int WM_PAINT_OUTER = 0x000F;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT_OUTER)
        {
            using var g = Graphics.FromHwnd(Handle);
            using var pen = new Pen(Border, 1);
            g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    private sealed class ButtonsPainter : NativeWindow
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PRINTCLIENT = 0x0318;

        private readonly Control _btns;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        public ButtonsPainter(Control btns)
        {
            _btns = btns;
            // Symmetric attach/detach so the painter survives handle recreation
            // (DPI changes, re-parenting) without a subscription gap.
            btns.HandleCreated += (_, _) => { if (Handle == IntPtr.Zero) AssignHandle(btns.Handle); };
            btns.HandleDestroyed += (_, _) => { if (Handle != IntPtr.Zero) ReleaseHandle(); };
            if (btns.IsHandleCreated) AssignHandle(btns.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_ERASEBKGND:
                    // Erase to our dark fill and mark handled, so no white flash
                    // ever slips through between erase and paint.
                    if (m.WParam != IntPtr.Zero)
                    {
                        using (var g = Graphics.FromHdc(m.WParam))
                        using (var bg = new SolidBrush(BgInput))
                            g.FillRectangle(bg, _btns.ClientRectangle);
                    }
                    m.Result = (IntPtr)1;
                    return;

                case WM_PAINT:
                {
                    // Full owner-draw: never invoke the default (light) button paint.
                    IntPtr hdc = BeginPaint(Handle, out var ps);
                    try
                    {
                        using var g = Graphics.FromHdc(hdc);
                        Paint(g, _btns.ClientRectangle);
                    }
                    finally
                    {
                        EndPaint(Handle, ref ps);
                    }
                    m.Result = IntPtr.Zero;
                    return;
                }

                case WM_PRINTCLIENT:
                    // Mirror onto the print/composite path (PrintWindow, a
                    // double-buffered parent) so those render dark too.
                    using (var g = Graphics.FromHdc(m.WParam))
                        Paint(g, _btns.ClientRectangle);
                    m.Result = (IntPtr)1;
                    return;
            }

            base.WndProc(ref m);
        }

        private static void Paint(Graphics g, Rectangle bounds)
        {
            using (var bg = new SolidBrush(BgInput))
                g.FillRectangle(bg, bounds);

            using (var pen = new Pen(Border))
            {
                g.DrawLine(pen, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom);
                int midY = bounds.Top + bounds.Height / 2;
                g.DrawLine(pen, bounds.Left, midY, bounds.Right, midY);
            }

            int cx = bounds.Left + bounds.Width / 2;
            int upMidY = bounds.Top + bounds.Height / 4;
            int dnMidY = bounds.Top + bounds.Height * 3 / 4;

            using var arrow = new SolidBrush(TextPrimary);
            g.FillPolygon(arrow, new[]
            {
                new Point(cx - 4, upMidY + 2),
                new Point(cx + 4, upMidY + 2),
                new Point(cx,     upMidY - 3),
            });
            g.FillPolygon(arrow, new[]
            {
                new Point(cx - 4, dnMidY - 2),
                new Point(cx + 4, dnMidY - 2),
                new Point(cx,     dnMidY + 3),
            });
        }
    }
}
