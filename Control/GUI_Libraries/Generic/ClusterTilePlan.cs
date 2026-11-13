namespace APS.Controls.Generic;

public readonly record struct TilePlan(bool ShowBars, int MetricRows, float RowHeight)
{
    // 96-DPI row heights matched to ClusterProcessPanel.UiScale; callers pass DpiY / 96 as scale.
    public const int RowWithBar = 32;
    public const int RowNoBar = 22;
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
        float compressed = (float)availableHeight / metricCount;
        if (compressed >= floor)
            return new TilePlan(false, metricCount, compressed);
        int rows = System.Math.Clamp((int)(availableHeight / floor), 1, metricCount);
        return new TilePlan(false, rows, floor);
    }
}
