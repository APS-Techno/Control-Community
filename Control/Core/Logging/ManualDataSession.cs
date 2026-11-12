using System.Globalization;
using System.IO;

namespace APS.SystemControl.Core.Logging;

/// <summary>
/// An operator-started measurement log, independent of recipe runs. It owns its own
/// <see cref="DataLogger"/>, so a run's <c>LastFilePath</c>, outcome data file and accumulated means can
/// never see it. Files are named <c>Manual_&lt;stamp&gt;[_partN].tsv</c> under the output directory,
/// a shape run discovery does not parse. Rows are paced by wall-clock interval; with an external clock
/// the Elapsed/SimTime columns carry that clock and a clock that goes backwards opens a new part file.
/// </summary>
public sealed class ManualDataSession : IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Func<long> _nowMs;
    private readonly Action<string>? _notify;
    private readonly DataLogger _logger;
    private readonly List<string> _files = [];

    private Dictionary<string, string> _columns = [];
    private bool _active;
    private bool _externalClock;
    private string _clockName = "";
    private string _stamp = "";
    private int _part;
    private int _intervalMs;
    private long _lastWriteMs;
    private bool _wroteAny;
    private int _rows;
    private int _rowsWithoutClock;
    private double _maxStep;

    public ManualDataSession(string directory, Action<string>? notify = null, Func<long>? nowMs = null)
    {
        _directory = directory;
        _notify = notify;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _logger = new DataLogger(directory);
    }

    public bool IsRecording { get { lock (_gate) return _active; } }

    public int IntervalMs { get { lock (_gate) return _intervalMs; } }

    public string? ActiveFilePath { get { lock (_gate) return _active ? _logger.LastFilePath : null; } }

    public string? LastFilePath { get { lock (_gate) return _files.Count > 0 ? _files[^1] : null; } }

    /// <summary>Every file of the most recent session in order, one per clock segment.</summary>
    public IReadOnlyList<string> Files { get { lock (_gate) return [.. _files]; } }

    public int RowCount { get { lock (_gate) return _rows; } }

    public int RowsWithoutClock { get { lock (_gate) return _rowsWithoutClock; } }

    public void SetIntervalMs(int intervalMs)
    {
        lock (_gate) _intervalMs = ManualLoggingSettings.ClampInterval(intervalMs);
    }

    /// <summary>Largest gap between consecutive rows in clock seconds over the session; 0 without an external clock.</summary>
    public double MaxClockStepSeconds { get { lock (_gate) return Math.Max(_maxStep, _logger.MaxClockStepSeconds); } }

    public bool Start(Dictionary<string, string> columns, int intervalMs, string? clockName = null)
    {
        lock (_gate)
        {
            if (_active) return false;
            _columns = new Dictionary<string, string>(columns);
            _externalClock = clockName is not null;
            _clockName = clockName ?? "";
            _intervalMs = ManualLoggingSettings.ClampInterval(intervalMs);
            _stamp = DateTime.Now.ToString(DataLogger.StampFormat, CultureInfo.InvariantCulture);
            _part = 1;
            _wroteAny = false;
            _rows = 0;
            _rowsWithoutClock = 0;
            _maxStep = 0;
            _files.Clear();
            if (!OpenPart()) return false;
            _active = true;
            return true;
        }
    }

    /// <summary>Writes a row if the wall-clock interval has elapsed. <paramref name="collect"/> runs only for a row that is due.</summary>
    public bool Offer(Func<Dictionary<string, double>> collect, Func<double?>? clock = null)
    {
        lock (_gate)
        {
            if (!_active) return false;
            if (!_logger.IsRecording)
            {
                _active = false;
                _notify?.Invoke("measurement logging stopped: the file could not be written");
                return false;
            }

            long now = _nowMs();
            if (_wroteAny && now - _lastWriteMs < _intervalMs) return false;

            double? seconds = null;
            if (_externalClock)
            {
                seconds = clock?.Invoke();
                if (seconds is not double s || !double.IsFinite(s))
                {
                    _rowsWithoutClock++;
                    return false;
                }
                if (_logger.LastClockSeconds is double last && s < last)
                {
                    _notify?.Invoke($"clock '{_clockName}' restarted ({last.ToString("G9", CultureInfo.InvariantCulture)} s -> {s.ToString("G9", CultureInfo.InvariantCulture)} s); continuing in part {_part + 1}");
                    _maxStep = Math.Max(_maxStep, _logger.MaxClockStepSeconds);
                    _logger.Stop();
                    _part++;
                    if (!OpenPart())
                    {
                        _active = false;
                        _notify?.Invoke("measurement logging stopped: the next part could not be opened");
                        return false;
                    }
                }
            }

            var values = collect();
            foreach (string column in _columns.Keys)
                if (!values.ContainsKey(column)) values[column] = double.NaN;

            if (!_logger.LogRow(values, seconds)) return false;
            _logger.Flush();
            _lastWriteMs = now;
            _wroteAny = true;
            _rows++;
            return true;
        }
    }

    /// <summary>Closes the log and returns the path of its last file, or null when nothing was recording.</summary>
    public string? Stop()
    {
        lock (_gate)
        {
            if (!_active) return null;
            _active = false;
            _maxStep = Math.Max(_maxStep, _logger.MaxClockStepSeconds);
            _logger.Stop();
            string? last = _files.Count > 0 ? _files[^1] : null;
            string clock = _externalClock
                ? $", clock {_clockName}, largest step {_maxStep.ToString("G4", CultureInfo.InvariantCulture)} s, {_rowsWithoutClock} poll cycles without a clock reading"
                : ", wall clock";
            _notify?.Invoke($"measurement log closed: {_rows} rows in {_files.Count} file(s), interval {_intervalMs} ms{clock}; {last}");
            return last;
        }
    }

    public void Dispose()
    {
        Stop();
        _logger.Dispose();
    }

    private bool OpenPart()
    {
        string stem = _part == 1 ? $"Manual_{_stamp}" : $"Manual_{_stamp}_part{_part}";
        string unique = stem;
        for (int n = 2; File.Exists(Path.Combine(_directory, unique + ".tsv")); n++)
            unique = $"{stem}_{n}";
        _logger.Start("Manual", _columns, unique, _externalClock);
        if (!_logger.IsRecording || _logger.LastFilePath is null) return false;
        _files.Add(_logger.LastFilePath);
        return true;
    }
}
