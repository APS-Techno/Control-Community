using System.Globalization;
using System.Xml.Linq;

namespace APS.SystemControl.Core.Logging;

/// <summary>
/// Optional <c>&lt;ManualLogging clockSensor="simtime" intervalMs="500"/&gt;</c> child of the
/// profile's Config.xml root. <c>clockSensor</c> names a sensor whose value is a clock in seconds;
/// it is the only way a measurement log leaves the wall clock. <c>intervalMs</c> is the default
/// wall-clock spacing of measurement rows.
/// </summary>
public sealed record ManualLoggingSettings(string? ClockSensorId, int IntervalMs)
{
    public const int DefaultIntervalMs = 1000;
    public const int MinIntervalMs = 100;
    public const int MaxIntervalMs = 60000;

    public static ManualLoggingSettings Default { get; } = new(null, DefaultIntervalMs);

    public static int ClampInterval(int ms) => Math.Clamp(ms, MinIntervalMs, MaxIntervalMs);

    public static ManualLoggingSettings Load(string? configDir)
    {
        if (string.IsNullOrEmpty(configDir)) return Default;
        string path = Path.Combine(configDir, "Config.xml");
        try
        {
            if (!File.Exists(path)) return Default;
            var element = XDocument.Load(path).Root?.Element("ManualLogging");
            if (element is null) return Default;

            string? clock = element.Attribute("clockSensor")?.Value.Trim();
            int interval = int.TryParse(element.Attribute("intervalMs")?.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int ms) ? ClampInterval(ms) : DefaultIntervalMs;
            return new ManualLoggingSettings(string.IsNullOrEmpty(clock) ? null : clock, interval);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            System.Diagnostics.Trace.TraceWarning($"ManualLoggingSettings: {path}: {ex.Message}");
            return Default;
        }
    }
}
