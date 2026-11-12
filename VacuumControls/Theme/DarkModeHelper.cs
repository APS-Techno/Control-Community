using System.Runtime.InteropServices;

namespace APS.Controls.Theme;

/// <summary>
/// Fixes standard WinForms controls that don't respect .NET 9 dark mode.
/// Call <see cref="FixControls"/> after InitializeComponent() or at end of constructor.
/// For tab controls, use <see cref="DarkTabControl"/> instead of standard TabControl.
/// </summary>
public static class DarkModeHelper
{
    private static readonly Color BgDark = Color.FromArgb(13, 27, 42);
    // BgPanel intentionally matches BgDark — the theme uses a single dark tone for
    // all chrome (menu, toolbar, status bar, tab pages, panels, propgrid frame).
    private static readonly Color BgPanel = Color.FromArgb(13, 27, 42);
    private static readonly Color BgHover = Color.FromArgb(30, 44, 66);
    private static readonly Color BgInput = Color.FromArgb(18, 30, 48);
    private static readonly Color Border = Color.FromArgb(40, 60, 90);
    private static readonly Color TextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color TextDim = Color.FromArgb(119, 136, 153);
    private static readonly Color Accent = Color.FromArgb(72, 169, 254);

#pragma warning disable WFO5001
    /// <summary>No-op when the app is not running in dark mode.</summary>
    public static void FixControls(Control root)
    {
        if (!VacuumTheme.IsDark)
            return;

        FixControlsRecursive(root);
    }
#pragma warning restore WFO5001

    private static void FixControlsRecursive(Control control)
    {
        switch (control)
        {
            case Button btn:
                if (btn.FlatStyle != FlatStyle.Flat)
                {
                    btn.UseVisualStyleBackColor = false;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = BgPanel;
                    btn.ForeColor = TextPrimary;
                    btn.FlatAppearance.BorderColor = Border;
                    btn.FlatAppearance.MouseOverBackColor = BgHover;
                    btn.FlatAppearance.BorderSize = 1;
                }
                break;

            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = BgPanel;
                page.ForeColor = TextPrimary;
                break;

            case DarkTabControl:
                // Self-themed for painting, but still recurse into tab page children
                break;

            case StatusStrip status:
                status.BackColor = BgDark;
                status.ForeColor = TextPrimary;
                status.Renderer = new DarkToolStripRenderer();
                foreach (ToolStripItem item in status.Items)
                    item.ForeColor = TextPrimary;
                break;

            case MenuStrip menu:
                menu.BackColor = BgPanel;
                menu.ForeColor = TextPrimary;
                menu.Renderer = new DarkToolStripRenderer();
                break;

            case ToolStrip strip:
                strip.BackColor = BgPanel;
                strip.ForeColor = TextPrimary;
                strip.Renderer = new DarkToolStripRenderer();
                FixToolStripItems(strip);
                break;

            case DataGridView dgv:
                dgv.BackgroundColor = BgDark;
                dgv.GridColor = Border;
                dgv.DefaultCellStyle.BackColor = BgPanel;
                dgv.DefaultCellStyle.ForeColor = TextPrimary;
                dgv.DefaultCellStyle.SelectionBackColor = Accent;
                dgv.DefaultCellStyle.SelectionForeColor = Color.White;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = BgDark;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
                dgv.RowHeadersDefaultCellStyle.BackColor = BgDark;
                dgv.RowHeadersDefaultCellStyle.ForeColor = TextPrimary;
                dgv.EnableHeadersVisualStyles = false;
                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    if (col is DataGridViewComboBoxColumn combo)
                    {
                        combo.FlatStyle = FlatStyle.Flat;
                        combo.DefaultCellStyle.BackColor = BgPanel;
                        combo.DefaultCellStyle.ForeColor = TextPrimary;
                    }
                }
                ApplyDarkScrollbars(dgv);
                break;

            case TreeView tree:
                tree.BackColor = BgDark;
                tree.ForeColor = TextPrimary;
                tree.LineColor = Border;
                ApplyDarkScrollbars(tree);
                break;

            case ListView list:
                list.BackColor = BgDark;
                list.ForeColor = TextPrimary;
                ApplyDarkListView(list);
                break;

            case ListBox lb:
                lb.BackColor = BgInput;
                lb.ForeColor = TextPrimary;
                ApplyDarkScrollbars(lb);
                break;

            case TextBox txt:
                txt.BackColor = BgInput;
                txt.ForeColor = TextPrimary;
                txt.BorderStyle = BorderStyle.FixedSingle;
                ApplyDarkScrollbars(txt);
                break;

            case RichTextBox rtb:
                rtb.BackColor = BgDark;
                rtb.ForeColor = TextPrimary;
                ApplyDarkScrollbars(rtb);
                break;

            case ComboBox combo:
                combo.BackColor = BgInput;
                combo.ForeColor = TextPrimary;
                combo.FlatStyle = FlatStyle.Flat;
                ApplyDarkCombo(combo);
                break;

            case NumericUpDown nud:
                nud.BackColor = BgInput;
                nud.ForeColor = TextPrimary;
                break;

            case ProgressBar pbar:
                ApplyDarkProgressBar(pbar);
                break;

            case GroupBox grp:
                grp.ForeColor = TextDim;
                break;

            case LinkLabel link:
                // Default LinkColor is Windows blue (~#0000FF) — unreadable on the
                // dark background. Use the theme accent for links, brighter on click.
                link.LinkColor = Accent;
                link.ActiveLinkColor = Color.White;
                link.VisitedLinkColor = Accent;
                link.DisabledLinkColor = TextDim;
                if (link.BackColor == SystemColors.Control || link.BackColor == Color.Transparent)
                    link.ForeColor = TextPrimary;
                break;

            case Label lbl:
                if (lbl.BackColor == SystemColors.Control || lbl.BackColor == Color.Transparent)
                    lbl.ForeColor = TextPrimary;
                break;

            case Panel panel:
                if (panel.BackColor == SystemColors.Control)
                    panel.BackColor = BgDark;
                if (panel.AutoScroll)
                    ApplyDarkScrollbars(panel);
                break;

            case SplitContainer split:
                split.BackColor = BgDark;
                break;

            case PropertyGrid pg:
                pg.BackColor = BgPanel;
                pg.ViewBackColor = BgDark;
                pg.ViewForeColor = TextPrimary;
                pg.LineColor = Border;
                pg.CategoryForeColor = Accent;
                pg.CategorySplitterColor = Border;
                pg.HelpBackColor = BgPanel;
                pg.HelpForeColor = TextDim;
                pg.CommandsBackColor = BgPanel;
                pg.CommandsForeColor = TextPrimary;
                break;

            case Form form:
                form.BackColor = BgDark;
                form.ForeColor = TextPrimary;
                SetTitleBarColor(form, BgDark);
                break;
        }

        foreach (Control child in control.Controls)
        {
            FixControlsRecursive(child);
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int LVM_GETHEADER = 0x1000 + 31;

    private static void ApplyDarkCombo(ComboBox combo)
    {
        void Apply() => SetWindowTheme(combo.Handle, "DarkMode_CFD", null);
        if (combo.IsHandleCreated) Apply();
        else combo.HandleCreated += (_, _) => Apply();
    }

    /// <summary>Apply the Win32 DarkMode_Explorer theme so scrollbars render dark.
    /// Works for controls that host native scrollbars (TreeView, RichTextBox,
    /// TextBox, ListBox, DataGridView, scrolling Panel).</summary>
    private static void ApplyDarkScrollbars(Control control)
    {
        void Apply() => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        if (control.IsHandleCreated) Apply();
        else control.HandleCreated += (_, _) => Apply();
    }

    private static void ApplyDarkListView(ListView list)
    {
        // Owner-draw the column header: the default header paints with the Windows
        // 'Explorer' theme (light grey) and ignores BackColor.
        void ApplyTheme()
        {
            SetWindowTheme(list.Handle, "DarkMode_Explorer", null);
            var hdr = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (hdr != IntPtr.Zero)
                SetWindowTheme(hdr, "DarkMode_ItemsView", null);
        }
        if (list.IsHandleCreated) ApplyTheme();
        else list.HandleCreated += (_, _) => ApplyTheme();

        if (list.View != View.Details) return;

        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) =>
        {
            using var bg = new SolidBrush(BgPanel);
            e.Graphics.FillRectangle(bg, e.Bounds);
            using var sep = new Pen(Border);
            e.Graphics.DrawLine(sep, e.Bounds.Right - 1, e.Bounds.Top + 2,
                                     e.Bounds.Right - 1, e.Bounds.Bottom - 2);
            e.Graphics.DrawLine(sep, e.Bounds.Left, e.Bounds.Bottom - 1,
                                     e.Bounds.Right, e.Bounds.Bottom - 1);
            var textRect = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, e.Font, textRect,
                TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem    += (_, e) => e.DrawDefault = true;
        list.DrawSubItem += (_, e) => e.DrawDefault = true;
    }

    private static void SetTitleBarColor(Form form, Color color)
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 — marks the window as dark-mode-aware
        // so DWM renders scrollbars/frame chrome in Windows 11's dark palette.
        int dark = 1;
        DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        // DWMWA_CAPTION_COLOR = 35 (Windows 11+)
        int colorRef = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(form.Handle, 35, ref colorRef, sizeof(int));
    }

    private static void FixToolStripItems(ToolStrip strip)
    {
        foreach (ToolStripItem item in strip.Items)
        {
            switch (item)
            {
                case ToolStripComboBox combo:
                    combo.BackColor = BgInput;
                    combo.ForeColor = TextPrimary;
                    combo.FlatStyle = FlatStyle.Flat;
                    break;
                case ToolStripTextBox txt:
                    txt.BackColor = BgInput;
                    txt.ForeColor = TextPrimary;
                    break;
                case ToolStripProgressBar pbar:
                    ApplyDarkProgressBar(pbar.ProgressBar);
                    break;
            }
        }
    }

    /// <summary>Disable Windows visual styles on a ProgressBar so it honors
    /// BackColor (track) and ForeColor (fill). Without this the bar paints
    /// in the OS light theme and ignores our palette.</summary>
    private static void ApplyDarkProgressBar(ProgressBar pbar)
    {
        pbar.BackColor = BgInput;
        pbar.ForeColor = Accent;
        void Apply() => SetWindowTheme(pbar.Handle, " ", " ");
        if (pbar.IsHandleCreated) Apply();
        else pbar.HandleCreated += (_, _) => Apply();
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using var brush = new SolidBrush(BgHover);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
            else
            {
                using var brush = new SolidBrush(BgPanel);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Respect the item's own ForeColor if it was explicitly set
            // (e.g. status-bar state indicator); otherwise use theme colors.
            if (e.Item.ForeColor != TextPrimary && e.Item.Owner is StatusStrip)
                e.TextColor = e.Item.ForeColor;
            else
                e.TextColor = e.Item.Selected ? Accent : TextPrimary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is ToolStripButton btn && (btn.Selected || btn.Checked))
            {
                using var brush = new SolidBrush(btn.Checked ? BgHover : BgHover);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using var brush = new SolidBrush(BgHover);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => BgPanel;
        public override Color MenuStripGradientEnd => BgPanel;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => BgHover;
        public override Color MenuItemSelectedGradientBegin => BgHover;
        public override Color MenuItemSelectedGradientEnd => BgHover;
        public override Color MenuItemPressedGradientBegin => BgDark;
        public override Color MenuItemPressedGradientEnd => BgDark;
        public override Color ToolStripDropDownBackground => BgPanel;
        public override Color ImageMarginGradientBegin => BgPanel;
        public override Color ImageMarginGradientMiddle => BgPanel;
        public override Color ImageMarginGradientEnd => BgPanel;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color StatusStripGradientBegin => BgDark;
        public override Color StatusStripGradientEnd => BgDark;
        public override Color ToolStripGradientBegin => BgPanel;
        public override Color ToolStripGradientEnd => BgPanel;
        public override Color ToolStripGradientMiddle => BgPanel;
        public override Color ToolStripBorder => Border;
        public override Color OverflowButtonGradientBegin => BgPanel;
        public override Color OverflowButtonGradientMiddle => BgPanel;
        public override Color OverflowButtonGradientEnd => BgPanel;
    }
}
