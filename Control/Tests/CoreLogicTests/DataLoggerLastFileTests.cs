using APS.SystemControl.Core.Logging;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Verifies <see cref="DataLogger.LastFilePath"/>: set when Start() opens a TSV,
/// null when Start() fails, preserved across Stop() (finalize runs after recipe
/// end), and cleared by ClearLastFilePath() (Core's stale-run guard for
/// suppressed logging).
/// </summary>
public class DataLoggerLastFileTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "aps-lastfile-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly Dictionary<string, string> Columns = new() { ["pm"] = "mTorr" };

    [Fact]
    public void LastFilePath_set_by_successful_Start_and_survives_Stop()
    {
        string dir = Path.Combine(_root, "Data");
        using var logger = new DataLogger(dir);

        Assert.Null(logger.LastFilePath);

        logger.Start("W_dep", Columns);
        string? afterStart = logger.LastFilePath;
        logger.Stop();

        Assert.NotNull(afterStart);
        Assert.Equal(Directory.GetFiles(dir, "*.tsv").Single(), afterStart);
        Assert.Equal(afterStart, logger.LastFilePath); // preserved across Stop()
    }

    [Fact]
    public void Stop_returns_the_tsv_it_closed_and_null_when_not_recording()
    {
        string dir = Path.Combine(_root, "Data");
        using var logger = new DataLogger(dir);

        Assert.Null(logger.ActiveFilePath);
        Assert.Null(logger.Stop());

        logger.Start("W_dep", Columns);
        string? active = logger.ActiveFilePath;
        Assert.Equal(logger.LastFilePath, active);

        Assert.Equal(active, logger.Stop());
        Assert.Null(logger.ActiveFilePath);   // the run has ended
        Assert.Equal(active, logger.LastFilePath);
        Assert.Null(logger.Stop());           // a second Stop closed nothing
    }

    [Fact]
    public void Tsv_stem_timestamp_is_invariant_under_a_non_gregorian_culture()
    {
        // th-TH's default calendar is Buddhist (year + 543). The stem is the agent-facing run
        // id and is parsed with InvariantCulture, so the writer must use it too.
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            string dir = Path.Combine(_root, "Data");
            using var logger = new DataLogger(dir);
            logger.Start("W_dep", Columns);
            string stem = Path.GetFileNameWithoutExtension(logger.LastFilePath!);
            logger.Stop();

            Assert.StartsWith(DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), stem);
            Assert.True(DateTime.TryParseExact(stem[..DataLogger.StampFormat.Length], DataLogger.StampFormat,
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void LastFilePath_null_after_failed_Start()
    {
        // Force Start() down its IOException catch: the output "directory" is an
        // existing FILE, so Directory.CreateDirectory throws IOException.
        Directory.CreateDirectory(_root);
        string blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");

        using var logger = new DataLogger(blocked);
        logger.Start("W_dep", Columns);

        Assert.False(logger.IsRecording);
        Assert.Null(logger.LastFilePath);
    }

    [Fact]
    public void Failed_Start_clears_a_previous_runs_path()
    {
        string goodDir = Path.Combine(_root, "Data");
        using var logger = new DataLogger(goodDir);
        logger.Start("run1", Columns);
        logger.Stop();
        Assert.NotNull(logger.LastFilePath);

        // Second run redirected into a "folder" that is actually a file → Start fails.
        Directory.CreateDirectory(_root);
        string blocked = Path.Combine(_root, "blocked2");
        File.WriteAllText(blocked, "not a directory");
        logger.NextRunOutputDirectory = blocked;
        logger.Start("run2", Columns);

        Assert.Null(logger.LastFilePath); // must not report run1's TSV for run2
    }

    [Fact]
    public void ClearLastFilePath_nulls_it()
    {
        string dir = Path.Combine(_root, "Data");
        using var logger = new DataLogger(dir);
        logger.Start("W_dep", Columns);
        logger.Stop();
        Assert.NotNull(logger.LastFilePath);

        logger.ClearLastFilePath();

        Assert.Null(logger.LastFilePath);
    }
}
