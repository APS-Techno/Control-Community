namespace APS.SystemControl.Core.Production;

/// <summary>
/// Decides whether a target's format fits a magnetron's format for mount filtering.
/// Both formats are Sources.xml &lt;format&gt; ids (shape+size, e.g. "4\" round", "160mm").
/// </summary>
public static class TargetFormatMatch
{
    /// <summary>
    /// True when the target may be mounted on the magnetron by format.
    /// A magnetron with no format (null/whitespace) has nothing to filter on, so every
    /// target is compatible. Otherwise the formats must be equal (OrdinalIgnoreCase);
    /// a blank target format is treated as incompatible with a formatted magnetron.
    /// </summary>
    public static bool IsCompatible(string? magnetronFormat, string? targetFormat)
    {
        if (string.IsNullOrWhiteSpace(magnetronFormat)) return true;
        return string.Equals(magnetronFormat, targetFormat, StringComparison.OrdinalIgnoreCase);
    }
}
