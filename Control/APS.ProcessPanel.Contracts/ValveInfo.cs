namespace APS.ProcessPanel.Contracts;

/// <summary>
/// Snapshot of a single valve's state for the Process panel.
/// Two-state only (open/closed); Undefined renders as closed.
/// </summary>
public record ValveInfo(string Id, bool IsOpen);
