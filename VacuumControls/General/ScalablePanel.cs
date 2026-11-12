using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace APS.Controls.General
{
    public class ScalablePanel : Panel
    {
        /// <summary>96-DPI base. Layout/paint code must use <see cref="HeaderHeightPx"/>.</summary>
        public const int HeaderHeight = 24;

        /// <summary>Monitor DPI factor for instrument-panel pixel math. Panel
        /// geometry (sizes, paddings, paint offsets) multiplies by this; FONTS
        /// stay plain point sizes — GDI+ renders points × DPI on its own.</summary>
        public float Dpi => DeviceDpi / 96f;

        public int HeaderHeightPx => (int)(HeaderHeight * Dpi);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Caption { get; set; } = "";

        private Color? _headerColor;
        private Color? _borderColor;

        /// <summary>When null the header derives from BackColor (darkened to 80%).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? HeaderColor
        {
            get => _headerColor;
            set { _headerColor = value; ApplyHeaderColor(); Invalidate(); }
        }

        /// <summary>Optional panel-border color override. When null the panel
        /// uses <see cref="APS.Controls.Theme.VacuumTheme.PanelBorder"/>.
        /// Only painted by the base when <see cref="DrawStandardBorder"/> is true.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        private Color EffectiveHeaderColor =>
            _headerColor ?? DarkenColor(BackColor, 0.80f);

        /// <summary>Off by default so schematic/no-border panels are unaffected; the
        /// stock instrument panels override to true.</summary>
        protected virtual bool DrawStandardBorder => false;

        private readonly FlowLayoutPanel _headerOverlay;

        protected Panel HeaderOverlay => _headerOverlay;

        public ScalablePanel()
        {
            if (APS.Controls.Theme.VacuumTheme.IsDark)
                BackColor = Color.FromArgb(13, 27, 42);

            _headerOverlay = new FlowLayoutPanel
            {
                Dock = DockStyle.None,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Height = (int)((HeaderHeight - 2) * (DeviceDpi / 96f)),
                Width = 0,
                BackColor = EffectiveHeaderColor,
                AutoSize = true,
                Padding = new Padding(2, 0, 4, 0),
                Visible = false,
            };
            PositionHeaderOverlay();
            Controls.Add(_headerOverlay);
            _headerOverlay.ControlAdded += (_, _) => _headerOverlay.Visible = true;
            Resize += (_, _) => PositionHeaderOverlay();
            _headerOverlay.SizeChanged += (_, _) => PositionHeaderOverlay();
        }

        // Inset 3/2 px (96-DPI base) from right/top so the overlay's darker
        // background doesn't paint over the inner panel-border pixels.
        private void PositionHeaderOverlay() =>
            _headerOverlay.Location = new Point(
                Width - _headerOverlay.Width - (int)(3 * Dpi), (int)(2 * Dpi));

        protected void ScaleControls()
        {
            float scaleFactor = DeviceDpi / 96f;
            ScaleControl(this, scaleFactor);
        }

        // The overlay's BackColor is set once in the ctor; without this, a later
        // BackColor change (e.g. a config-driven per-panel tint) leaves the overlay
        // and its ambient-colored LED children as a mismatched rectangle.
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            ApplyHeaderColor();
        }

        private void ApplyHeaderColor()
        {
            if (_headerOverlay != null)
                _headerOverlay.BackColor = EffectiveHeaderColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;

            if (!string.IsNullOrEmpty(Caption))
            {
                // Inset so the strip doesn't paint over the 2-px panel border.
                int inset = (int)(2 * Dpi);
                var headerRect = new Rectangle(inset, inset, Width - 2 * inset, HeaderHeightPx - 1);
                using var headerBrush = new SolidBrush(EffectiveHeaderColor);
                g.FillRectangle(headerBrush, headerRect);

                // Point-sized font DPI-scales itself; centering uses the measured
                // height so it stays centered in the scaled strip.
                using var font = new Font("Segoe UI", 9, FontStyle.Bold);
                var textColor = Brightness(headerBrush.Color) < 128 ? Color.White : Color.Black;
                using var textBrush = new SolidBrush(textColor);
                g.DrawString(Caption, font, textBrush, 6 * Dpi,
                    (HeaderHeightPx - (float)Math.Ceiling(font.GetHeight(g))) / 2f + 1);
            }

            if (DrawStandardBorder)
            {
                using var pen = new Pen(_borderColor ?? APS.Controls.Theme.VacuumTheme.PanelBorder, 2);
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private static Color DarkenColor(Color color, float factor)
        {
            return Color.FromArgb(color.A,
                (int)(color.R * factor),
                (int)(color.G * factor),
                (int)(color.B * factor));
        }

        private static int Brightness(Color c) =>
            (c.R * 299 + c.G * 587 + c.B * 114) / 1000;

        private void ScaleControl(Control control, float scaleFactor)
        {
            // _headerOverlay self-scales (ctor Height × Dpi, PositionHeaderOverlay
            // location) — scaling it here too would dpi²-inflate it past the header
            // strip. Its CHILDREN (PSU status LEDs) still need the one-time scale.
            if (control != _headerOverlay)
            {
                control.Location = new Point((int)(control.Location.X * scaleFactor), (int)(control.Location.Y * scaleFactor));
                control.Size = new Size((int)(control.Size.Width * scaleFactor), (int)(control.Size.Height * scaleFactor));
            }

            foreach (Control childControl in control.Controls)
            {
                ScaleControl(childControl, scaleFactor);
            }
        }
    }
}
