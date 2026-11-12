using System.ComponentModel;
using APS.Controls.Theme;

namespace APS.Controls.General
{
    /// <summary>
    /// GDI TextRenderer/FontCache calls <c>Font.SizeInPoints</c>, which throws
    /// "Parameter is not valid" for an invalid pixel-unit font; unhandled, that is a
    /// white-cross placeholder and a crash. On failure the text is drawn once with a
    /// safe default font instead.
    /// </summary>
    [ToolboxItem(false)]
    public class SafeFontLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                base.OnPaint(e);
            }
            catch (ArgumentException)
            {
                // The assigned font is invalid (disposed / bad metrics). Render with a
                // known-good font so a schematic resize can't blank the display or kill
                // the process; do NOT mutate this.Font here (would recurse via layout).
                try
                {
                    TextRenderer.DrawText(e.Graphics, Text ?? string.Empty, SystemFonts.DefaultFont,
                        ClientRectangle, ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.WordEllipsis);
                }
                catch (ArgumentException) { /* last resort: skip this paint, never crash */ }
            }
        }
    }

    [ToolboxItem(false)]
    public class LEDDisplay : SafeFontLabel
    {
        internal const int DefaultFontSize = 11;

        public LEDDisplay()
        {
            bool isDark = VacuumTheme.IsDark;
            DoubleBuffered = true;
            BackColor = isDark ? VacuumTheme.LcdBackground : Color.DarkGray;
            TextAlign = ContentAlignment.MiddleCenter;
            ForeColor = isDark ? VacuumTheme.LcdForeground : Color.Green;
            Margin = new Padding(3, 5, 3, 5);

            // Plain point size: GDI+ already renders points × monitor DPI, so a DeviceDpi
            // factor would scale twice. LEDPanel overrides this with a pixel-unit font.
            Font = new Font("Digital-7 mono", DefaultFontSize);
        }
    }
}
