using APS.ProcessPanel.Contracts;

namespace APS.SystemControl.Core.Cluster;

/// <summary>
/// Pure mapping from resolved per-chamber lookups to a <see cref="ClusterSnapshot"/>.
/// No live Core access — FrmMain supplies the lookups on the UI thread. Chambers
/// without a face angle (e.g. TM itself, the centre hex) are excluded from the
/// radial tiles; the orphan "Main" fallback is skipped.
/// </summary>
internal static class ClusterSnapshotBuilder
{
    public static ClusterSnapshot Build(
        IReadOnlyCollection<string> chamberIds,
        string? mainFallbackToSkip,
        Func<string, SystemStateCode> stateOf,
        Func<string, double?> pressureOf,
        Func<string, bool> slitOpenOf,
        Func<string, int?> faceAngleOf,
        ClusterRobotInfo robot,
        Func<string, double?>? temperatureOf = null,
        Func<string, ClusterReadingInfo[]>? flowsOf = null,
        Func<string, ClusterReadingInfo[]>? powersOf = null,
        Func<string, ClusterValveInfo[]>? valvesOf = null,
        Func<string, double?>? drivePositionOf = null,
        Func<string, (bool turbo, bool scroll)>? pumpsOf = null,
        Func<string, ClusterSourceInfo[]>? sourcesOf = null)
    {
        var chambers = new List<ClusterChamberInfo>();
        foreach (var id in chamberIds)
        {
            if (mainFallbackToSkip != null &&
                string.Equals(id, mainFallbackToSkip, StringComparison.OrdinalIgnoreCase))
                continue;

            int? angle = faceAngleOf(id);
            if (angle is null) continue;

            var (turbo, scroll) = pumpsOf?.Invoke(id) ?? (false, false);

            chambers.Add(new ClusterChamberInfo(
                Key: id,
                FaceAngle: angle.Value,
                State: stateOf(id),
                Pressure: pressureOf(id),
                SlitOpen: slitOpenOf(id),
                Temperature: temperatureOf?.Invoke(id),
                Flows: flowsOf?.Invoke(id) ?? System.Array.Empty<ClusterReadingInfo>(),
                Powers: powersOf?.Invoke(id) ?? System.Array.Empty<ClusterReadingInfo>(),
                Valves: valvesOf?.Invoke(id) ?? System.Array.Empty<ClusterValveInfo>(),
                DrivePosition: drivePositionOf?.Invoke(id),
                TurboOn: turbo,
                ScrollOn: scroll,
                Sources: sourcesOf?.Invoke(id) ?? System.Array.Empty<ClusterSourceInfo>()));
        }
        return new ClusterSnapshot(chambers.ToArray(), robot);
    }
}
