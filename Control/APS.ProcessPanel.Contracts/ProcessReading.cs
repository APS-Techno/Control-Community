namespace APS.ProcessPanel.Contracts;

public enum ReadingState { Ok, Warning, Alarm, Off }

/// <summary>
/// A single live value from the hardware layer pushed to the process panel.
/// Display metadata (label, units, format) lives in the subclass ReadingSpec —
/// the wire record only carries identity, value, and state.
/// </summary>
/// <param name="Id">Stable identifier (e.g. "pm", "Ar1", "PSU-A"). Must match the
/// Id in the panel subclass's <c>DefineReadings()</c> list.</param>
/// <param name="Value">Current reading value, or <c>null</c> when not available /
/// the device is offline. Null is distinct from a valid zero value.</param>
public record ProcessReading(string Id, double? Value, ReadingState State);
