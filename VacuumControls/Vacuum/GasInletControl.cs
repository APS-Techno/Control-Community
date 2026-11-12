using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

/// <summary>
/// Direction controls which way the nozzle/outlet points: down (default), up, left, right.
/// </summary>
[ToolboxItem(false)]
public class GasInletControl : ThemedVacuumControl
{
    private const int DefaultWidth = 40;
    private const int DefaultHeight = 60;

    private readonly string _gas;
    private string _direction = "down";
    private float _scale = 1.0f;
    private Font? _gasFont;
    private Font? _idFont;
    private float _cachedBaseScale = -1f;

    public string Gas => _gas;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Direction
    {
        set
        {
            _direction = value;
            CalculateSize();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    public GasInletControl(string id, string gas)
    {
        _gas = gas;
        ID = id;
        this.Name = GUIConst.GASINLET_PREFIX + id;
        CalculateSize();
    }

    #region [Anchors]

    /// <summary>
    /// Vertical extent reserved below the body in horizontal orientations
    /// for the ID label. Fonts may not be initialized when this is queried
    /// (constructor calls CalculateSize), so we use a scale-aware estimate
    /// instead of measuring the actual text.
    /// </summary>
    private int HorizontalIdLabelH() => Math.Max(14, (int)(14 * BaseScale));

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        int axisY = (Height - HorizontalIdLabelH()) / 2;
        return _direction switch
        {
            "up" => [new("outlet", new Point(Width / 2, 0))],
            "left" => [new("outlet", new Point(0, axisY))],
            "right" => [new("outlet", new Point(Width, axisY))],
            _ => [new("outlet", new Point(Width / 2, Height))],
        };
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        int w = (int)Math.Round(DefaultWidth * BaseScale * _scale);
        int h = (int)Math.Round(DefaultHeight * BaseScale * _scale);

        if (_direction is "left" or "right")
            Size = new Size(h, w + HorizontalIdLabelH());
        else
            Size = new Size(w, h);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 8 || h < 8) return;

        EnsureFonts();

        switch (_direction)
        {
            case "up": PaintVertical(g, w, h, domeAtBottom: true); break;
            case "left": PaintHorizontal(g, w, h, domeAtRight: true); break;
            case "right": PaintHorizontal(g, w, h, domeAtRight: false); break;
            default: PaintVertical(g, w, h, domeAtBottom: false); break;
        }
    }

    private void PaintVertical(Graphics g, int w, int h, bool domeAtBottom)
    {
        int idLabelH = _idFont != null ? TextRenderer.MeasureText(ID ?? "", _idFont).Height + 2 : 0;
        int nozzleH = Math.Max(4, (int)(6 * BaseScale));
        int cylinderH = h - idLabelH - nozzleH;
        if (cylinderH < 10) cylinderH = 10;

        int bodyInset = Math.Max(3, (int)(4 * BaseScale));
        int bodyX = bodyInset;
        int bodyW = w - bodyInset * 2;
        int domeH = Math.Max(6, bodyW / 3);
        if (bodyW < 4) return;

        if (!domeAtBottom)
        {
            // Dome at top, nozzle at bottom (default "down")
            int bodyY = domeH / 2;
            int bodyH = cylinderH - domeH / 2;
            if (bodyH < 4) return;

            var bodyRect = new Rectangle(bodyX, bodyY, bodyW, bodyH);
            DrawMetalHousing(g, bodyRect, 4);
            DrawDome(g, bodyX, 0, bodyW, domeH);

            int nozzleW = Math.Max(4, bodyW / 4);
            int nozzleX = bodyX + (bodyW - nozzleW) / 2;
            DrawNozzle(g, nozzleX, bodyY + bodyH, nozzleW, nozzleH);
            DrawGasLabel(g, bodyX, bodyY, bodyW, bodyH);
            DrawIdLabel(g, w, nozzleX + nozzleW / 2, bodyY + bodyH + nozzleH + 1, vertical: true);
        }
        else
        {
            // Dome at bottom, nozzle at top ("up")
            int nozzleY = 0;
            int bodyY = nozzleH;
            int bodyH = cylinderH - domeH / 2;
            if (bodyH < 4) return;

            int nozzleW = Math.Max(4, bodyW / 4);
            int nozzleX = bodyX + (bodyW - nozzleW) / 2;
            DrawNozzle(g, nozzleX, nozzleY, nozzleW, nozzleH);

            var bodyRect = new Rectangle(bodyX, bodyY, bodyW, bodyH);
            DrawMetalHousing(g, bodyRect, 4);
            DrawDome(g, bodyX, bodyY + bodyH - domeH / 2, bodyW, domeH);
            DrawGasLabel(g, bodyX, bodyY, bodyW, bodyH);
            DrawIdLabel(g, w, nozzleX + nozzleW / 2, bodyY + bodyH + domeH / 2 + 1, vertical: true);
        }
    }

    private void PaintHorizontal(Graphics g, int w, int h, bool domeAtRight)
    {
        int nozzleW = Math.Max(4, (int)(6 * BaseScale));
        int cylinderW = w - nozzleW;
        if (cylinderW < 10) cylinderW = 10;

        int idLabelH = HorizontalIdLabelH();
        int bodyInset = Math.Max(3, (int)(4 * BaseScale));
        int bodyY = bodyInset;
        int bodyH = h - bodyInset * 2 - idLabelH;
        int domeW = Math.Max(6, bodyH / 3);
        if (bodyH < 4) return;

        if (!domeAtRight)
        {
            // Dome at left, nozzle at right ("right")
            int bodyX = domeW / 2;
            int bodyW = cylinderW - domeW / 2;
            if (bodyW < 4) return;

            var bodyRect = new Rectangle(bodyX, bodyY, bodyW, bodyH);
            DrawMetalHousing(g, bodyRect, 4);
            DrawDomeH(g, 0, bodyY, domeW, bodyH);

            int nozzleH = Math.Max(4, bodyH / 4);
            int nozzleY = bodyY + (bodyH - nozzleH) / 2;
            DrawNozzle(g, bodyX + bodyW, nozzleY, nozzleW, nozzleH);
            DrawGasLabel(g, bodyX, bodyY, bodyW, bodyH);
            DrawIdLabel(g, h, bodyX + bodyW / 2, bodyY + bodyH + bodyInset + 1, vertical: false);
        }
        else
        {
            // Dome at right, nozzle at left ("left")
            int nozzleX = 0;
            int bodyX = nozzleW;
            int bodyW = cylinderW - domeW / 2;
            if (bodyW < 4) return;

            int nozzleH = Math.Max(4, bodyH / 4);
            int nozzleY = bodyY + (bodyH - nozzleH) / 2;
            DrawNozzle(g, nozzleX, nozzleY, nozzleW, nozzleH);

            var bodyRect = new Rectangle(bodyX, bodyY, bodyW, bodyH);
            DrawMetalHousing(g, bodyRect, 4);
            DrawDomeH(g, bodyX + bodyW - domeW / 2, bodyY, domeW, bodyH);
            DrawGasLabel(g, bodyX, bodyY, bodyW, bodyH);
            DrawIdLabel(g, h, bodyX + bodyW / 2, bodyY + bodyH + bodyInset + 1, vertical: false);
        }
    }

    #endregion

    #region [Drawing Helpers]

    private void DrawDome(Graphics g, int x, int y, int w, int domeH)
    {
        var domeRect = new Rectangle(x, y, w, domeH);
        using (var domeBrush = VacuumTheme.CreateMetalGradient(domeRect))
            g.FillEllipse(domeBrush, domeRect);
        using (var domePen = new Pen(VacuumTheme.MetalEdge, 1f))
            g.DrawEllipse(domePen, domeRect);

        int specW = w / 2, specH = domeH / 3;
        if (specW > 2 && specH > 2)
        {
            var specRect = new Rectangle(x + w / 4, y + 1, specW, specH);
            using var specBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
            g.FillEllipse(specBrush, specRect);
        }
    }

    private void DrawDomeH(Graphics g, int x, int y, int domeW, int h)
    {
        var domeRect = new Rectangle(x, y, domeW, h);
        using (var domeBrush = VacuumTheme.CreateMetalGradient(domeRect))
            g.FillEllipse(domeBrush, domeRect);
        using (var domePen = new Pen(VacuumTheme.MetalEdge, 1f))
            g.DrawEllipse(domePen, domeRect);

        int specW = domeW / 3, specH = h / 2;
        if (specW > 2 && specH > 2)
        {
            var specRect = new Rectangle(x + 1, y + h / 4, specW, specH);
            using var specBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
            g.FillEllipse(specBrush, specRect);
        }
    }

    private static void DrawNozzle(Graphics g, int x, int y, int w, int h)
    {
        var nozzleRect = new Rectangle(x, y, w, h);
        using (var nozzleBrush = VacuumTheme.CreateMetalGradient(nozzleRect))
            g.FillRectangle(nozzleBrush, nozzleRect);
        using (var nozzlePen = new Pen(VacuumTheme.MetalEdge, 0.5f))
            g.DrawRectangle(nozzlePen, nozzleRect);
    }

    private void DrawGasLabel(Graphics g, int bodyX, int bodyY, int bodyW, int bodyH)
    {
        if (string.IsNullOrEmpty(_gas) || _gasFont == null) return;

        Color gasColor = GetGasColor(_gas);
        var textSize = g.MeasureString(_gas, _gasFont);
        float textX = bodyX + (bodyW - textSize.Width) / 2f;
        float textY = bodyY + (bodyH - textSize.Height) / 2f;

        if (IsDark)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(40, gasColor));
            g.FillRectangle(glowBrush, textX - 2, textY - 1, textSize.Width + 4, textSize.Height + 2);
        }

        using var gasBrush = new SolidBrush(gasColor);
        g.DrawString(_gas, _gasFont, gasBrush, textX, textY);
    }

    private void DrawIdLabel(Graphics g, int span, int cx, int y, bool vertical)
    {
        if (string.IsNullOrEmpty(ID) || _idFont == null) return;

        var idSize = TextRenderer.MeasureText(ID, _idFont);
        int idX = vertical ? (span - idSize.Width) / 2 : cx - idSize.Width / 2;

        if (IsDark)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(30, VacuumTheme.GlowBlue));
            g.FillRectangle(glowBrush, idX - 2, y - 1, idSize.Width + 4, idSize.Height + 2);
        }

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TextRenderer.DrawText(g, ID, _idFont, new Point(idX, y), textColor);
    }

    private static Color GetGasColor(string gas) => gas switch
    {
        "Ar" => Color.FromArgb(0, 200, 255),
        "N2" => Color.FromArgb(0, 255, 100),
        "O2" => Color.FromArgb(255, 180, 60),
        _ => VacuumTheme.GlowBlue,
    };

    private void EnsureFonts()
    {
        // Keyed on BaseScale only. Pixel-unit fonts: pt × 4/3 so 96-DPI rendering is unchanged.
        if (_gasFont != null && Math.Abs(_cachedBaseScale - BaseScale) <= 0.001f) return;

        _gasFont?.Dispose();
        _idFont?.Dispose();

        _gasFont = new Font("Consolas", 10f * 4f / 3f * BaseScale, FontStyle.Bold,    GraphicsUnit.Pixel);
        _idFont  = new Font("Consolas", 7f  * 4f / 3f * BaseScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _cachedBaseScale = BaseScale;
    }

    #endregion

    #region [Events]

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    #endregion

    #region [Dispose]

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gasFont?.Dispose();
            _idFont?.Dispose();
            _gasFont = null;
            _idFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
