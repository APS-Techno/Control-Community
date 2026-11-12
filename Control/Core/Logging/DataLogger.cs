using System.Globalization;
using System.IO;

namespace APS.SystemControl.Core.Logging;

/// <summary>
/// Records time-series sensor data to tab-separated value (TSV) files.
/// Each recording session creates a new file named with the timestamp and recipe name.
/// Thread-safe: Start/LogRow/Stop/Dispose may be called from different threads.
/// </summary>
public class DataLogger : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateTime _startTime;
    private string[] _columnHeaders = [];
    private string[] _columnUnits = [];
    private readonly string _outputDirectory;

    /// <summary>
    /// Optional one-shot override of <see cref="_outputDirectory"/> consumed
    /// by the next <see cref="Start"/> call and cleared afterwards.
    /// Set by <c>Core.CreateDepositionSnapshot</c> so a single recipe run writes its
    /// TSV log into the dated snapshot folder alongside its recipe copy.
    /// </summary>
    public string? NextRunOutputDirectory { get; set; }

    /// <summary>
    /// Full path of the TSV opened by the most recent successful <see cref="Start"/>;
    /// null after a failed Start or <see cref="ClearLastFilePath"/>. Deliberately
    /// preserved across <see cref="Stop"/> — Core.FinalizeDepositionSnapshot reads it
    /// at run end (after Stop) to stamp the snapshot's Outcome.DataFile.
    /// </summary>
    public string? LastFilePath { get; private set; }

    /// <summary>
    /// Forget the previous run's TSV path. Called by Core.CreateDepositionSnapshot at
    /// run start so a suppressed run (recipe logging OFF — Start never called) cannot
    /// stamp the prior run's TSV into its own snapshot.
    /// </summary>
    internal void ClearLastFilePath()
    {
        lock (_lock) LastFilePath = null;
    }

    /// <summary>TSV-stem timestamp format; RunArtifacts.TryParseStem parses the same shape.</summary>
    public const string StampFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>Path of the TSV currently being written, or null when not recording. Unlike
    /// <see cref="LastFilePath"/> this is null once <see cref="Stop"/> has run.</summary>
    public string? ActiveFilePath
    {
        get { lock (_lock) return IsRecording ? LastFilePath : null; }
    }

    private int _rowCount;
    private const int FlushInterval = 50;
    private bool _externalClock;
    private double? _clockOrigin;

    /// <summary>Last accepted external-clock reading in seconds; null until a row is written or when the file uses the wall clock.</summary>
    public double? LastClockSeconds { get; private set; }

    /// <summary>Largest gap between two consecutive accepted external-clock rows, in clock seconds.</summary>
    public double MaxClockStepSeconds { get; private set; }

    public int RowCount { get { lock (_lock) return _rowCount; } }

    internal const string StatusOver = "OVER";
    internal const string StatusUnder = "UNDER";
    internal const string StatusInvalid = "INVALID";

    /// <summary>Columns written by an external-clock file that are not measurements.</summary>
    public static readonly IReadOnlySet<string> ClockColumns = new HashSet<string> { "Time", "Elapsed", "SimTime", "WallElapsed", "RecipeRunning" };

    /// <summary>Over-range, under-range and invalid readings keep a status label; a zero would read as a measurement.</summary>
    internal static string FormatValue(double v) =>
        double.IsNaN(v) ? StatusInvalid
        : double.IsPositiveInfinity(v) ? StatusOver
        : double.IsNegativeInfinity(v) ? StatusUnder
        : v.ToString("G4", CultureInfo.InvariantCulture);

    public DataLogger(string outputDirectory = "Data")
    {
        _outputDirectory = outputDirectory;
    }

    public bool IsRecording { get; private set; }

    /// <summary>
    /// Start recording. Creates a new TSV file with headers and units rows.
    /// </summary>
    /// <param name="recipeName">Name of the recipe being executed (used in filename).</param>
    /// <param name="columns">Dictionary of column header to unit pairs.</param>
    /// <param name="fileStem">Complete file name without extension; replaces the timestamp_recipe stem.</param>
    /// <param name="externalClock">Adds SimTime and WallElapsed columns and makes Elapsed follow the clock passed to <see cref="LogRow"/>.</param>
    public void Start(string recipeName, Dictionary<string, string> columns, string? fileStem = null, bool externalClock = false)
    {
        lock (_lock)
        {
            if (IsRecording)
                StopCore();

            try
            {
                // Honour CreateDepositionSnapshot's one-shot override so the TSV lands
                // in the dated specimen folder, then clear it for next run.
                string outDir = NextRunOutputDirectory ?? _outputDirectory;
                NextRunOutputDirectory = null;
                Directory.CreateDirectory(outDir);

                _startTime = DateTime.Now;
                _rowCount = 0;
                _externalClock = externalClock;
                _clockOrigin = null;
                LastClockSeconds = null;
                MaxClockStepSeconds = 0;

                string safeName = SanitizeFileName(recipeName);
                // Invariant: the stem is the agent-facing run id (RunArtifacts parses it with
                // InvariantCulture); a Buddhist- or Hijri-calendar thread culture would
                // otherwise stamp a different year.
                string timestamp = _startTime.ToString(StampFormat, System.Globalization.CultureInfo.InvariantCulture);
                string fileName = (fileStem ?? $"{timestamp}_{safeName}") + ".tsv";
                string filePath = Path.Combine(outDir, fileName);

                int fixedCols = externalClock ? 4 : 2;
                _columnHeaders = new string[columns.Count + fixedCols];
                _columnUnits = new string[columns.Count + fixedCols];
                _columnHeaders[0] = "Time";
                _columnHeaders[1] = "Elapsed";
                _columnUnits[0] = "HH:mm:ss"; // wall-clock timestamp, not seconds
                _columnUnits[1] = "s";
                if (externalClock)
                {
                    _columnHeaders[2] = "SimTime";
                    _columnUnits[2] = "s";
                    _columnHeaders[3] = "WallElapsed";
                    _columnUnits[3] = "s";
                }

                int idx = fixedCols;
                foreach (var kvp in columns)
                {
                    _columnHeaders[idx] = kvp.Key;
                    _columnUnits[idx] = kvp.Value;
                    idx++;
                }

                _writer = new StreamWriter(filePath, append: false, encoding: System.Text.Encoding.UTF8)
                {
                    AutoFlush = false
                };

                _writer.WriteLine(string.Join("\t", _columnHeaders));
                _writer.WriteLine(string.Join("\t", _columnUnits));
                _writer.Flush();

                LastFilePath = filePath;
                IsRecording = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or NotSupportedException or System.Security.SecurityException)
            {
                // Disable recording best-effort: a read-only / ACL-restricted data dir
                // (e.g. a Program Files install) raises UnauthorizedAccessException. (audit M-9)
                System.Diagnostics.Trace.TraceWarning($"DataLogger.Start disabled recording: {ex.Message}");
                _writer?.Dispose();
                _writer = null;
                LastFilePath = null;
                IsRecording = false;
            }
        }
    }

    /// <summary>
    /// Log one data row. Values are matched to columns by header name.
    /// Missing values are written as empty strings.
    /// </summary>
    /// <param name="values">Dictionary of column header to measured value.</param>
    /// <param name="clockSeconds">External clock reading in seconds; required for a file started with an external clock, ignored otherwise.</param>
    /// <returns>True when a row was written. False when not recording, the clock is missing or not finite, or the clock went backwards.</returns>
    public bool LogRow(Dictionary<string, double> values, double? clockSeconds = null)
    {
        lock (_lock)
        {
            if (!IsRecording || _writer == null)
                return false;

            try
            {
                DateTime now = DateTime.Now;
                double wallElapsed = (now - _startTime).TotalSeconds;
                int fixedCols = _externalClock ? 4 : 2;

                string[] row = new string[_columnHeaders.Length];
                row[0] = now.ToString("HH:mm:ss");

                if (_externalClock)
                {
                    if (clockSeconds is not double clock || !double.IsFinite(clock))
                        return false;
                    if (LastClockSeconds is double last)
                    {
                        if (clock < last) return false;
                        MaxClockStepSeconds = Math.Max(MaxClockStepSeconds, clock - last);
                    }
                    _clockOrigin ??= clock;
                    LastClockSeconds = clock;
                    row[1] = (clock - _clockOrigin.Value).ToString("G9", CultureInfo.InvariantCulture);
                    row[2] = clock.ToString("G9", CultureInfo.InvariantCulture);
                    row[3] = wallElapsed.ToString("F3", CultureInfo.InvariantCulture);
                }
                else
                {
                    row[1] = wallElapsed.ToString("F1", CultureInfo.InvariantCulture);
                }

                for (int i = fixedCols; i < _columnHeaders.Length; i++)
                {
                    if (values.TryGetValue(_columnHeaders[i], out double val))
                        // 4 significant figures ≈ 0.1% relative precision — self-scales
                        // across the full range (pressure 1.2E-07 … 760) and trims
                        // trailing zeros so no digit is wasted. Elapsed keeps F1 above.
                        row[i] = FormatValue(val);
                    else
                        row[i] = "";
                }

                _writer.WriteLine(string.Join("\t", row));
                _rowCount++;

                if (_rowCount % FlushInterval == 0)
                    _writer.Flush();
                return true;
            }
            catch (IOException)
            {
                StopCore();
                return false;
            }
        }
    }

    /// <summary>Pushes buffered rows to disk so a copy of the file made now holds every logged row.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            try { _writer?.Flush(); }
            catch (IOException) { StopCore(); }
        }
    }

    /// <summary>Stops recording and returns the path of the TSV that was being written, or
    /// null when nothing was recording (logging off for this run, or Start failed).</summary>
    public string? Stop()
    {
        lock (_lock)
        {
            string? closed = IsRecording ? LastFilePath : null;
            StopCore();
            // Clear any one-shot deposition-folder redirect that a suppressed run
            // (recipe logging OFF) set via CreateDepositionSnapshot but never consumed
            // in Start(), so it cannot misdirect a later logged run's TSV. Cleared here
            // (not in StopCore) because Start() calls StopCore() before consuming the
            // redirect — nulling it there would wipe a legitimate override.
            NextRunOutputDirectory = null;
            return closed;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal stop — must be called with <c>_lock</c> held.
    /// </summary>
    private void StopCore()
    {
        if (_writer != null)
        {
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch (IOException) { }
            _writer = null;
        }
        IsRecording = false;
    }

    /// <summary>The TSV stem form of a recipe name: invalid path chars and spaces become '_'.
    /// Shared with RunArtifacts so run-id ↔ recipe-name matching cannot drift from the writer.</summary>
    internal static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = name;
        foreach (char c in invalid)
            result = result.Replace(c, '_');
        result = result.Replace(' ', '_');
        return result;
    }
}
