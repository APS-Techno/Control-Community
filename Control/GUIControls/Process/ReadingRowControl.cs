using System.Drawing.Drawing2D;
using System.Drawing.Text;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Custom-painted (no child controls) so ~15 rows × 5 Hz repaint stays cheap.
///
/// Warning and Alarm override <see cref="ReadingSpec.Accent"/> — a fault must
/// never be disguised as a nicer-looking colour.
/// </summary>
internal sealed class ReadingRowControl : Control
{
    private static readonly Color ColorTextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color ColorTextDim     = Color.FromArgb(119, 136, 153);
    private static readonly Color ColorRowSep      = Color.FromArgb(22,  35,  51);
    private static readonly Color ColorSparkOk     = Color.FromArgb(80,  200, 120);
    private static readonly Color ColorSparkWarn   = Color.FromArgb(255, 200, 50);
    private static readonly Color ColorSparkAlarm  = Color.FromArgb(255, 80,  80);
    private static readonly Color ColorBackground  = Color.FromArgb(13,  27, 42);

    // Mini trend chart chrome — a recessed well, a frame, a grid and a
    // marked-up vertical axis, so a trace reads as a measurement and not as a
    // stray coloured line floating on the row.
    private static readonly Color ColorPlotWell   = Color.FromArgb(11,  22,  37);
    private static readonly Color ColorPlotFrame  = Color.FromArgb(48,  72, 105);
    private static readonly Color ColorPlotGrid   = Color.FromArgb(40,  62,  91);
    private static readonly Color ColorPlotAxis   = Color.FromArgb(72, 102, 142);
    private static readonly Color ColorAxisLabel  = Color.FromArgb(104, 130, 162);

    // Row geometry, 96-DPI bases scaled by the paint-time DPI factor.
    private const int RowPadX      = 6;    // side padding for caption and chart
    private const int CaptionTop   = 1;
    private const int CaptionGap   = 2;    // caption line → chart
    private const int RowBottomPad = 3;    // chart → row separator
    private const int MinPlotWidth  = 24;
    private const int MinPlotHeight = 10;  // below this the chart is dropped entirely

    // Chart geometry, 96-DPI bases scaled by the paint-time DPI factor.
    private const int AxisTickLength   = 3;
    private const int TimeDivisions    = 3;    // vertical grid lines drawn inside the plot
    private const int MinGridSpacing   = 9;    // thin the value grid below this line pitch
    // Below these the plot is too cramped for range labels / a tick-marked axis;
    // the trace, frame and grid still render. Measured against the chart alone.
    private const int MinHeightForLabels = 22;
    private const int MinWidthForLabels  = 70;
    private const int MinHeightForTicks  = 14;

    // Seed row height (96-DPI base). The stacks stretch rows past this to fill
    // the column — see BaseProcessPanel.ComputeReadingRowHeight, whose base
    // row height must match this value.
    private const int SeedRowHeight = 40;

    // Monitor DPI factor for WinForms-control sizing.
    // The paint path derives its own factor from Graphics.DpiY instead.
    private float DpiF => DeviceDpi / 96f;

    private static readonly Font LabelFont = ScaledFont.Create(9.5f);
    private static readonly Font ValueFont = new("Consolas", 10.5f, FontStyle.Bold);
    private static readonly Font UnitsFont = ScaledFont.Create(8.5f);
    private static readonly Font AxisFont  = ScaledFont.Create(6.5f);

    private readonly ReadingSpec _spec;
    private readonly ReadingRingBuffer _history;
    private readonly float[] _scratch;

    // Second trace (ReadingSpec.Secondary) — its own history and axis, drawn
    // over the primary on the same chart. Null-capacity buffers are never
    // allocated for single-trace rows.
    private readonly ReadingRingBuffer? _history2;
    private readonly float[]? _scratch2;
    private double? _value2;

    private double? _value;
    private ReadingState _state = ReadingState.Off;

    public ReadingSpec Spec => _spec;
    public ReadingRingBuffer History => _history;

    /// <summary>Last primary value applied (null = none / offline). Test seam.</summary>
    internal double? PrimaryValue => _value;
    /// <summary>Last secondary-trace value applied (null = none / no secondary). Test seam.</summary>
    internal double? SecondaryValue => _value2;

    public ReadingRowControl(ReadingSpec spec, int historyCapacity = 120)
    {
        _spec = spec;
        _history = new ReadingRingBuffer(historyCapacity);
        _scratch = new float[historyCapacity];
        if (spec.Secondary != null)
        {
            _history2 = new ReadingRingBuffer(historyCapacity);
            _scratch2 = new float[historyCapacity];
        }

        DoubleBuffered = true;
        BackColor = ColorBackground;
        ForeColor = ColorTextPrimary;
        Height = (int)(SeedRowHeight * DpiF);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void ApplyReading(ProcessReading reading)
    {
        if (_spec.Secondary is { } sec && reading.Id == sec.Id)
        {
            // The secondary trace shares the row's state (a supply that is off
            // is off for both V and I) — only the primary reading sets it.
            _value2 = reading.Value;
            if (reading.Value is double v2) _history2!.Add((float)v2);
            Invalidate();
            return;
        }

        _value = reading.Value;
        _state = reading.State;
        if (reading.Value is double v) _history.Add((float)v);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Scale pixel geometry by the monitor DPI factor (fonts are point-sized
        // and GDI+-scaled, so they need no adjustment here).
        float s = g.DpiY / 96f;
        int padX = (int)(RowPadX * s);

        Rectangle rc = ClientRectangle;

        using (Pen sep = new(ColorRowSep))
            g.DrawLine(sep, rc.Left, rc.Bottom - 1, rc.Right, rc.Bottom - 1);

        // Every text is centred in a band as tall as the tallest of the three
        // fonts, so the label and the value share one optical baseline.
        float labelH = LabelFont.GetHeight(g);
        float valueH = ValueFont.GetHeight(g);
        float unitsH = UnitsFont.GetHeight(g);
        float captionH = Math.Max(labelH, Math.Max(valueH, unitsH));
        float captionTop = CaptionTop * s;

        Color labelColor = _state == ReadingState.Off ? ColorTextDim : ColorTextPrimary;
        using (SolidBrush lb = new(labelColor))
            g.DrawString(_spec.DisplayLabel, LabelFont, lb,
                padX, captionTop + (captionH - labelH) / 2f);

        string valueText;
        Color valueColor;
        if (_state == ReadingState.Off || _value is null)
        {
            valueText = _state == ReadingState.Off ? "off" : "—";
            valueColor = ColorTextDim;
        }
        else
        {
            valueText = _value.Value.ToString(_spec.Format,
                System.Globalization.CultureInfo.InvariantCulture);
            valueColor = SparkColor();
        }

        // Right-aligned: units against the row's right padding, the value just
        // left of them. A dual-trace row continues leftwards:
        // "<v1> <u1> · <v2> <u2>", the separator dim.
        float cursor = rc.Right - padX;
        if (_spec.Secondary is { } sec)
        {
            string v2Text;
            Color v2Color;
            if (_state == ReadingState.Off || _value2 is null)
            {
                v2Text = _state == ReadingState.Off ? "off" : "—";
                v2Color = ColorTextDim;
            }
            else
            {
                v2Text = _value2.Value.ToString(sec.Format,
                    System.Globalization.CultureInfo.InvariantCulture);
                v2Color = SecondaryColor();
            }
            cursor = DrawValueUnits(g, v2Text, v2Color, sec.Units, cursor, captionTop, captionH, s);
            SizeF sepSize = g.MeasureString("·", UnitsFont);
            cursor -= sepSize.Width + 3 * s;
            using SolidBrush sb = new(ColorTextDim);
            g.DrawString("·", UnitsFont, sb, cursor, captionTop + (captionH - unitsH) / 2f);
            cursor -= 3 * s;
        }
        DrawValueUnits(g, valueText, valueColor, _spec.Units, cursor, captionTop, captionH, s);

        int plotTop    = (int)(captionTop + captionH + CaptionGap * s);
        int plotBottom = rc.Bottom - (int)(RowBottomPad * s);
        int plotH      = plotBottom - plotTop;
        if (plotH < MinPlotHeight * s) return;

        var plot = new Rectangle(
            padX, plotTop,
            Math.Max((int)(MinPlotWidth * s), rc.Width - 2 * padX), plotH);

        // The well and frame are drawn whenever the row is meant to have a
        // chart, so a column of rows keeps a single alignment even while some
        // sensors are still off or have no history yet — only the trace inside
        // waits for data.
        if (_spec.ShowSparkline)
        {
            bool hasTrace = _state != ReadingState.Off && _history.Count >= 2;
            DrawMiniChart(g, plot, s, hasTrace);
        }
        else
        {
            using SolidBrush db = new(ColorTextDim);
            SizeF dashSize = g.MeasureString("—", LabelFont);
            g.DrawString("—", LabelFont, db,
                plot.Left + (plot.Width - dashSize.Width) / 2f,
                plot.Top + (plot.Height - dashSize.Height) / 2f);
        }
    }

    /// <summary>
    /// Draws "<c>value units</c>" right-aligned so its right edge sits at
    /// <paramref name="rightX"/>; returns the x where the text starts.
    /// </summary>
    private static float DrawValueUnits(Graphics g, string valueText, Color valueColor,
        string units, float rightX, float captionTop, float captionH, float s)
    {
        float valueH = ValueFont.GetHeight(g);
        float unitsH = UnitsFont.GetHeight(g);
        bool hasUnits = !string.IsNullOrEmpty(units);
        SizeF unitsSize = hasUnits ? g.MeasureString(units, UnitsFont) : SizeF.Empty;
        SizeF valueSize = g.MeasureString(valueText, ValueFont);

        float unitsX = rightX - unitsSize.Width;
        float valueX = unitsX - valueSize.Width - (hasUnits ? 2 * s : 0);

        using (SolidBrush vb = new(valueColor))
            g.DrawString(valueText, ValueFont, vb, valueX, captionTop + (captionH - valueH) / 2f);
        if (hasUnits)
        {
            using SolidBrush ub = new(ColorTextDim);
            g.DrawString(units, UnitsFont, ub, unitsX, captionTop + (captionH - unitsH) / 2f);
        }
        return valueX;
    }

    /// <summary>
    /// Colour of the trace, its fill, the end-of-trace marker and the value
    /// text. The spec's accent applies in the healthy state only: Warning and
    /// Alarm keep their fixed amber / red, and an off reading stays dim.
    /// </summary>
    private Color SparkColor() => _state switch
    {
        ReadingState.Ok      => _spec.Accent ?? ColorSparkOk,
        ReadingState.Warning => ColorSparkWarn,
        ReadingState.Alarm   => ColorSparkAlarm,
        _                    => ColorTextDim,
    };

    /// <summary>
    /// Colour of the secondary trace and its caption value: the trace's own
    /// accent, else the primary colour lightened towards white so the two
    /// traces still separate. Warning / Alarm / Off follow the primary — the
    /// pair is one signal, and a fault must colour both.
    /// </summary>
    private Color SecondaryColor() => _state switch
    {
        ReadingState.Ok => _spec.Secondary?.Accent ?? Blend(SparkColor(), Color.White, 0.45f),
        _               => SparkColor(),
    };

    // The chart chrome picks up a fraction of the accent so a row is
    // identifiable even before its trace has data — a gauge that is still off,
    // or a panel opened seconds ago. The blend stays low: chrome must not
    // compete with the trace drawn over it.
    private Color FrameColor() => Blend(ColorPlotFrame, _spec.Accent, 0.45f);
    private Color AxisColor()  => Blend(ColorPlotAxis,  _spec.Accent, 0.35f);

    internal static Color Blend(Color baseColor, Color? accent, float t)
    {
        if (accent is not Color a) return baseColor;
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(baseColor.R + (a.R - baseColor.R) * t),
            (int)(baseColor.G + (a.G - baseColor.G) * t),
            (int)(baseColor.B + (a.B - baseColor.B) * t));
    }

    /// <param name="plot">Outer chart rectangle, frame included.</param>
    /// <param name="s">DPI factor for the 96-DPI pixel geometry.</param>
    /// <param name="hasTrace">False while the reading is off or has &lt; 2
    /// samples — the empty chart is still drawn, with a dash in the middle.</param>
    private void DrawMiniChart(Graphics g, Rectangle plot, float s, bool hasTrace)
    {
        var inner = Rectangle.Inflate(plot, -1, -1);
        if (inner.Width <= 2 || inner.Height <= 2) return;

        using (SolidBrush well = new(ColorPlotWell))
            g.FillRectangle(well, plot);

        if (!hasTrace)
        {
            DrawFrame(g, plot, FrameColor());
            using SolidBrush db = new(ColorTextDim);
            SizeF dash = g.MeasureString("—", AxisFont);
            g.DrawString("—", AxisFont, db,
                plot.Left + (plot.Width - dash.Width) / 2f,
                plot.Top + (plot.Height - dash.Height) / 2f);
            return;
        }

        int count = _history.CopyTo(_scratch);

        // A log row scales over the positive samples only: a gauge that reads a
        // hard 0 while it is off would otherwise drag the axis to -infinity.
        var (dataMin, dataMax) = _spec.Scale == ReadingScale.Log
            ? _history.MinMaxPositive()
            : _history.MinMax();

        var axis = SparklineScale.Build(dataMin, dataMax, _spec.Scale);

        bool showLabels = plot.Height >= MinHeightForLabels * s
                          && plot.Width >= MinWidthForLabels * s;
        bool showTicks  = plot.Height >= MinHeightForTicks * s;

        float tickLen = AxisTickLength * s;

        // Range labels are drawn first: the trace crosses over them.
        if (showLabels)
        {
            using SolidBrush lb = new(ColorAxisLabel);
            float labelX = inner.Left + tickLen + 2 * s;
            g.DrawString(SparklineScale.Format(axis.Max), AxisFont, lb,
                labelX, inner.Top + 1 * s);
            SizeF minSize = g.MeasureString(SparklineScale.Format(axis.Min), AxisFont);
            g.DrawString(SparklineScale.Format(axis.Min), AxisFont, lb,
                labelX, inner.Bottom - minSize.Height - 1 * s);
        }

        // Thin the value grid so lines never crowd closer than MinGridSpacing —
        // a 24 px row with a 6-interval axis would otherwise be solid grid.
        int stride = GridStride(axis.Ticks.Length, inner.Height, MinGridSpacing * s);

        // Grid and axis are hairlines: anti-aliasing turns a 1 px dotted line
        // into a smear that reads as noise rather than as a rule.
        var priorSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;

        // Skip value grid lines that coincide with the frame edges.
        using (Pen grid = new(ColorPlotGrid, 1f) { DashStyle = DashStyle.Dot })
        {
            for (int i = 0; i < axis.Ticks.Length; i += stride)
            {
                float f = axis.Fraction(axis.Ticks[i]);
                if (f <= 0.001f || f >= 0.999f) continue;
                float y = inner.Bottom - f * inner.Height;
                g.DrawLine(grid, inner.Left, y, inner.Right, y);
            }

            // --- Time grid: the history window is a fixed span (capacity ×
            // the telemetry tick), so these divisions are equal slices of it ---
            for (int i = 1; i <= TimeDivisions; i++)
            {
                float x = inner.Left + inner.Width * i / (float)(TimeDivisions + 1);
                g.DrawLine(grid, x, inner.Top, x, inner.Bottom);
            }
        }

        using (Pen axisPen = new(AxisColor(), 1f))
        {
            g.DrawLine(axisPen, inner.Left, inner.Top, inner.Left, inner.Bottom);

            if (showTicks)
            {
                for (int i = 0; i < axis.Ticks.Length; i += stride)
                {
                    float y = inner.Bottom - axis.Fraction(axis.Ticks[i]) * inner.Height;
                    y = Math.Clamp(y, inner.Top, inner.Bottom - 1);
                    g.DrawLine(axisPen, inner.Left, y, inner.Left + tickLen, y);
                }
            }
        }

        g.SmoothingMode = priorSmoothing;

        // X maps by ring-buffer capacity, not by sample count, so the newest
        // sample is always at the right edge and the time grid keeps a constant
        // scale: a partly-filled buffer grows in from the right instead of
        // stretching a handful of samples across the whole plot.
        int capacity = Math.Max(2, _history.Capacity);
        float stepX = inner.Width / (float)(capacity - 1);

        var pts = new PointF[count];
        for (int i = 0; i < count; i++)
        {
            float x = inner.Right - (count - 1 - i) * stepX;
            float y = inner.Bottom - axis.Fraction(_scratch[i]) * inner.Height;
            pts[i] = new PointF(x, y);
        }

        var clipState = g.Save();
        g.SetClip(inner);

        using (GraphicsPath fill = new())
        {
            fill.AddLines(pts);
            fill.AddLine(pts[^1].X, inner.Bottom, pts[0].X, inner.Bottom);
            fill.CloseFigure();
            using LinearGradientBrush fb = new(
                new RectangleF(inner.Left, inner.Top, inner.Width, inner.Height + 1),
                Color.FromArgb(64, SparkColor()),
                Color.FromArgb(0, SparkColor()),
                LinearGradientMode.Vertical);
            g.FillPath(fb, fill);
        }

        using (Pen trace = new(SparkColor(), 1.5f * s)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        })
        {
            g.DrawLines(trace, pts);
        }

        float r = 2f * s;
        using (SolidBrush dot = new(SparkColor()))
            g.FillEllipse(dot, pts[^1].X - r, pts[^1].Y - r, r * 2, r * 2);

        DrawSecondaryTrace(g, inner, s, stepX, showLabels, tickLen);

        g.Restore(clipState);

        DrawFrame(g, plot, FrameColor());
    }

    /// <summary>
    /// The second trace over the primary: its own linear axis, range labels on
    /// the right edge (the primary's sit on the left) with a tick per label,
    /// no fill — the primary's fill already gives the chart its body, and a
    /// second fill would muddy where the two cross.
    /// </summary>
    private void DrawSecondaryTrace(Graphics g, Rectangle inner, float s, float stepX,
        bool showLabels, float tickLen)
    {
        if (_history2 is null || _scratch2 is null) return;
        if (_state == ReadingState.Off || _history2.Count < 2) return;

        int count = _history2.CopyTo(_scratch2);
        var (min, max) = _history2.MinMax();
        var axis = SparklineScale.Build(min, max, ReadingScale.Linear);
        Color color = SecondaryColor();

        if (showLabels)
        {
            using SolidBrush lb = new(Blend(ColorAxisLabel, color, 0.5f));
            string maxText = SparklineScale.Format(axis.Max);
            string minText = SparklineScale.Format(axis.Min);
            SizeF maxSize = g.MeasureString(maxText, AxisFont);
            SizeF minSize = g.MeasureString(minText, AxisFont);
            float labelRight = inner.Right - tickLen - 2 * s;
            g.DrawString(maxText, AxisFont, lb, labelRight - maxSize.Width, inner.Top + 1 * s);
            g.DrawString(minText, AxisFont, lb, labelRight - minSize.Width,
                inner.Bottom - minSize.Height - 1 * s);

            var priorSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (Pen tick = new(Blend(ColorPlotAxis, color, 0.35f), 1f))
            {
                float yMax = Math.Clamp(inner.Bottom - axis.Fraction(axis.Max) * inner.Height, inner.Top, inner.Bottom - 1);
                float yMin = Math.Clamp(inner.Bottom - axis.Fraction(axis.Min) * inner.Height, inner.Top, inner.Bottom - 1);
                g.DrawLine(tick, inner.Right - tickLen, yMax, inner.Right, yMax);
                g.DrawLine(tick, inner.Right - tickLen, yMin, inner.Right, yMin);
            }
            g.SmoothingMode = priorSmoothing;
        }

        var pts = new PointF[count];
        for (int i = 0; i < count; i++)
        {
            float x = inner.Right - (count - 1 - i) * stepX;
            float y = inner.Bottom - axis.Fraction(_scratch2[i]) * inner.Height;
            pts[i] = new PointF(x, y);
        }

        using (Pen trace = new(color, 1.25f * s)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        })
        {
            g.DrawLines(trace, pts);
        }

        float r = 2f * s;
        using SolidBrush dot = new(color);
        g.FillEllipse(dot, pts[^1].X - r, pts[^1].Y - r, r * 2, r * 2);
    }

    /// <summary>
    /// Step to take through the axis ticks so drawn grid lines stay at least
    /// <paramref name="minSpacing"/> pixels apart. Always ≥ 1.
    /// </summary>
    internal static int GridStride(int tickCount, int plotHeight, float minSpacing)
    {
        int intervals = tickCount - 1;
        if (intervals < 1 || plotHeight <= 0 || minSpacing <= 0) return 1;

        float pitch = plotHeight / (float)intervals;
        return pitch >= minSpacing ? 1 : (int)Math.Ceiling(minSpacing / pitch);
    }

    private static void DrawFrame(Graphics g, Rectangle plot, Color color)
    {
        using Pen frame = new(color, 1f);
        g.DrawRectangle(frame, plot.X, plot.Y, plot.Width - 1, plot.Height - 1);
    }
}
