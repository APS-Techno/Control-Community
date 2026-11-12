namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Left panel sections:   Pressure, MassFlow, Substrate (in that order)
/// Right panel sections:  Power
/// Centre column:         Process — only when the subclass opts in with a
///                        non-zero <c>CenterPanelWidth</c>; otherwise these
///                        rows fall back to the left panel after Substrate.
/// Anything else:         Other (renders in Substrate section as a catch-all)
/// </summary>
public enum ReadingGroup { MassFlow, Pressure, Power, Substrate, Other, Process }

/// <summary>
/// A second trace drawn on the same chart as its owning <see cref="ReadingSpec"/>,
/// with its own value axis (labelled on the right edge) and its own caption
/// value. Used for a DC supply's current beside its voltage: the two move
/// together and an operator reads them as one signal pair.
/// </summary>
/// <param name="Id">Stable id matching <c>ProcessReading.Id</c> for this trace.</param>
/// <param name="Accent">Trace and value colour; null falls back to the row's
/// primary accent, lightened.</param>
public sealed record ReadingTrace(
    string Id,
    string Format,
    string Units,
    Color? Accent = null
);

/// <param name="Id">Stable id matching <c>ProcessReading.Id</c>. Also the row
/// control's <c>Name</c>.</param>
/// <param name="Format">Numeric format string passed to <c>double.ToString</c>
/// (e.g. "F1", "E1", "F0").</param>
/// <param name="ShowSparkline">If false, the sparkline area renders as a dim
/// em-dash regardless of value.</param>
/// <param name="Scale">Vertical scaling of the row's mini trend chart. Use
/// <see cref="ReadingScale.Log"/> for readings that move by decades (chamber
/// and load-lock pressure) — on a linear axis everything below the peak decade
/// collapses onto the baseline.</param>
/// <param name="Accent">Signal-identity colour for the trace, its fill, the
/// value text and the chart chrome — see <see cref="ReadingPalette"/>. A mass
/// flow takes its gas's pipe colour so the trend matches the Vacuum tab's
/// piping. Null keeps the row on the default state colouring. The accent only
/// applies while the reading is healthy: Warning and Alarm still repaint the
/// row amber / red.</param>
/// <param name="Section">Optional sub-heading within the group's column. Rows
/// are emitted in declaration order and a dim sub-header is inserted each time
/// the section changes, so consecutive specs sharing a Section form one block
/// ("Throttle", "Bias").</param>
/// <param name="Device">Hardware device the row belongs to, for
/// <c>SetPowerSupplyVisibility</c> — a supply whose readings publish under
/// <c>&lt;id&gt;_V</c> / <c>_I</c> still hides by its plain id. Null means the
/// row id is the device id.</param>
public sealed record ReadingSpec(
    string Id,
    ReadingGroup Group,
    string DisplayLabel,
    string Format,
    string Units,
    bool ShowSparkline = true,
    ReadingScale Scale = ReadingScale.Linear,
    Color? Accent = null,
    string? Section = null,
    string? Device = null,
    ReadingTrace? Secondary = null
)
{
    public string DeviceId => Device ?? Id;
}
