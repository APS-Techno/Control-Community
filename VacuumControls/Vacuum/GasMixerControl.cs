using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;

namespace APS.Controls.Vacuum;

/// <summary>
/// Ports are on the long sides of the tube (like pipe stubs along the barrel).
/// Horizontal: inputs on top, outputs on bottom, distributed along length.
/// Vertical: inputs on left, outputs on right, distributed along length.
/// </summary>
[ToolboxItem(false)]
public class GasMixerControl : ThemedVacuumControl
{
    private const int BaseWidth = 120;
    private const int BaseHeight = 50;
    private const int PortHeightIncrement = 12;
    private const int DefaultFontSize = 10;
    private const int FlangeThick = 3;
    private const int FlangeExtra = 2;
    private const int CylinderCornerRadius = 16;

    private int _inputs = 2;
    private int _outputs = 1;
    private bool _vert;
    private float _scale = 1.0f;
    private Font? _cachedFont;
    private int _cachedFontSize = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Inputs
    {
        get => _inputs;
        set
        {
            _inputs = Math.Clamp(value, 2, 4);
            CalculateSize();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Outputs
    {
        get => _outputs;
        set
        {
            _outputs = Math.Clamp(value, 1, 4);
            CalculateSize();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Direction
    {
        set
        {
            _vert = value == "vert";
            CalculateSize();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
            Invalidate();
        }
    }

    public GasMixerControl(string id)
    {
        ID = id;
        Name = $"MIXER_{id}";
        CalculateSize();
    }

    #region [Sizing]

    protected override void CalculateSize()
    {
        float sf = _scale * BaseScale;
        int maxPorts = Math.Max(_inputs, _outputs);
        int baseH = BaseHeight + (maxPorts - 2) * PortHeightIncrement;
        int w = (int)Math.Round(BaseWidth * sf);
        int h = (int)Math.Round(baseH * sf);

        EnsureFont();
        var textSize = MeasureIdText();

        if (_vert)
            Size = new Size(h, w + textSize.Height + (int)(5 * BaseScale));
        else
            Size = new Size(w, h);
    }

    private void EnsureFont()
    {
        // Integer point size (truncation, cap in points), rendered as a pixel-unit
        // font (pt × 4/3) to stay DPI-neutral.
        int pt = (int)Math.Round(DefaultFontSize * _scale * BaseScale);
        if (pt > 11) pt = 11;
        if (pt < 1) pt = 1;

        if (_cachedFont == null || _cachedFontSize != pt)
        {
            _cachedFont?.Dispose();
            _cachedFont = new Font("Arial", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }
    }

    private Size MeasureIdText()
    {
        EnsureFont();
        return TextRenderer.MeasureText(ID ?? "", _cachedFont!);
    }

    #endregion

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        var anchors = new List<AnchorDef>();
        float sf = _scale * BaseScale;
        int maxPorts = Math.Max(_inputs, _outputs);
        int baseH = BaseHeight + (maxPorts - 2) * PortHeightIncrement;
        int bodyW = (int)Math.Round(BaseWidth * sf);
        int bodyH = (int)Math.Round(baseH * sf);

        if (_vert)
        {
            // Offset by the label height above the tube.
            EnsureFont();
            var textSize = MeasureIdText();
            int textOff = textSize.Height + (int)(5 * BaseScale);
            for (int i = 1; i <= _inputs; i++)
            {
                float frac = i / (float)(_inputs + 1);
                anchors.Add(new AnchorDef($"in_{i}", new Point(0, textOff + (int)(bodyW * frac))));
            }
            for (int i = 1; i <= _outputs; i++)
            {
                float frac = i / (float)(_outputs + 1);
                anchors.Add(new AnchorDef($"out_{i}", new Point(bodyH, textOff + (int)(bodyW * frac))));
            }
        }
        else
        {
            for (int i = 1; i <= _inputs; i++)
            {
                float frac = i / (float)(_inputs + 1);
                anchors.Add(new AnchorDef($"in_{i}", new Point((int)(bodyW * frac), 0)));
            }
            for (int i = 1; i <= _outputs; i++)
            {
                float frac = i / (float)(_outputs + 1);
                anchors.Add(new AnchorDef($"out_{i}", new Point((int)(bodyW * frac), bodyH)));
            }
        }

        return anchors;
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        EnsureFont();

        float sf = _scale * BaseScale;
        int maxPorts = Math.Max(_inputs, _outputs);
        int baseH = BaseHeight + (maxPorts - 2) * PortHeightIncrement;
        int bodyW = (int)Math.Round(BaseWidth * sf);
        int bodyH = (int)Math.Round(baseH * sf);
        int flangeThick = Math.Max(3, (int)(FlangeThick * sf));
        int flangeExtra = Math.Max(2, (int)(FlangeExtra * sf));
        int cornerR = Math.Max(8, (int)(CylinderCornerRadius * sf));

        if (_vert)
            DrawVertical(g, bodyW, bodyH, flangeThick, flangeExtra, cornerR, sf);
        else
            DrawHorizontal(g, bodyW, bodyH, flangeThick, flangeExtra, cornerR, sf);
    }

    private void DrawHorizontal(Graphics g, int bodyW, int bodyH, int flangeThick, int flangeExtra, int cornerR, float sf)
    {
        for (int i = 1; i <= _inputs; i++)
        {
            float frac = i / (float)(_inputs + 1);
            int x = (int)(bodyW * frac);
            int pw = Math.Max(4, (int)(8 * sf));
            DrawFlange(g, x - pw / 2 - flangeExtra, 0, pw + flangeExtra * 2, vertical: false, thick: flangeThick);
        }

        for (int i = 1; i <= _outputs; i++)
        {
            float frac = i / (float)(_outputs + 1);
            int x = (int)(bodyW * frac);
            int pw = Math.Max(4, (int)(8 * sf));
            DrawFlange(g, x - pw / 2 - flangeExtra, bodyH - flangeThick, pw + flangeExtra * 2, vertical: false, thick: flangeThick);
        }

        int inset = flangeThick;
        var bodyRect = new Rectangle(0, inset, bodyW, bodyH - inset * 2);
        DrawMetalHousing(g, bodyRect, cornerR);

        if (bodyRect.Width > 30 && bodyRect.Height > 30)
            DrawScrews(g, bodyRect, 8); // 8 = master's Math.Min(10, Math.Max(6,(int)(4*SF))+2) at SF=1

        int cavInset = Math.Max(4, (int)(6 * sf));
        var cavityRect = new Rectangle(bodyRect.X + cavInset, bodyRect.Y + cavInset,
            bodyRect.Width - cavInset * 2, bodyRect.Height - cavInset * 2);
        DrawCavity(g, cavityRect, Math.Max(4, cornerR - 4));

        DrawBaffles(g, cavityRect, horizontal: true, sf);

        var textSize = MeasureIdText();
        int textX = bodyW / 2 - textSize.Width / 2;
        int textY = bodyH / 2 - textSize.Height / 2;
        DrawIdText(g, textX, textY);
    }

    private void DrawVertical(Graphics g, int bodyW, int bodyH, int flangeThick, int flangeExtra, int cornerR, float sf)
    {
        var textSize = MeasureIdText();
        int textOff = textSize.Height + (int)(5 * BaseScale);

        for (int i = 1; i <= _inputs; i++)
        {
            float frac = i / (float)(_inputs + 1);
            int y = textOff + (int)(bodyW * frac);
            int pw = Math.Max(4, (int)(8 * sf));
            DrawFlange(g, 0, y - pw / 2 - flangeExtra, pw + flangeExtra * 2, vertical: true, thick: flangeThick);
        }

        for (int i = 1; i <= _outputs; i++)
        {
            float frac = i / (float)(_outputs + 1);
            int y = textOff + (int)(bodyW * frac);
            int pw = Math.Max(4, (int)(8 * sf));
            DrawFlange(g, bodyH - flangeThick, y - pw / 2 - flangeExtra, pw + flangeExtra * 2, vertical: true, thick: flangeThick);
        }

        int inset = flangeThick;
        var bodyRect = new Rectangle(inset, textOff, bodyH - inset * 2, bodyW);
        DrawMetalHousing(g, bodyRect, cornerR);

        if (bodyRect.Width > 30 && bodyRect.Height > 30)
            DrawScrews(g, bodyRect, 8); // 8 = master's Math.Min(10, Math.Max(6,(int)(4*SF))+2) at SF=1

        int cavInset = Math.Max(4, (int)(6 * sf));
        var cavityRect = new Rectangle(bodyRect.X + cavInset, bodyRect.Y + cavInset,
            bodyRect.Width - cavInset * 2, bodyRect.Height - cavInset * 2);
        DrawCavity(g, cavityRect, Math.Max(4, cornerR - 4));

        DrawBaffles(g, cavityRect, horizontal: false, sf);

        int textX = bodyH / 2 - textSize.Width / 2;
        DrawIdText(g, textX, 0);
    }

    private static void DrawBaffles(Graphics g, Rectangle cavity, bool horizontal, float sf)
    {
        using var pen = new Pen(VacuumTheme.MetalMid, Math.Max(1f, 1.5f * sf));
        int count = 3;

        if (horizontal)
        {
            for (int i = 1; i <= count; i++)
            {
                float frac = i / (float)(count + 1);
                int x = cavity.X + (int)(cavity.Width * frac);
                int offset = (i % 2 == 0) ? cavity.Height / 4 : -cavity.Height / 4;
                g.DrawLine(pen,
                    x, cavity.Y + cavity.Height / 2 - cavity.Height / 3,
                    x + offset / 2, cavity.Y + cavity.Height / 2 + cavity.Height / 3);
            }
        }
        else
        {
            for (int i = 1; i <= count; i++)
            {
                float frac = i / (float)(count + 1);
                int y = cavity.Y + (int)(cavity.Height * frac);
                int offset = (i % 2 == 0) ? cavity.Width / 4 : -cavity.Width / 4;
                g.DrawLine(pen,
                    cavity.X + cavity.Width / 2 - cavity.Width / 3, y,
                    cavity.X + cavity.Width / 2 + cavity.Width / 3, y + offset / 2);
            }
        }
    }

    private static void DrawFlange(Graphics g, int x, int y, int size, bool vertical, int thick)
    {
        Rectangle rect = vertical
            ? new Rectangle(x, y, thick, size)
            : new Rectangle(x, y, size, thick);

        using var brush = new SolidBrush(VacuumTheme.MetalHighlight);
        g.FillRectangle(brush, rect);
        using var pen = new Pen(VacuumTheme.MetalEdge, 0.5f);
        g.DrawRectangle(pen, rect);
    }

    private void DrawIdText(Graphics g, int x, int y)
    {
        if (_cachedFont == null || string.IsNullOrEmpty(ID)) return;

        if (IsDark)
        {
            var textSize = TextRenderer.MeasureText(ID, _cachedFont);
            using var glowBrush = new SolidBrush(Color.FromArgb(20, VacuumTheme.GlowBlue));
            g.FillRectangle(glowBrush, x - 2, y - 1, textSize.Width + 4, textSize.Height + 2);
        }

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TextRenderer.DrawText(g, ID, _cachedFont, new Point(x, y), textColor);
    }

    #endregion

    #region [Dispose]

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cachedFont?.Dispose();
            _cachedFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
