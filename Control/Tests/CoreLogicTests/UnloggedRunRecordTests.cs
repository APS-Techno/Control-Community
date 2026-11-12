using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.Evidence;
using APS.SystemControl.Core.Logging;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// The run record must survive a run with recipe logging OFF, and the deposition snapshot must
/// name it. With logging off there is no TSV, yet the record holds every fault pause and
/// everything the operator hand-drove during a hold, while the <c>.depo.json</c> is written
/// regardless. <c>outcome.dataFile</c> names the TSV, so it cannot lead to the record when there
/// is no TSV; schema 2 names the record in <c>outcome.runRecord</c>.
/// </summary>
public class UnloggedRunRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aps-runrec", Guid.NewGuid().ToString("N"));
    private readonly string _prevRecipes = AppPaths.RecipesDir;
    private readonly string _prevLogs = AppPaths.LogsDir;
    private readonly string _prevData = AppPaths.DataDir;

    /// <summary>AppPaths is process-wide, so this points it at a temp tree and puts it back. Safe
    /// because the assembly runs non-parallel (see TestCollectionBehavior.cs).</summary>
    public UnloggedRunRecordTests() => SetDataDir(_root);

    public void Dispose()
    {
        AppPaths.Initialize(new Config(new AppConfig
        {
            ConfigDir = ".", Prefix = "T", WorkDir = ".",
            RecipesDir = _prevRecipes, LogsDir = _prevLogs, DataDir = _prevData,
        }));
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static void SetDataDir(string dataDir) => AppPaths.Initialize(new Config(new AppConfig
    {
        ConfigDir = ".", Prefix = "T", WorkDir = ".",
        RecipesDir = ".", LogsDir = ".", DataDir = dataDir,
    }));

    private static Core NewCore() => new CoreThread(new ChannelPool()).Core;

    /// <summary>The dated run folder + stem the record must use: the same pair the DataLogger
    /// would have given the TSV, so the record sits beside its <c>.depo.json</c> and its RunId
    /// keeps the shape run discovery parses.</summary>
    private string Expected(DateTime startedAt, string recipe)
    {
        string dir = DepositionRecordService.DatedDir(_root, startedAt);
        string stem = startedAt.ToString(DataLogger.StampFormat, System.Globalization.CultureInfo.InvariantCulture)
                    + "_" + recipe;
        return Path.Combine(dir, stem + RunRecord.Suffix);
    }

    [Fact]
    public void A_user_run_with_logging_off_still_records_the_pause_and_the_hand_driven_commands()
    {
        var core = NewCore();
        var startedAt = new DateTime(2026, 9, 4, 14, 3, 9, DateTimeKind.Local);
        var at = startedAt.AddMinutes(2).ToUniversalTime();

        core.BeginRunRecord("CoSputter", startedAt, embedded: false);
        core.WriteRunRecord(
            tsvPath: null,                                   // recipe logging OFF: no TSV at all
            recipeName: "CoSputter", recipePath: @"C:\Recipes\CoSputter.rcpx", embedded: false,
            startedAt, RunRecord.Complete, "(20m 00s)",
            pauses: [new PauseEvent(at, "deposit Si 600 Home", "operator de-energised RF301",
                "retry", "operator",
                ManualCommands: ["14:05:12 Power: RF301 SetPower", "14:05:14 Power: RF301 OutputOn"])],
            skippedSteps: []);

        string expected = Expected(startedAt, "CoSputter");
        Assert.True(File.Exists(expected), $"no run record at {expected}");

        // TryRead is addressed by a TSV path; the record shares its stem, so the TSV that was
        // never written still names it. That is the whole point of keeping the convention.
        var r = RunRecord.TryRead(Path.ChangeExtension(expected[..^RunRecord.Suffix.Length] + ".tsv", ".tsv"));
        Assert.NotNull(r);
        Assert.Equal("2026-09-04_14-03-09_CoSputter", r!.RunId);   // no trailing ".run"
        Assert.Equal("CoSputter", r.Recipe);
        Assert.False(r.Embedded);
        var pause = Assert.Single(r.Pauses);
        Assert.Equal("retry", pause.Decision);
        Assert.Equal(["14:05:12 Power: RF301 SetPower", "14:05:14 Power: RF301 OutputOn"],
            pause.ManualCommands);
    }

    /// <summary>An unlogged EMBEDDED run still writes nothing, deliberately: only a user recipe
    /// can pause, be held or be hand-driven, so there is nothing to lose — and SYSTEM_* / LOAD_*
    /// sequences run many times an hour and would otherwise litter the dated data folders.
    /// </summary>
    [Fact]
    public void An_unlogged_embedded_run_writes_nothing()
    {
        var core = NewCore();
        var startedAt = new DateTime(2026, 9, 4, 15, 0, 0, DateTimeKind.Local);

        core.BeginRunRecord("SYSTEM_PUMPDOWN", startedAt, embedded: true);
        core.WriteRunRecord(null, "SYSTEM_PUMPDOWN", null, embedded: true,
            startedAt, RunRecord.Complete, "(02m)");

        Assert.False(File.Exists(Expected(startedAt, "SYSTEM_PUMPDOWN")));
    }

    [Fact]
    public void A_logged_run_still_writes_beside_its_tsv()
    {
        var core = NewCore();
        string dir = Path.Combine(_root, "2026-09", "04");
        Directory.CreateDirectory(dir);
        string tsv = Path.Combine(dir, "2026-09-04_16-00-00_Logged.tsv");
        File.WriteAllText(tsv, "Time\n");

        core.BeginRunRecord("Logged", new DateTime(2026, 9, 4, 16, 0, 0), embedded: false);
        core.WriteRunRecord(tsv, "Logged", null, embedded: false,
            new DateTime(2026, 9, 4, 16, 0, 0), RunRecord.Complete, "(01m)");

        var r = RunRecord.TryRead(tsv);
        Assert.NotNull(r);
        Assert.Equal("2026-09-04_16-00-00_Logged", r!.RunId);
    }

    /// <summary>Schema 2: the snapshot names the record outright. Derived on both sides from the
    /// same inputs rather than remembered, because the snapshot is finalised on the UI thread off
    /// the terminal progress message while the record is written in the recipe thread's finally —
    /// the two are not ordered, and the reference must be right whichever lands first.</summary>
    [Fact]
    public void The_snapshot_names_the_run_record_of_a_run_with_logging_off()
    {
        var core = NewCore();
        var startedAt = DateTime.Now;

        core.BeginRunRecord("CoSputter", startedAt, embedded: false);
        string? snapshotPath = core.CreateDepositionSnapshot(new DepositionConditions
        {
            Specimen = "S1",
            Recipe = new DepositionRecipe { Name = "CoSputter", Content = "deposit Si 600 Home" },
        });
        Assert.NotNull(snapshotPath);

        core.WriteRunRecord(null, "CoSputter", null, embedded: false,
            startedAt, RunRecord.Complete, "(20m)");
        core.FinalizeDepositionSnapshot(snapshotPath!, new DepositionOutcome { Result = "completed" });

        var snap = DepositionSnapshotSerializer.FromJson(File.ReadAllText(snapshotPath!));
        Assert.Equal(2, snap.Schema);
        Assert.NotNull(snap.Outcome);
        Assert.Null(snap.Outcome!.DataFile);                       // logging was off
        Assert.Equal(Path.GetFileName(Expected(startedAt, "CoSputter")), snap.Outcome.RunRecord);

        // …and the file it names is really there, in the snapshot's own folder.
        Assert.True(File.Exists(Path.Combine(
            Path.GetDirectoryName(snapshotPath!)!, snap.Outcome.RunRecord!)));
    }
}
