using System.Threading.Channels;
using APS.SystemControl.Core.Logging;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// <see cref="DataLogger.NextRunOutputDirectory"/> is the hook Core.StoreEvidence uses to
/// re-route a recipe's TSV log into the dated specimen folder alongside the recipe copy.
/// </summary>
public class StoreEvidenceTests
{
    [Fact]
    public void NextRunOutputDirectory_redirects_one_recording_then_clears()
    {
        string root = Path.Combine(Path.GetTempPath(), "aps-evidence-tests", Guid.NewGuid().ToString("N"));
        string baseDir    = Path.Combine(root, "Data");
        string evidenceDir = Path.Combine(root, "Data", "2026-05", "07");

        try
        {
            using var logger = new DataLogger(baseDir);
            logger.NextRunOutputDirectory = evidenceDir;

            var columns = new Dictionary<string, string> { ["pm"] = "mTorr" };
            logger.Start("test_recipe", columns);
            logger.LogRow(new Dictionary<string, double> { ["pm"] = 0.001 });
            logger.Stop();

            var inEvidence = Directory.Exists(evidenceDir)
                ? Directory.GetFiles(evidenceDir, "*.tsv")
                : Array.Empty<string>();
            Assert.Single(inEvidence);

            Assert.Null(logger.NextRunOutputDirectory);

            logger.Start("test_recipe_2", columns);
            logger.Stop();

            var inBase = Directory.GetFiles(baseDir, "*.tsv");
            Assert.Single(inBase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// When a run is suppressed (recipe logging OFF) the redirect set by
    /// CreateDepositionSnapshot is never consumed by Start(). Stop() must still clear it
    /// so it cannot misdirect a later logged run's TSV into the stale specimen folder.
    /// </summary>
    [Fact]
    public void Stop_clears_an_unconsumed_redirect_so_next_run_uses_base()
    {
        string root = Path.Combine(Path.GetTempPath(), "aps-evidence-tests", Guid.NewGuid().ToString("N"));
        string baseDir     = Path.Combine(root, "Data");
        string evidenceDir = Path.Combine(root, "Data", "2026-05", "07");

        try
        {
            using var logger = new DataLogger(baseDir);

            logger.NextRunOutputDirectory = evidenceDir;
            logger.Stop();

            Assert.Null(logger.NextRunOutputDirectory);

            var columns = new Dictionary<string, string> { ["pm"] = "mTorr" };
            logger.Start("later_run", columns);
            logger.Stop();

            Assert.False(Directory.Exists(evidenceDir),
                "unconsumed redirect leaked: later run's TSV was written to the stale specimen folder");
            var inBase = Directory.GetFiles(baseDir, "*.tsv");
            Assert.Single(inBase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
