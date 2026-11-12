using System.Drawing.Drawing2D;
using System.Drawing.Text;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Params column hides when no
/// known parameter keys are present in ProcessStepInfo.Parameters.
///
/// Known parameter keys (case-insensitive):
///   "DepRate"          -> "Rate x.x Å/s"         (from Parameters["DepRate"].Value + .Unit)
///   "CurrentThickness" -> first line numerator
///   "TargetThickness"  -> first line denominator (combined with CurrentThickness)
/// </summary>
internal sealed class CurrentStepCard : Control
{
    private static readonly Color ColorBackground  = Color.FromArgb(13,  27, 42);
    private static readonly Color ColorCardBg      = Color.FromArgb(22,  32, 50);
    private static readonly Color ColorBorder      = Color.FromArgb(40,  60, 90);
    private static readonly Color ColorTextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color ColorTextDim     = Color.FromArgb(119, 136, 153);
    private static readonly Color ColorAccent      = Color.FromArgb(0,   255, 255);
    private static readonly Color ColorProgressOk  = Color.FromArgb(80,  200, 120);

    private static readonly Font MaterialFont = ScaledFont.Create(20f, FontStyle.Bold);
    private static readonly Font TitleFont    = ScaledFont.Create(10f, FontStyle.Bold);
    private static readonly Font MetaFont     = ScaledFont.Create(8.5f);
    private static readonly Font ParamLabelF  = ScaledFont.Create(8f);
    private static readonly Font ParamValueF  = new("Consolas", 9f, FontStyle.Bold);

    private ProcessStepInfo? _step;
    private int _stepNumber;
    private int _totalSteps;

    public CurrentStepCard()
    {
        DoubleBuffered = true;
        BackColor = ColorBackground;
        Height = 64;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void ApplyStep(ProcessStepInfo? step)
    {
        _step = step;
        Invalidate();
    }

    public void ApplyRecipeProgress(int currentStep, int totalSteps)
    {
        _stepNumber = currentStep;
        _totalSteps = totalSteps;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Scale pixel geometry by the monitor DPI factor (fonts are point-sized
        // and GDI+-scaled, so they need no adjustment) — same convention as
        // BaseProcessPanel.OnPaintHeader.
        float s = g.DpiY / 96f;

        Rectangle full = ClientRectangle;
        Rectangle card = Rectangle.Inflate(full, -(int)(8 * s), -(int)(2 * s));

        using (SolidBrush bg = new(ColorCardBg))
            g.FillRectangle(bg, card);
        using (Pen border = new(ColorBorder))
            g.DrawRectangle(border, card);

        if (_step is null)
        {
            using SolidBrush db = new(ColorTextDim);
            g.DrawString("No step active", MetaFont, db, card.Left + 10 * s, card.Top + card.Height / 2f - 6 * s);
            return;
        }

        int x = card.Left + (int)(10 * s);
        int y = card.Top + (int)(6 * s);
        int innerH = card.Height - (int)(12 * s);

        if (!string.IsNullOrEmpty(_step.Material))
        {
            SizeF sz = g.MeasureString(_step.Material, MaterialFont);
            using SolidBrush mb = new(ColorAccent);
            g.DrawString(_step.Material, MaterialFont, mb, x, y + (innerH - sz.Height) / 2f);
            x += (int)(sz.Width + 18 * s);
        }

        int paramsRight = card.Right - (int)(10 * s);
        int paramsLeft = paramsRight;
        var paramLines = BuildParamLines(_step.Parameters);
        if (paramLines.Count > 0)
        {
            float maxWidth = 0;
            foreach (var (label, value) in paramLines)
            {
                SizeF s1 = g.MeasureString(label, ParamLabelF);
                SizeF s2 = g.MeasureString(value, ParamValueF);
                maxWidth = Math.Max(maxWidth, s1.Width + s2.Width + 6 * s);
            }
            paramsLeft = (int)(paramsRight - maxWidth - 4 * s);

            float py = y;
            foreach (var (label, value) in paramLines)
            {
                SizeF s1 = g.MeasureString(label, ParamLabelF);
                SizeF s2 = g.MeasureString(value, ParamValueF);
                float rowY = py;
                using (SolidBrush db = new(ColorTextDim))
                    g.DrawString(label, ParamLabelF, db, paramsRight - s2.Width - 6 * s - s1.Width, rowY + 2 * s);
                using (SolidBrush vb = new(ColorTextPrimary))
                    g.DrawString(value, ParamValueF, vb, paramsRight - s2.Width, rowY);
                py += Math.Max(s1.Height, s2.Height) + 2 * s;
            }
        }

        int midLeft = x;
        int midRight = paramsLeft - (int)(10 * s);
        int midWidth = Math.Max((int)(60 * s), midRight - midLeft);

        string title = _totalSteps > 0
            ? $"{_step.StepType} · Step {_stepNumber} / {_totalSteps}"
            : _step.StepType;
        using (SolidBrush tb = new(ColorTextPrimary))
            g.DrawString(title, TitleFont, tb, midLeft, y);

        // Progress bar — positioned below the title's measured height so the
        // bar never paints over the text descenders as fonts grow on
        // high-DPI monitors (22px legacy baseline kept at 96 DPI).
        double fraction = _step.TotalSeconds > 0
            ? Math.Clamp(_step.ElapsedSeconds / _step.TotalSeconds, 0, 1)
            : 0;

        int barY = y + (int)Math.Max(22 * s, TitleFont.GetHeight(g) + 2 * s);
        int barH = (int)(6 * s);
        var barRect = new Rectangle(midLeft, barY, midWidth, barH);
        using (SolidBrush barBg = new(Color.FromArgb(34, 51, 76)))
            g.FillRectangle(barBg, barRect);
        int fillWidth = (int)(midWidth * fraction);
        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(barRect.Left, barRect.Top, fillWidth, barRect.Height);
            using LinearGradientBrush grad = new(
                fillRect,
                ColorAccent,
                ColorProgressOk,
                LinearGradientMode.Horizontal);
            g.FillRectangle(grad, fillRect);
        }

        string meta = _step.TotalSeconds > 0
            ? $"{FormatHms(_step.ElapsedSeconds)} / {FormatHms(_step.TotalSeconds)} · {fraction * 100:F0}%"
            : $"{FormatHms(_step.ElapsedSeconds)} elapsed";
        using (SolidBrush mb = new(ColorTextDim))
            g.DrawString(meta, MetaFont, mb, midLeft, barY + barH + 4 * s);
    }

    private static List<(string Label, string Value)> BuildParamLines(
        IReadOnlyDictionary<string, ProcessParameter> parameters)
    {
        var lines = new List<(string, string)>();

        if (TryGet(parameters, "DepRate", out var rate))
        {
            lines.Add(("Rate", $"{rate.Value:F1} {rate.Unit}"));
        }

        bool hasCurrent = TryGet(parameters, "CurrentThickness", out var cur);
        bool hasTarget  = TryGet(parameters, "TargetThickness",  out var tgt);
        if (hasCurrent && hasTarget)
        {
            string unit = tgt.Unit ?? cur.Unit ?? "";
            lines.Add(("Thickness", $"{cur.Value:F0} / {tgt.Value:F0} {unit}".TrimEnd()));
        }
        else if (hasCurrent)
        {
            lines.Add(("Thickness", $"{cur.Value:F0} {cur.Unit}".TrimEnd()));
        }

        return lines;
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, ProcessParameter> parameters,
        string key,
        out ProcessParameter value)
    {
        // Case-insensitive lookup — tolerate whatever casing the Worker chooses.
        foreach (var kvp in parameters)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    private static string FormatHms(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        int total = (int)Math.Round(seconds);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }
}
