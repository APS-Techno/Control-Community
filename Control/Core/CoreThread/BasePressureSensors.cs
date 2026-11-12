using System.Globalization;

namespace APS.SystemControl.Core;

internal static class BasePressureSensors
{
    internal sealed record Candidate(string Id, string Chamber, string Description);

    internal static bool IsPressureSensor(string id, string description)
    {
        string s = $"{id} {description}".ToLowerInvariant();
        return !(s.Contains("temp") || s.Contains("tmp") || s.Contains("thickness"));
    }

    /// <summary>One sensor per chamber, chambers in ordinal order: the first sensor named by the
    /// chamber's alarm rules (rule order), else the first pressure sensor by ordinal id.</summary>
    internal static IReadOnlyList<(string Chamber, string SensorId)> PickPrimaries(
        IEnumerable<Candidate> connected, Func<string, IReadOnlyList<string>> alarmSensorsOf)
    {
        var result = new List<(string, string)>();
        var byChamber = connected
            .Where(c => IsPressureSensor(c.Id, c.Description))
            .GroupBy(c => c.Chamber, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byChamber)
        {
            var ids = group.Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
            string? ruled = alarmSensorsOf(group.Key)
                .Select(a => ids.FirstOrDefault(i => string.Equals(i, a, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(i => i is not null);
            result.Add((group.Key, ruled ?? ids[0]));
        }
        return result;
    }

    internal static string FormatLine(string chamber, string sensorId, float torr) =>
        $"Base pressure ({chamber}, {sensorId}): {torr.ToString("E2", CultureInfo.InvariantCulture)} Torr";
}
