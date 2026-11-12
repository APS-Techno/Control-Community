using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.Logging;

namespace APS.SystemControl.Core;

public partial class Core
{
    private readonly object _manualLogLock = new();
    private ManualDataSession? _manualLog;
    private IReadOnlyCollection<string> _manualPsuIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private ManualLoggingSettings? _loggingProfile;
    private string? _loggingProfileDir;
    private int _manualIntervalMs;

    /// <summary>The profile's Config.xml logging options. Reading it also tells the recipe logger which sensor is the clock.</summary>
    public ManualLoggingSettings LoggingProfile
    {
        get
        {
            string? dir = ConfigDir;
            if (_loggingProfile is null || _loggingProfileDir != dir)
            {
                _loggingProfile = ManualLoggingSettings.Load(dir);
                _loggingProfileDir = dir;
                logConfig.ClockSensorId = _loggingProfile.ClockSensorId;
            }
            return _loggingProfile;
        }
    }

    /// <summary>Wall-clock spacing of measurement rows: the operator's choice, else the profile default.</summary>
    public int ManualLogIntervalMs
    {
        get => _manualIntervalMs > 0 ? _manualIntervalMs : LoggingProfile.IntervalMs;
        set
        {
            _manualIntervalMs = ManualLoggingSettings.ClampInterval(value);
            lock (_manualLogLock) _manualLog?.SetIntervalMs(_manualIntervalMs);
        }
    }

    public bool IsManualLogging => _manualLog?.IsRecording ?? false;

    public int ManualLogRows => _manualLog?.RowCount ?? 0;

    /// <summary>Every file of the latest measurement log, one per clock segment; empty before the first one.</summary>
    public IReadOnlyList<string> ManualLogFiles => _manualLog?.Files ?? [];

    public string? ManualLogPath => _manualLog?.LastFilePath;

    /// <summary>Starts a measurement log. On failure <paramref name="message"/> says why; on success it is the file path.</summary>
    public bool TryStartManualLogging(out string message)
    {
        lock (_manualLogLock)
        {
            if (_manualLog is { IsRecording: true })
            {
                message = $"already recording to {_manualLog.ActiveFilePath}";
                return false;
            }

            var profile = LoggingProfile;
            if (profile.ClockSensorId is { } clockId && FindClockSensor(clockId) is null)
            {
                message = $"clock sensor '{clockId}' named in Config.xml is missing or not connected";
                EventLogBus.Publish("ManualLog", $"cannot start: {message}", LogEventLevel.Operator);
                return false;
            }

            _manualPsuIds = ComputeSourcePsuIds();
            var columns = logConfig.BuildColumns(hwElements, _manualPsuIds, includeStates: true);
            columns["RecipeRunning"] = "1=running";

            _manualClockId = profile.ClockSensorId;
            _manualCollect ??= CollectManualRow;
            _manualClock = profile.ClockSensorId is null ? null : (_manualClockReader ??= ReadManualClock);
            _manualLog?.Dispose();
            _manualLog = new ManualDataSession(
                Path.Combine(AppPaths.DataDir, "Manual"),
                text => EventLogBus.Publish("ManualLog", text, LogEventLevel.Operator));

            int interval = ManualLogIntervalMs;
            if (!_manualLog.Start(columns, interval, profile.ClockSensorId))
            {
                message = "the measurement file could not be created";
                EventLogBus.Publish("ManualLog", $"cannot start: {message}", LogEventLevel.Operator);
                return false;
            }

            message = _manualLog.ActiveFilePath ?? "";
            string timebase = profile.ClockSensorId is { } id ? $"clock sensor {id} (seconds)" : "wall clock";
            EventLogBus.Publish("ManualLog",
                $"measurement log started: {message}; interval {interval} ms; timebase {timebase}", LogEventLevel.Operator);
            return true;
        }
    }

    /// <summary>Stops the measurement log and returns the path of its last file; null when none was running.</summary>
    public string? StopManualLogging()
    {
        lock (_manualLogLock) return _manualLog?.Stop();
    }

    private BaseSensor? FindClockSensor(string id)
    {
        if (!hwElements.Item.TryGetValue(DeviceType.Sensor, out var sensors)) return null;
        foreach (var kvp in sensors)
            if (string.Equals(kvp.Key, id, StringComparison.OrdinalIgnoreCase) && kvp.Value.Connected && kvp.Value is BaseSensor sensor)
                return sensor;
        return null;
    }

    private Func<Dictionary<string, double>>? _manualCollect;
    private Func<double?>? _manualClock;
    private Func<double?>? _manualClockReader;
    private string? _manualClockId;

    private Dictionary<string, double> CollectManualRow()
    {
        var values = logConfig.CollectValues(hwElements, _manualPsuIds, includeStates: true);
        values["RecipeRunning"] = IsRecipeRunning ? 1 : 0;
        return values;
    }

    private double? ReadManualClock() => FindClockSensor(_manualClockId!)?.LastValue;

    private void ManualLogTick()
    {
        var session = _manualLog;
        var collect = _manualCollect;
        if (session is null || collect is null || !session.IsRecording) return;
        session.Offer(collect, _manualClock);
    }
}
