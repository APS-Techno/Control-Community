using APS.SystemControl.Hosting;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Maps a power-supply identifier (the <c>powerSupplyId</c> string from
/// <c>Sources.xml</c>) to a coarse power-type label suitable for display on
/// the chamber schematic. Returns null for empty/unknown supplies so the
/// caller can omit the type prefix and show just the ID, or render nothing
/// at all when both are absent.
/// </summary>
public static class PowerSupplyClassifier
{
    public static string? Classify(string? id) =>
        Classify(id, ControlApp.Edition.Find<IPowerSupplyTypeSource>());

    internal static string? Classify(string? id, IPowerSupplyTypeSource? extension)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        if (extension?.Classify(id) is { } label) return label;

        if (id.StartsWith("ION", StringComparison.OrdinalIgnoreCase)) return "Ion source";
        if (id.StartsWith("RF",  StringComparison.OrdinalIgnoreCase)) return "RF";
        if (id.StartsWith("DC",  StringComparison.OrdinalIgnoreCase)) return "DC";

        return null;
    }
}
