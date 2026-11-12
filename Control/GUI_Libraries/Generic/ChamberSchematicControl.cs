using System.Drawing.Drawing2D;
using System.Drawing.Text;
using APS.SystemControl.Controls.Theme;

namespace APS.Controls.Generic;

public class ChamberSchematicControl : Control
{
    public const int FlangeCount = 6;

    private const float StepTolerance = 2100f;
    private const float RatioChamber = 0.96f;
    private const float RatioShutter = 0.91f;
    private const float RatioOrbit = 0.635f;
    private const float RatioFlange = 0.234f;
    private const float RatioHole = 0.254f;

    private static readonly float[] FlangeStepPositions = [0, 4024, 8048, 12072, 16096, 20120];

    // Motor steps between adjacent flanges.
    private const float FlangeStepSpan = 4024f;

    // Degrees, CCW from right.
    private static readonly float[] FlangeAngles = [0f, 60f, 120f, 180f, 240f, 300f];

    private static readonly Color ColorBackground = Color.FromArgb(13, 27, 42);
    private static readonly Color ColorTextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color ColorTextDim = Color.FromArgb(119, 136, 153);
    private static readonly Color ColorBorder = Color.FromArgb(40, 60, 90);

    private float _holderPosition;
    private float _shutterPosition;
    private bool _holderMoving;
    private bool _shutterMoving;
    private bool _holderHomed;
    private bool _shutterHomed;

    private float _holderDisplayPosition;
    private bool _holderDisplayInit;
    private float _shutterDisplayPosition;
    private bool _shutterDisplayInit;

    private readonly string[] _materials = new string[FlangeCount];
    private readonly double[] _depositionSeconds = new double[FlangeCount];
    private readonly bool[] _depositionTimeSet = new bool[FlangeCount];
    private readonly string[] _powerLabels = new string[FlangeCount];

    private readonly RectangleF[] _flangeRects = new RectangleF[FlangeCount];

    private float _chamberR, _shutterR, _orbitR, _flangeR, _holeR;

    private readonly System.Windows.Forms.Timer _renderTimer;

    private bool _showDepositionTimes;

    /// <summary>
    /// Seconds for the holder icon to sweep between two adjacent flanges (4024
    /// steps); the sweep is linear. Set per system in Panels.xml to match the real
    /// drive's traverse speed. Clamped to a 0.05 s floor.
    /// </summary>
    public float HolderFlangeSeconds { get; set; } = 0.5f;

    /// <summary>Seconds for the shutter icon to sweep between two adjacent
    /// flanges. See <see cref="HolderFlangeSeconds"/>.</summary>
    public float ShutterFlangeSeconds { get; set; } = 0.5f;

    public bool ShowDepositionTimes
    {
        get => _showDepositionTimes;
        set
        {
            if (_showDepositionTimes == value) return;
            _showDepositionTimes = value;
            Invalidate();
        }
    }

    /// <summary>Argument is the 1-based flange number.</summary>
    public event EventHandler<int>? FlangeDoubleClicked;

    public ChamberSchematicControl()
    {
        DoubleBuffered = true;
        BackColor = ColorBackground;
        for (int i = 0; i < FlangeCount; i++) _materials[i] = "";
        for (int i = 0; i < FlangeCount; i++) _powerLabels[i] = "";

        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    /// <summary>
    /// UI thread only. The first call seeds the display position to avoid a visible
    /// snap from 0. Does not invalidate; the render timer interpolates toward the target.
    /// </summary>
    public void SetHolderState(float position, bool isMoving, bool isHomed)
    {
        if (!_holderDisplayInit)
        {
            _holderDisplayPosition = position;
            _holderDisplayInit = true;
        }
        _holderPosition = position;
        _holderMoving = isMoving;
        _holderHomed = isHomed;
    }

    /// <summary>Same rules as <see cref="SetHolderState"/>.</summary>
    public void SetShutterState(float position, bool isMoving, bool isHomed)
    {
        if (!_shutterDisplayInit)
        {
            _shutterDisplayPosition = position;
            _shutterDisplayInit = true;
        }
        _shutterPosition = position;
        _shutterMoving = isMoving;
        _shutterHomed = isHomed;
    }

    /// <summary><paramref name="flangeIndex"/> is 0-based; null or empty clears.</summary>
    public void SetFlangeMaterial(int flangeIndex, string? material)
    {
        if (flangeIndex < 0 || flangeIndex >= FlangeCount) return;
        string v = material ?? "";
        if (_materials[flangeIndex] == v) return;
        _materials[flangeIndex] = v;
        Invalidate();
    }

    /// <summary>
    /// Drawn as a dim "TYPE · ID" subtitle under "FL N". <paramref name="flangeIndex"/>
    /// is 0-based; null or empty <paramref name="supplyId"/> clears.
    /// </summary>
    public void SetFlangePowerSource(int flangeIndex, string? supplyId, string? powerType)
    {
        if (flangeIndex < 0 || flangeIndex >= FlangeCount) return;

        string formatted = FormatPowerLabel(supplyId, powerType);
        if (_powerLabels[flangeIndex] == formatted) return;
        _powerLabels[flangeIndex] = formatted;
        Invalidate();
    }

    private static string FormatPowerLabel(string? supplyId, string? powerType)
    {
        if (string.IsNullOrWhiteSpace(supplyId)) return "";
        return string.IsNullOrWhiteSpace(powerType)
            ? supplyId
            : $"{powerType} · {supplyId}";
    }

    public static string FormatDepositionTime(double? seconds)
    {
        if (seconds is null) return "--";
        double s = seconds.Value;
        if (s < 100)
            return $"{s:F2} sec";
        int m = (int)Math.Round(s) / 60;
        int r = (int)Math.Round(s) % 60;
        return $"{m} min {r} sec";
    }

    /// <summary><paramref name="flangeIndex"/> is 0-based.</summary>
    public void SetFlangeDepositionSeconds(int flangeIndex, double seconds)
    {
        if (flangeIndex < 0 || flangeIndex >= FlangeCount) return;
        if (_depositionTimeSet[flangeIndex] && _depositionSeconds[flangeIndex] == seconds) return;
        _depositionSeconds[flangeIndex] = seconds;
        _depositionTimeSet[flangeIndex] = true;
        Invalidate();
    }

    /// <summary>
    /// Map a motor step position to the nearest flange (1-based), or -1 if the
    /// position is outside every flange's tolerance window.
    /// </summary>
    public static int PositionToFlange(float position)
    {
        float minDist = float.MaxValue;
        int nearest = -1;
        for (int i = 0; i < FlangeCount; i++)
        {
            float dist = Math.Abs(position - FlangeStepPositions[i]);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = i + 1;
            }
        }
        return minDist <= StepTolerance ? nearest : -1;
    }

    private static float PositionToAngle(float steps)
    {
        float clamped = Math.Clamp(steps, 0f, 20120f);
        return clamped / 20120f * 300f;
    }

    private PointF AngleToPoint(float angleDeg, float cx, float cy)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new PointF(
            cx + _orbitR * (float)Math.Cos(rad),
            cy - _orbitR * (float)Math.Sin(rad));
    }

    private PointF GetFlangeCenter(int flangeIndex, float cx, float cy)
    {
        double angleRad = FlangeAngles[flangeIndex] * Math.PI / 180.0;
        return new PointF(
            cx + _orbitR * (float)Math.Cos(angleRad),
            cy - _orbitR * (float)Math.Sin(angleRad));
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float tickSec = _renderTimer.Interval / 1000f;
        _holderDisplayPosition = Approach(_holderDisplayPosition, _holderPosition,
            FlangeStepSpan / Math.Max(0.05f, HolderFlangeSeconds) * tickSec);
        _shutterDisplayPosition = Approach(_shutterDisplayPosition, _shutterPosition,
            FlangeStepSpan / Math.Max(0.05f, ShutterFlangeSeconds) * tickSec);

        if (Math.Abs(_holderDisplayPosition  - _holderPosition)  > 1f ||
            Math.Abs(_shutterDisplayPosition - _shutterPosition) > 1f ||
            _holderMoving || _shutterMoving)
            Invalidate();
    }

    private static float Approach(float current, float target, float maxStep)
    {
        float d = target - current;
        return Math.Abs(d) <= maxStep ? target : current + Math.Sign(d) * maxStep;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // No size cap: the Process reading column takes its width by docking, and a
        // cap only leaves dead space around the chamber.
        float unit = Math.Min(Width, Height) / 2f;
        _chamberR = unit * RatioChamber;
        _shutterR = unit * RatioShutter;
        _orbitR   = unit * RatioOrbit;
        _flangeR  = unit * RatioFlange;
        _holeR    = unit * RatioHole;

        float cx = Width / 2f;
        float cy = Height / 2f;

        DrawFlanges(g, cx, cy);
        DrawHolderIndicator(g, cx, cy);
        DrawShutterDisc(g, cx, cy);
    }

    private void DrawChamber(Graphics g, float cx, float cy)
    {
        using Pen chamberPen = new(ColorBorder, 2f);
        g.DrawEllipse(chamberPen, cx - _chamberR, cy - _chamberR,
            _chamberR * 2, _chamberR * 2);

        using Font chamberFont = ScaledFont.Create(8f, FontStyle.Regular);
        using SolidBrush dimBrush = new(ColorTextDim);
        SizeF sz = g.MeasureString("Chamber", chamberFont);
        g.DrawString("Chamber", chamberFont, dimBrush, cx - sz.Width / 2f, cy - sz.Height / 2f);
    }

    private void DrawFlanges(Graphics g, float cx, float cy)
    {
        float dpi = g.DpiY / 96f;
        DrawChamber(g, cx, cy);

        using Font labelFont = ScaledFont.Create(9.5f, FontStyle.Bold);
        using Font materialFont = ScaledFont.Create(12.5f, FontStyle.Bold);
        using Font timeFont = ScaledFont.Create(8.5f, FontStyle.Regular);
        using Font powerFont = ScaledFont.Create(9.5f, FontStyle.Regular);

        for (int i = 0; i < FlangeCount; i++)
        {
            int flangeNum = i + 1;
            PointF center = GetFlangeCenter(i, cx, cy);

            _flangeRects[i] = new RectangleF(
                center.X - _flangeR, center.Y - _flangeR,
                _flangeR * 2, _flangeR * 2);

            float x = center.X - _flangeR;
            float y = center.Y - _flangeR;
            float d = _flangeR * 2;

            using Pen circlePen = new(Color.FromArgb(60, 80, 110), 1.5f);
            g.DrawEllipse(circlePen, x, y, d, d);

            string flangeLabel = $"FL {flangeNum}";
            SizeF labelSize = g.MeasureString(flangeLabel, labelFont);
            using SolidBrush dimBrush = new(ColorTextDim);
            g.DrawString(flangeLabel, labelFont, dimBrush,
                center.X - labelSize.Width / 2f, center.Y - _flangeR + 5f * dpi);

            string material = string.IsNullOrEmpty(_materials[i]) ? "---" : _materials[i];
            SizeF matSize = g.MeasureString(material, materialFont);
            using SolidBrush textBrush = new(Color.FromArgb(255, 180, 80));
            g.DrawString(material, materialFont, textBrush,
                center.X - matSize.Width / 2f, center.Y - matSize.Height / 2f);

            string powerLabel = _powerLabels[i];
            if (!string.IsNullOrEmpty(powerLabel))
            {
                SizeF powerSize = g.MeasureString(powerLabel, powerFont);
                float powerY = center.Y + matSize.Height / 2f + 1f;
                float bottomLimit = center.Y + _flangeR - (_showDepositionTimes ? 16f * dpi : 4f * dpi);
                if (powerY + powerSize.Height < bottomLimit)
                {
                    using SolidBrush powerBrush = new(Color.FromArgb(140, 220, 180));
                    g.DrawString(powerLabel, powerFont, powerBrush,
                        center.X - powerSize.Width / 2f, powerY);
                }
            }

            if (_showDepositionTimes)
            {
                double? totalSec = _depositionTimeSet[i] ? _depositionSeconds[i] : (double?)null;
                string timeStr = FormatDepositionTime(totalSec);
                SizeF timeSize = g.MeasureString(timeStr, timeFont);
                g.DrawString(timeStr, timeFont, dimBrush,
                    center.X - timeSize.Width / 2f, center.Y + _flangeR - timeSize.Height - 3f * dpi);
            }
        }
    }

    private void DrawHolderIndicator(Graphics g, float cx, float cy)
    {
        float angle = PositionToAngle(_holderDisplayPosition);
        PointF p = AngleToPoint(angle, cx, cy);
        float d = _flangeR * 2;

        using SolidBrush fill = new(Color.FromArgb(60, 0, 255, 255));
        g.FillEllipse(fill, p.X - _flangeR, p.Y - _flangeR, d, d);

        using Pen pen = new(Color.Cyan, 3f);
        g.DrawEllipse(pen, p.X - _flangeR, p.Y - _flangeR, d, d);
    }

    private void DrawShutterDisc(Graphics g, float cx, float cy)
    {
        float holeAngle = PositionToAngle(_shutterDisplayPosition);
        PointF holeCenter = AngleToPoint(holeAngle, cx, cy);

        // FillMode.Alternate leaves the hole cutout transparent.
        using GraphicsPath path = new(FillMode.Alternate);
        path.AddEllipse(cx - _shutterR, cy - _shutterR, _shutterR * 2, _shutterR * 2);
        path.AddEllipse(holeCenter.X - _holeR, holeCenter.Y - _holeR, _holeR * 2, _holeR * 2);

        using SolidBrush fill = new(Color.FromArgb(140, 25, 40, 58));
        g.FillPath(fill, path);

        using Pen discBorder = new(Color.FromArgb(128, 60, 85, 115), 1.5f);
        g.DrawEllipse(discBorder, cx - _shutterR, cy - _shutterR, _shutterR * 2, _shutterR * 2);

        using Pen holeBorder = new(Color.FromArgb(140, 255, 165, 0), 1.5f);
        holeBorder.DashStyle = DashStyle.Dash;
        g.DrawEllipse(holeBorder, holeCenter.X - _holeR, holeCenter.Y - _holeR, _holeR * 2, _holeR * 2);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        for (int i = 0; i < FlangeCount; i++)
        {
            if (_flangeRects[i].Contains(e.Location))
            {
                FlangeDoubleClicked?.Invoke(this, i + 1);
                return;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
