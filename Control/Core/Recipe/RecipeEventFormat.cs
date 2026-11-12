namespace APS.SystemControl.Core.Recipe;

/// <summary>
/// Pure formatting helpers for recipe lifecycle event messages emitted to
/// the floating Events overlay (see EventLogBus / EventLogClassifier).
/// </summary>
public static class RecipeEventFormat
{
    /// <summary>
    /// Renders a recipe-elapsed duration in operator-friendly form:
    ///   ≥ 1h  → "1h 02m 05s"
    ///   ≥ 1m  → "12m 34s"
    ///   else  → "4.7s"
    /// Negative inputs are clamped to zero.
    /// </summary>
    public static string FormatDuration(TimeSpan d)
    {
        if (d.Ticks < 0) d = TimeSpan.Zero;

        if (d.TotalHours >= 1)
            return $"{(int)d.TotalHours}h {d.Minutes:D2}m {d.Seconds:D2}s";

        if (d.TotalMinutes >= 1)
            return $"{(int)d.TotalMinutes:D2}m {d.Seconds:D2}s";

        return d.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
    }

    /// <summary>
    /// Returns the first line of <paramref name="text"/> (up to the first
    /// CR or LF), trimmed, capped at 120 characters. Returns "" for null,
    /// empty, whitespace input, or when that first line is blank.
    /// Used to keep failure-reason fragments on a single overlay line.
    /// </summary>
    public static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        int nl = text.IndexOfAny(['\r', '\n']);
        string head = nl < 0 ? text : text[..nl];
        head = head.Trim();
        return head.Length > 120 ? head[..120] : head;
    }
}
