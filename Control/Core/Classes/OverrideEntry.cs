namespace APS.SystemControl.Core.Classes;

internal sealed record OverrideEntry(
    string Reason,
    DateTime DisabledAt,
    string DisabledBy,
    bool Persist);
