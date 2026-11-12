using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class MagnetronControl : ThemedVacuumControl
{
    private const int DefaultWidth = 70;
    private const int DefaultHeight = 50;
    private const int PortRadius = 8;

    private readonly string _gas;
    private string _direction = "down";
    private float _scale = 1.0f;
    private bool _hasGasInlet = true;

    private bool _energized;
    private Font? _labelFont;
    private Font? _idFont;
    private float _cachedBaseScale = -1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? TargetLabel { get; set; }

    public string Gas => _gas;

    /// <summary>Caption placement direction. Anchor is on the opposite side.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Direction
    {
        get => _direction;
        set { _direction = value; CalculateSize(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        get => _scale;
        set { _scale = value; CalculateSize(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasGasInlet
    {
        get => _hasGasInlet;
        set { _hasGasInlet = value; CalculateSize(); Invalidate(); }
    }

    /// <summary>Driven by live PSU power.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Energized
    {
        get => _energized;
        set
        {
            if (_energized == value) return;
            _energized = value;
            Invalidate();
        }
    }

    public MagnetronControl(string id, string gas)
    {
        _gas = gas;
        ID = id;
        TargetLabel = id;
        this.Name = GUIConst.ControlName(DeviceType.Power, id);
        CalculateSize();
    }

    #region [Layout]

    private struct MagLayout
    {
        public Rectangle Housing;
        public (int x, int y)? IdPos;
        // Half-circle port: center sits on the housing edge, bulges outward
        public int PortCX, PortCY, PortR;
    }

    private int ScaledPortRadius => _hasGasInlet ? Math.Max(5, (int)(PortRadius * _scale * BaseScale)) : 0;

    private MagLayout ComputeMagLayout()
    {
        EnsureFonts();
        int w = ClientSize.Width;
        int h = ClientSize.Height;

        int idLabelH = _idFont != null ? TextRenderer.MeasureText(ID ?? "", _idFont).Height + 2 : 0;
        int idLabelW = _idFont != null ? TextRenderer.MeasureText(ID ?? "", _idFont).Width : 0;
        int pr = ScaledPortRadius;

        var L = new MagLayout { PortR = pr };

        switch (_direction)
        {
            case "up":
                L.Housing = new Rectangle(0, idLabelH, w - 1, h - 1 - idLabelH - pr);
                L.PortCX = w / 2;
                L.PortCY = L.Housing.Bottom;
                L.IdPos = ((w - idLabelW) / 2, 0);
                break;

            case "left":
                L.Housing = new Rectangle(idLabelH, 0, w - 1 - idLabelH - pr, h - 1);
                L.PortCX = L.Housing.Right;
                L.PortCY = h / 2;
                L.IdPos = (1, (h - idLabelH) / 2);
                break;

            case "right":
                L.Housing = new Rectangle(pr, 0, w - 1 - idLabelH - pr, h - 1);
                L.PortCX = L.Housing.X;
                L.PortCY = h / 2;
                L.IdPos = (L.Housing.Right + 2, (h - idLabelH) / 2);
                break;

            default:
                L.Housing = new Rectangle(0, pr, w - 1, h - 1 - idLabelH - pr);
                L.PortCX = w / 2;
                L.PortCY = L.Housing.Y;
                L.IdPos = ((w - idLabelW) / 2, L.Housing.Bottom + 1);
                break;
        }

        return L;
    }

    #endregion

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        // Anchor at the tip of the nozzle (or housing edge if no gas inlet)
        return _direction switch
        {
            "up" => [new("bottom", new Point(Width / 2, Height))],
            "left" => [new("right", new Point(Width, Height / 2))],
            "right" => [new("left", new Point(0, Height / 2))],
            _ => [new("top", new Point(Width / 2, 0))],
        };
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        float sf = _scale * BaseScale;
        int pr = _hasGasInlet ? Math.Max(5, (int)(PortRadius * sf)) : 0;
        int bodyW = (int)Math.Round(DefaultWidth * sf);
        int bodyH = (int)Math.Round(DefaultHeight * sf);
        bool vert = _direction is "left" or "right";
        if (vert)
            Size = new Size(bodyH + pr, bodyW);
        else
            Size = new Size(bodyW, bodyH + pr);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 8 || h < 8) return;

        var L = ComputeMagLayout();

        if (_hasGasInlet && L.PortR > 0)
        {
            int r = L.PortR;
            int d = r * 2;
            float startAngle = _direction switch
            {
                "up" => 0,     // bulge down
                "left" => 270, // bulge right
                "right" => 90, // bulge left
                _ => 180,      // "down" → bulge up
            };

            using var portBrush = new SolidBrush(Color.FromArgb(45, 50, 60));
            using var portPen = new Pen(VacuumTheme.MetalEdge, 1.2f);
            g.FillPie(portBrush, L.PortCX - r, L.PortCY - r, d, d, startAngle, 180);
            g.DrawPie(portPen, L.PortCX - r, L.PortCY - r, d, d, startAngle, 180);

            bool portVert = _direction is "up" or "down";
            int dotOffX = portVert ? 0 : (_direction == "left" ? r / 2 : -r / 2);
            int dotOffY = portVert ? (_direction == "up" ? r / 2 : -r / 2) : 0;
            int dotR = Math.Max(2, r / 3);
            Color dotColor = GetGasColor(_gas);
            using var dotBrush = new SolidBrush(Color.FromArgb(180, dotColor));
            g.FillEllipse(dotBrush, L.PortCX + dotOffX - dotR, L.PortCY + dotOffY - dotR, dotR * 2, dotR * 2);
        }

        var housingRect = L.Housing;
        if (housingRect.Width < 4 || housingRect.Height < 4) return;

        // Plasma plume in front of the target face when energized — drawn BEFORE the
        // housing so the opaque box masks the inner half and the glow emanates OUTSIDE
        // the box on the face edge (like the plasma in front of a real magnetron).
        if (_energized)
            DrawPlasmaPlume(g, housingRect, _direction);

        DrawMetalHousing(g, housingRect, 5);

        if (housingRect.Width > 30 && housingRect.Height > 30)
            DrawScrews(g, housingRect, 7);

        int targetInset = Math.Max(6, (int)(8 * _scale * BaseScale));
        var targetRect = new Rectangle(
            housingRect.X + targetInset, housingRect.Y + targetInset,
            housingRect.Width - targetInset * 2,
            housingRect.Height - targetInset * 2);

        if (targetRect.Width > 4 && targetRect.Height > 4)
        {
            DrawCavity(g, targetRect, 3);

            var innerRect = new Rectangle(
                targetRect.X + 2, targetRect.Y + 2,
                targetRect.Width - 4, targetRect.Height - 4);
            if (innerRect.Width > 2 && innerRect.Height > 2)
            {
                using var targetBrush = new SolidBrush(Color.FromArgb(35, 35, 55));
                g.FillRectangle(targetBrush, innerRect);
                using var targetPen = new Pen(Color.FromArgb(55, 55, 75), 0.5f);
                g.DrawRectangle(targetPen, innerRect);
            }

            // White when energized for contrast against the plasma flood.
            string lbl = !string.IsNullOrEmpty(TargetLabel) ? TargetLabel! : (ID ?? "");
            if (!string.IsNullOrEmpty(lbl) && _labelFont != null)
            {
                Color labelColor = _energized ? Color.White : VacuumTheme.TextPrimary;
                var textSize = g.MeasureString(lbl, _labelFont);
                float textX = targetRect.X + (targetRect.Width - textSize.Width) / 2f;
                float textY = targetRect.Y + (targetRect.Height - textSize.Height) / 2f;

                using var labelBrush = new SolidBrush(labelColor);
                g.DrawString(lbl, _labelFont, labelBrush, textX, textY);
            }
        }

        if (PulseIntensity > 0.01f)
        {
            var glowRect = new Rectangle(housingRect.X - 4, housingRect.Y - 4,
                housingRect.Width + 8, housingRect.Height + 8);
            DrawGlow(g, glowRect, PulseColor, PulseIntensity);
        }
    }

    #endregion

    #region [Helpers]

    private static void DrawPlasmaPlume(Graphics g, Rectangle housing, string direction)
    {
        Rectangle rect;
        PointF center;
        switch (direction)
        {
            case "up":
            {
                int pw = (int)(housing.Width * 1.1f), ph = (int)(housing.Height * 0.72f);
                rect = new Rectangle(housing.X + housing.Width / 2 - pw / 2, housing.Top - ph * 3 / 4, pw, ph);
                center = new PointF(housing.X + housing.Width / 2f, housing.Top);
                break;
            }
            case "left":
            {
                int pw = (int)(housing.Width * 0.72f), ph = (int)(housing.Height * 1.1f);
                rect = new Rectangle(housing.Left - pw * 3 / 4, housing.Y + housing.Height / 2 - ph / 2, pw, ph);
                center = new PointF(housing.Left, housing.Y + housing.Height / 2f);
                break;
            }
            case "right":
            {
                int pw = (int)(housing.Width * 0.72f), ph = (int)(housing.Height * 1.1f);
                rect = new Rectangle(housing.Right - pw / 4, housing.Y + housing.Height / 2 - ph / 2, pw, ph);
                center = new PointF(housing.Right, housing.Y + housing.Height / 2f);
                break;
            }
            default: // "down" (and any non-standard value, e.g. "hor")
            {
                int pw = (int)(housing.Width * 1.1f), ph = (int)(housing.Height * 0.72f);
                rect = new Rectangle(housing.X + housing.Width / 2 - pw / 2, housing.Bottom - ph / 4, pw, ph);
                center = new PointF(housing.X + housing.Width / 2f, housing.Bottom);
                break;
            }
        }
        if (rect.Width <= 1 || rect.Height <= 1) return;

        using var path = new GraphicsPath();
        path.AddEllipse(rect);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = center,
            CenterColor = Color.FromArgb(200, VacuumTheme.GlowPlasma),
            SurroundColors = [Color.FromArgb(0, VacuumTheme.GlowPlasma)],
        };
        g.FillEllipse(brush, rect);
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
        // Keyed on BaseScale only; DPI does not affect widget-internal fonts.
        // Pixel-unit fonts: legacy pt × 4/3 so 96-DPI rendering is unchanged.
        if (_labelFont != null && Math.Abs(_cachedBaseScale - BaseScale) <= 0.001f) return;

        _labelFont?.Dispose();
        _idFont?.Dispose();

        _labelFont = new Font("Consolas", 8f  * 4f / 3f * BaseScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _idFont    = new Font("Consolas", 7f  * 4f / 3f * BaseScale, FontStyle.Regular, GraphicsUnit.Pixel);
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
            _labelFont?.Dispose();
            _idFont?.Dispose();
            _labelFont = null;
            _idFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
