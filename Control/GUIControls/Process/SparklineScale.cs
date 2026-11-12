using System.Globalization;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// <see cref="Log"/> is for readings that span decades (chamber / load-lock
/// pressure): on a linear axis a 1e-5 → 1e-1 pump-down is a hockey stick with
/// every decade below the peak flattened onto the baseline.
/// </summary>
public enum ReadingScale { Linear, Log }

/// <summary>
/// Fractions are 0..1 of the plot height (0 = bottom).
/// </summary>
internal readonly struct SparkAxis
{
    /// <summary>Value at the bottom of the plot.</summary>
    public double Min { get; }

    /// <summary>Value at the top of the plot.</summary>
    public double Max { get; }

    /// <summary>Grid line / tick values in data units, ascending, including
    /// <see cref="Min"/> and <see cref="Max"/>.</summary>
    public double[] Ticks { get; }

    public bool IsLog { get; }

    // Axis space: data units for a linear axis, log10(data units) for a log one.
    private readonly double _lo;
    private readonly double _span;

    public SparkAxis(double min, double max, double[] ticks, bool isLog)
    {
        Min = min;
        Max = max;
        Ticks = ticks;
        IsLog = isLog;

        _lo = isLog ? Math.Log10(min) : min;
        double hi = isLog ? Math.Log10(max) : max;
        _span = hi - _lo;
        if (!(_span > 0)) _span = 1;   // also catches NaN
    }

    /// <summary>
    /// Fraction of the plot height for <paramref name="value"/>: 0 at
    /// <see cref="Min"/>, 1 at <see cref="Max"/>. Clamped, so an out-of-range
    /// sample rides the frame instead of being drawn outside the plot.
    /// </summary>
    public float Fraction(double value)
    {
        if (double.IsNaN(value)) return 0f;
        double v = IsLog ? Math.Log10(Math.Max(value, Min)) : value;
        return (float)Math.Clamp((v - _lo) / _span, 0.0, 1.0);
    }
}

/// <summary>
/// Builds the vertical axis for a reading row's mini trend chart: a "nice"
/// 1/2/5-rounded range (or whole decades on a log axis) so grid lines land on
/// round numbers and the two corner labels stay short.
/// </summary>
internal static class SparklineScale
{
    /// <summary>Most grid intervals allowed in one plot. Rows are 24-120 px
    /// tall, so more than this turns the plot into a block of grid; the
    /// renderer thins further when the row is short.</summary>
    private const int MaxIntervals = 6;

    /// <summary>Linear-axis headroom above and below the data's swing, as a
    /// fraction of the plot height: the swing is padded to fill at most
    /// 1 - 2 × this of the axis, so the trace sits between the 20 % and 80 %
    /// lines before the nice-step rounding widens it further. Log axes take
    /// their headroom from decade
    /// snapping instead — an extra decade on each side would squash a
    /// pump-down trace, and <see cref="LogSnap"/> exists precisely to avoid
    /// opening spurious decades.</summary>
    internal const double HeadroomFraction = 0.2;

    /// <summary>Widest log axis we will draw before thinning the decade grid.</summary>
    private const int MaxLogDecadeLines = 6;

    /// <summary>Widest log axis in decades. A sanity cap only — the system range
    /// (1e-8 .. 750 Torr) is ~11 decades and must fit; this exists so one
    /// glitched near-zero sample can't open a 30-decade axis that flattens the
    /// rest of the window onto the top frame.</summary>
    private const double LogFloorDecades = 12;

    /// <summary>
    /// Tolerance, in decades, when snapping a bound to a whole decade. History
    /// samples are <c>float</c>, so a reading of 1e-6 Torr widens to
    /// 9.9999999e-7 as a double and its log10 lands at -6.0000000011 — floored
    /// naively that opens a spurious extra decade below the data.
    /// </summary>
    private const double LogSnap = 1e-6;

    /// <summary>
    /// Resolves the vertical axis for one plot. For a log axis, pass the
    /// min/max over the <em>positive</em> samples only
    /// (<see cref="ReadingRingBuffer.MinMaxPositive"/>) — a window with nothing
    /// positive in it falls back to a linear axis.
    /// </summary>
    public static SparkAxis Build(float dataMin, float dataMax, ReadingScale scale)
        => scale == ReadingScale.Log && dataMax > 0
            ? BuildLog(dataMin, dataMax)
            : BuildLinear(dataMin, dataMax);

    private static SparkAxis BuildLinear(double min, double max)
    {
        if (double.IsNaN(min) || double.IsNaN(max) ||
            double.IsInfinity(min) || double.IsInfinity(max))
        {
            min = 0;
            max = 1;
        }
        if (max < min) (min, max) = (max, min);

        bool nonNegative, nonPositive;
        double range = max - min;
        if (!(range > 0))
        {
            // Flat signal: open a band around it so the trace sits on the centre
            // line instead of collapsing onto the bottom frame. The sign test
            // runs on the band, so a steady zero keeps a band to sit in the
            // middle of rather than being clamped to a 0..step plot.
            double pad = Math.Abs(min) > 1e-12 ? Math.Abs(min) * HeadroomFraction : 1.0;
            min -= pad;
            max += pad;
            nonNegative = min >= 0;
            nonPositive = max <= 0;
        }
        else
        {
            // Sign of the raw data, read before the headroom below: a mass flow
            // that idles at 0 must not gain a negative band from it.
            nonNegative = min >= 0;
            nonPositive = max <= 0;

            // Headroom: the trace never touches the frame — a fifth of the
            // PLOT stays free above and below, so the swing must fill at most
            // 60 % of the axis. H/(1-2H) of the swing lands the data at
            // 20 %..80 % before rounding, which only widens.
            double pad = range * (HeadroomFraction / (1 - 2 * HeadroomFraction));
            min -= pad;
            max += pad;
        }
        range = max - min;

        // A reading that never goes negative gets no negative range: a mass flow
        // that idles at 0 would otherwise spend half its plot below the axis.
        // Zero is a multiple of every candidate step, so clamping there keeps
        // the grid aligned.

        // Walk up the 1/2/5 ladder until the rounded-outward range fits in
        // MaxIntervals steps. Clamping at zero can free an interval, so the
        // check has to happen after the clamp rather than on the raw range.
        double step = NiceStep(range / MaxIntervals);
        double lo, hi;
        for (int guard = 0; ; guard++)
        {
            lo = Math.Floor(min / step) * step;
            hi = Math.Ceiling(max / step) * step;
            if (nonNegative) lo = Math.Max(lo, 0);
            if (nonPositive) hi = Math.Min(hi, 0);
            if (hi - lo < step * 0.5) hi = lo + step;   // data sat exactly on a step

            if (guard >= 8 || Math.Round((hi - lo) / step) <= MaxIntervals) break;
            step = NiceStep(step * 1.5);               // next rung: 1→2→5→10
        }

        return new SparkAxis(lo, hi, Steps(lo, hi, step), isLog: false);
    }

    private static SparkAxis BuildLog(double min, double max)
    {
        // Callers pass the positive-only minimum, so min > 0 in practice; the
        // clamp below is the defensive path plus the span cap that keeps a
        // glitched near-zero sample from opening an absurdly wide axis.
        double floor = max / Math.Pow(10, LogFloorDecades);
        if (min <= 0 || min < floor) min = floor;
        if (min > max) min = max;

        int lo = (int)Math.Floor(Math.Log10(min) + LogSnap);
        int hi = (int)Math.Ceiling(Math.Log10(max) - LogSnap);
        if (hi <= lo) hi = lo + 1;

        // Re-apply the span cap after rounding — flooring lo and ceiling hi can
        // each add a decade to a span that was already at the limit.
        lo = Math.Max(lo, hi - (int)LogFloorDecades);

        // Thin the decade grid on wide spans so the plot doesn't fill with lines.
        int decades = hi - lo;
        int decadeStep = decades <= MaxLogDecadeLines
            ? 1
            : (decades + MaxLogDecadeLines - 1) / MaxLogDecadeLines;

        var ticks = new List<double>();
        for (int k = lo; k <= hi; k += decadeStep)
            ticks.Add(Math.Pow(10, k));
        if (ticks[^1] < Math.Pow(10, hi))
            ticks.Add(Math.Pow(10, hi));

        return new SparkAxis(Math.Pow(10, lo), Math.Pow(10, hi), [.. ticks], isLog: true);
    }

    /// <summary>Rounds a raw interval up to the next 1 / 2 / 5 × 10^k.</summary>
    internal static double NiceStep(double raw)
    {
        if (!(raw > 0) || double.IsInfinity(raw)) return 1;

        double pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double mantissa = raw / pow;
        double nice = mantissa <= 1 ? 1
                    : mantissa <= 2 ? 2
                    : mantissa <= 5 ? 5
                    : 10;
        return nice * pow;
    }

    private static double[] Steps(double lo, double hi, double step)
    {
        int n = Math.Clamp((int)Math.Round((hi - lo) / step), 1, 24);
        var ticks = new double[n + 1];
        for (int i = 0; i <= n; i++)
            ticks[i] = lo + i * step;
        ticks[n] = hi;   // absorb the rounding drift accumulated across the loop
        return ticks;
    }

    /// <summary>
    /// Compact axis label. The corner labels sit inside a plot that can be as
    /// narrow as ~70 px, so this is deliberately shorter than the row's value
    /// format ("5e-5", not "5.0E-005"). InvariantCulture: a ru-RU operator
    /// station must not render a decimal comma in machine-data labels.
    /// </summary>
    public static string Format(double v)
    {
        if (v == 0) return "0";
        if (double.IsNaN(v) || double.IsInfinity(v)) return "—";

        double magnitude = Math.Abs(v);
        if (magnitude >= 1e5 || magnitude < 1e-2)
        {
            int exp = (int)Math.Floor(Math.Log10(magnitude));
            double mantissa = v / Math.Pow(10, exp);
            string m = Math.Abs(Math.Abs(mantissa) - 1) < 0.05
                ? (v < 0 ? "-1" : "1")
                : mantissa.ToString("0.#", CultureInfo.InvariantCulture);
            return m + "e" + exp.ToString(CultureInfo.InvariantCulture);
        }

        string fmt = magnitude >= 100 ? "0" : magnitude >= 10 ? "0.#" : "0.##";
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }
}
