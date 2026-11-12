using System.Drawing.Drawing2D;
using System.Drawing.Text;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.GUIControls.Process;

internal sealed class LoopProgressCard : Control
{
    private static readonly Color ColorBackground  = Color.FromArgb(13,  27, 42);
    private static readonly Color ColorCardBg      = Color.FromArgb(22,  32, 50);
    private static readonly Color ColorBorder      = Color.FromArgb(40,  60, 90);
    private static readonly Color ColorTextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color ColorTextDim     = Color.FromArgb(119, 136, 153);
    private static readonly Color ColorAccent      = Color.FromArgb(0,   255, 255);
    private static readonly Color ColorDone        = Color.FromArgb(80,  200, 120);
    private static readonly Color ColorPending     = Color.FromArgb(34,  51,  76);

    private static readonly Font TitleFont = ScaledFont.Create(9.5f, FontStyle.Bold);
    private static readonly Font MetaFont  = ScaledFont.Create(8.5f);

    // Below this width a per-iteration segment is an unreadable sliver, so the
    // bar falls back to a single continuous fill with decade ticks. 96-DPI
    // base value — scaled by `s` at the comparison site, same convention as
    // every other pixel geometry constant in this file.
    private const float MinSegmentPx = 4f;

    private LoopProgressSnapshot _snapshot = LoopProgressSnapshot.Inactive;

    public LoopProgressCard()
    {
        Name = "LoopProgressCard";
        DoubleBuffered = true;
        BackColor = ColorBackground;
        Visible = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void Apply(LoopProgressSnapshot snapshot)
    {
        _snapshot = snapshot;
        Visible = snapshot.IsActive;
        if (snapshot.IsActive) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_snapshot.IsActive) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Scale pixel geometry by the monitor DPI factor; the point-sized fonts
        // are GDI+-scaled already — same convention as CurrentStepCard.
        float s = g.DpiY / 96f;

        Rectangle card = Rectangle.Inflate(ClientRectangle, -(int)(8 * s), -(int)(2 * s));
        using (SolidBrush bg = new(ColorCardBg)) g.FillRectangle(bg, card);
        using (Pen border = new(ColorBorder))    g.DrawRectangle(border, card);

        float left  = card.Left  + 10 * s;
        float right = card.Right - 10 * s;
        float y     = card.Top   + 5  * s;

        const string chip = "\u21bb LOOP";
        using (SolidBrush ab = new(ColorAccent))
            g.DrawString(chip, TitleFont, ab, left, y);
        float chipW = g.MeasureString(chip, TitleFont).Width;

        string counter = $"Stack {_snapshot.CurrentStack} / {_snapshot.TotalStacks}";
        using (SolidBrush pb = new(ColorTextPrimary))
            g.DrawString(counter, TitleFont, pb, left + chipW + 10 * s, y);

        string leftText = _snapshot.LoopRemaining is TimeSpan rem
            ? $"left ~{FormatLeft(rem)}"
            : "left --";
        SizeF leftSize = g.MeasureString(leftText, MetaFont);
        using (SolidBrush db = new(ColorTextDim))
            g.DrawString(leftText, MetaFont, db, right - leftSize.Width, y + 2 * s);

        float barY = y + TitleFont.GetHeight(g) + 4 * s;
        // Clamp: a control narrower than the card's fixed insets would otherwise
        // hand DrawIterationBar a negative-width rectangle, which GDI+ does not
        // accept. A very narrow card should just paint nothing there, not throw.
        var bar = new RectangleF(left, barY, Math.Max(0f, right - left), 10 * s);
        DrawIterationBar(g, bar, s);

        string iterText = _snapshot.IterationEstimate is TimeSpan est
            ? $"iter {BaseProcessPanel.FormatHms(_snapshot.IterationElapsed)} / ~{BaseProcessPanel.FormatHms(est)}"
            : $"iter {BaseProcessPanel.FormatHms(_snapshot.IterationElapsed)}";
        using (SolidBrush db = new(ColorTextDim))
            g.DrawString(iterText, MetaFont, db, left, bar.Bottom + 2 * s);
    }

    private void DrawIterationBar(Graphics g, RectangleF bar, float s)
    {
        int   total    = _snapshot.TotalStacks;
        int   current  = _snapshot.CurrentStack;
        float fraction = InIterationFraction();

        using SolidBrush pending = new(ColorPending);
        using SolidBrush done    = new(ColorDone);
        using SolidBrush active  = new(ColorAccent);

        float gap  = 2 * s;
        float segW = (bar.Width - gap * (total - 1)) / total;

        if (segW >= MinSegmentPx * s)
        {
            for (int i = 1; i <= total; i++)
            {
                var cell = new RectangleF(
                    bar.Left + (i - 1) * (segW + gap), bar.Top, segW, bar.Height);

                if (i < current)      g.FillRectangle(done, cell);
                else if (i > current) g.FillRectangle(pending, cell);
                else
                {
                    // Current iteration fills in proportion to its progress. With
                    // no estimate yet (first iteration) the cell is drawn solid
                    // rather than implying a progress we cannot know.
                    g.FillRectangle(pending, cell);
                    float w = fraction > 0 ? cell.Width * fraction : cell.Width;
                    g.FillRectangle(active, new RectangleF(cell.Left, cell.Top, w, cell.Height));
                }
            }
            return;
        }

        g.FillRectangle(pending, bar);
        float doneFrac = (current - 1) / (float)total;
        g.FillRectangle(done, new RectangleF(bar.Left, bar.Top, bar.Width * doneFrac, bar.Height));

        float overall = (current - 1 + (fraction > 0 ? fraction : 1f)) / total;
        g.FillRectangle(active, new RectangleF(
            bar.Left + bar.Width * doneFrac, bar.Top, bar.Width * (overall - doneFrac), bar.Height));

        using Pen tick = new(ColorBackground, 1f);
        for (int i = 10; i < total; i += 10)
        {
            float tx = bar.Left + bar.Width * i / total;
            g.DrawLine(tick, tx, bar.Top, tx, bar.Bottom);
        }
    }

    private float InIterationFraction()
    {
        if (_snapshot.IterationEstimate is not TimeSpan est || est <= TimeSpan.Zero) return 0f;
        return (float)Math.Clamp(
            _snapshot.IterationElapsed.TotalSeconds / est.TotalSeconds, 0, 1);
    }

    private static string FormatLeft(TimeSpan ts)
    {
        if (ts.TotalHours   >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} min";
        return $"{ts.Seconds} s";
    }
}
