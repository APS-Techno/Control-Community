namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// The gas entries are a verbatim mirror of the pipe palette in
/// <c>VacuumControls/Vacuum/Schematic/VacuumSchematicCanvas.cs</c> (GasColors),
/// so an Ar trend on the Process tab is the same cyan as the Ar pipe on the
/// Vacuum tab and an N2 trend the same green as the N2 pipe. Keep the two
/// tables in sync — if a gas is added there, add it here.
///
/// The non-gas entries have no pipe equivalent; they group the remaining rows
/// by what they measure (vacuum, temperature, DC / RF power) so a column of
/// charts reads as a few related families instead of one wall of green.
/// </summary>
public static class ReadingPalette
{
    public static readonly Color Ar     = Color.FromArgb(0, 200, 255);
    public static readonly Color N2     = Color.FromArgb(0, 255, 100);
    public static readonly Color Vacuum = Color.FromArgb(255, 180, 50);
    public static readonly Color Mixed  = Color.FromArgb(200, 200, 255);
    public static readonly Color Vent   = Color.FromArgb(255, 255, 150);

    public static readonly Color Temperature = Color.FromArgb(255, 150, 90);
    /// <summary>DC and pulsed-DC supplies.</summary>
    public static readonly Color DcPower = Color.FromArgb(170, 150, 255);
    /// <summary>RF supplies.</summary>
    public static readonly Color RfPower = Color.FromArgb(255, 120, 190);
    /// <summary>Any other supply — matches the power label colour the
    /// chamber schematic already paints on a flange.</summary>
    public static readonly Color Power = Color.FromArgb(140, 220, 180);
    /// <summary>Current trace drawn beside a supply's voltage on the same
    /// chart — warm gold, so it separates from every voltage accent (violet,
    /// magenta, mint) without reading as the Warning amber.</summary>
    public static readonly Color Current = Color.FromArgb(255, 220, 120);
    /// <summary>Throttle valve position — a pale steel blue: mechanical,
    /// not a gas, not a pressure.</summary>
    public static readonly Color Position = Color.FromArgb(130, 190, 230);

    private static readonly Dictionary<string, Color> ByGas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ar"]       = Ar,
        ["Argon"]    = Ar,
        ["N2"]       = N2,
        ["Nitrogen"] = N2,
        ["vacuum"]   = Vacuum,
        ["mixed"]    = Mixed,
        ["vent"]     = Vent,
    };

    /// <summary>
    /// Accent for a gas name (as written in HWConf / the pipe definitions), or
    /// null when the name is unknown — a null accent leaves the row on the
    /// default state colouring rather than inventing a colour for it.
    /// </summary>
    public static Color? ForGas(string? gas)
        => gas != null && ByGas.TryGetValue(gas.Trim(), out var c) ? c : null;
}
