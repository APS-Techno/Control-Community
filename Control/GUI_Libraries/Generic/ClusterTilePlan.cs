namespace APS.Controls.Generic;

/// <summary>
/// Pure decision of how much a rich chamber tile renders, given the pixels
/// available below its header and how many non-null metrics it has. Drives the
/// graceful degradation: bars+rows → rows-only → compressed rows → fewer rows
/// (never below one).
/// </summary>
public readonly record struct TilePlan(bool ShowBars, int MetricRows, float RowHeight)
{
    // Base row heights in pixels at 96 DPI, matched to ClusterProcessPanel.UiScale
    // (1.4x): keep these in step with it. Callers pass the monitor's DPI factor
    // (DpiY / 96) as <c>scale</c> so the thresholds track the point-size fonts.
    public const int RowWithBar = 32;
    public const int RowNoBar = 22;
    /// <summary>Readable floor for compressed rows — one ~12 pt text line with no
    /// padding. Below this, dropping rows beats overlapping them.</summary>
    public const int RowMin = 16;

    public static TilePlan For(int availableHeight, int metricCount, float scale = 1f)
    {
        if (scale <= 0f) scale = 1f;
        float withBar = RowWithBar * scale, noBar = RowNoBar * scale, floor = RowMin * scale;
        if (metricCount <= 0) return new TilePlan(false, 0, noBar);
        if (availableHeight >= metricCount * withBar)
            return new TilePlan(true, metricCount, withBar);
        if (availableHeight >= metricCount * noBar)
            return new TilePlan(false, metricCount, noBar);
        // Compress rows toward the readable floor before dropping any: a dense
        // tile (dual-mag: pm + two sources + tmp) keeps all its data visible at
        // the cost of tighter packing.
        float compressed = (float)availableHeight / metricCount;
        if (compressed >= floor)
            return new TilePlan(false, metricCount, compressed);
        int rows = System.Math.Clamp((int)(availableHeight / floor), 1, metricCount);
        return new TilePlan(false, rows, floor);
    }
}
