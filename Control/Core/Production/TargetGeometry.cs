using System.Text.RegularExpressions;

namespace APS.SystemControl.Core.Production;

public static class TargetGeometry
{
    private const double DefaultRadiusMm = 50.8; // 4" diameter

    /// <summary>Best-effort target radius (mm) from a free-form format like 4" round / 4in-round.
    /// Reads the leading inch *diameter*; falls back to a 4" default.</summary>
    public static double RadiusFromFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return DefaultRadiusMm;
        var m = Regex.Match(format, @"(\d+(?:\.\d+)?)\s*(?:""|in|inch)", RegexOptions.IgnoreCase);
        if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var dIn))
            return dIn * 25.4 / 2.0;
        return DefaultRadiusMm;
    }
}
