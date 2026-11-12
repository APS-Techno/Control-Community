namespace APS.SystemControl.GUIControls.General;

/// <summary>
/// All measurements are percent-of-parent. The column anchor is the min top over the
/// whole column, hidden panels included; hidden panels are omitted from the result so
/// the caller leaves them untouched.
/// </summary>
public static class ColumnCompactor
{
    public readonly record struct PanelBox(
        int Order, float Left, float TopPercent, float HeightPercent, bool Visible);

    /// <remarks><c>Order</c> must be unique across the entire <paramref name="boxes"/> list
    /// (callers pass a global registration index), not per-column — the returned map is keyed
    /// by <c>Order</c> across all columns.</remarks>
    public static IReadOnlyDictionary<int, float> Compute(
        IReadOnlyList<PanelBox> boxes, float gapPercent, float columnTolerance)
    {
        var result = new Dictionary<int, float>();
        if (columnTolerance <= 0f) columnTolerance = 1f;

        var columns = boxes.GroupBy(b => (int)MathF.Round(b.Left / columnTolerance));
        foreach (var col in columns)
        {
            float anchor = col.Min(b => b.TopPercent);
            float cursor = anchor;
            foreach (var b in col.Where(b => b.Visible).OrderBy(b => b.Order))
            {
                result[b.Order] = cursor;
                cursor += b.HeightPercent + gapPercent;
            }
        }
        return result;
    }
}
