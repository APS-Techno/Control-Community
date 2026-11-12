namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure evaluator for the C3 "drives must be homed AND stationary AND at
/// step 0" recipe precondition.
/// </summary>
public static class HomingCheck
{
    /// <summary>Motor steps of slack allowed around the home coordinate.</summary>
    public const float DefaultPositionTolerance = 0.5f;

    public readonly record struct DriveSnapshot(string Id, bool IsHomed, bool IsMoving, float Position);

    public readonly record struct Offender(string Id, string Reason);

    /// <summary>
    /// Pairs the two samples by drive ID and reports any drive that is
    /// not-homed, moving, off-home, drifting, or present in only one sample.
    /// </summary>
    public static IReadOnlyList<Offender> FindOffenders(
        IReadOnlyList<DriveSnapshot> first,
        IReadOnlyList<DriveSnapshot> second,
        float positionTolerance = DefaultPositionTolerance)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var secondById = second.ToDictionary(s => s.Id, s => s);
        var offenders = new List<Offender>();

        foreach (var s1 in first)
        {
            if (!secondById.TryGetValue(s1.Id, out var s2))
            {
                offenders.Add(new Offender(s1.Id, "missing from second sample"));
                continue;
            }

            if (!s1.IsHomed || !s2.IsHomed)
                offenders.Add(new Offender(s1.Id, "not homed"));
            else if (s1.IsMoving || s2.IsMoving)
                offenders.Add(new Offender(s1.Id, "still moving"));
            else if (Math.Abs(s1.Position) > positionTolerance || Math.Abs(s2.Position) > positionTolerance)
                offenders.Add(new Offender(s1.Id, $"at step {s2.Position:0.##} (not at home)"));
            else if (Math.Abs(s1.Position - s2.Position) > positionTolerance)
                offenders.Add(new Offender(s1.Id, $"position drifting ({s1.Position:0.##} -> {s2.Position:0.##})"));
        }

        var firstIds = first.Select(s => s.Id).ToHashSet();
        foreach (var s2 in second)
            if (!firstIds.Contains(s2.Id))
                offenders.Add(new Offender(s2.Id, "missing from first sample"));

        return offenders;
    }

    public static string FormatError(IReadOnlyList<Offender> offenders)
    {
        ArgumentNullException.ThrowIfNull(offenders);
        if (offenders.Count == 0) return string.Empty;
        return "Cannot start recipe: "
            + string.Join("; ", offenders.Select(o => $"'{o.Id}' {o.Reason}"))
            + ". Home all drives manually and wait for motion to stop.";
    }
}
