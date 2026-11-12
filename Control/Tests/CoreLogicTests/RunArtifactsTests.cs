using System.Globalization;
using System.Text;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Post-run artifact discovery for the Agent bridge (runs.list / runs.file). A run is
/// identified by its DataLogger TSV stem; the process log, the as-run recipe and the
/// signed snapshot are located around it. Everything here is pure file I/O on a temp tree.
/// </summary>
public sealed class RunArtifactsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"runart_{Guid.NewGuid():N}");
    private string Data => Path.Combine(_root, "Data");
    private string Logs => Path.Combine(_root, "Logs");
    private string Recipes => Path.Combine(_root, "Recipes");

    private const string SmokeTsv = "2026-09-03_10-51-51_SMOKE-Si-deposition.tsv";
    private const string ProbeTsv = "2026-09-03_11-31-53_ARM-A-probe.tsv";

    private const string ProcessLog = "Process_2026-09-03_102141.log";
    private const string ProcessLogText =
        "--- Process log started 2026-09-03 10:21:41 ---\n" +
        "------ 2026-09-03  Thursday ------\n" +
        "10:21:41.503  [Core]  Hardware configuration loaded\n" +
        "10:23:26.574  [Recipe]  Recipe started: SMOKE-Si-deposition\n" +
        "10:23:26.713  [Recipe]  Recipe failed: SMOKE-Si-deposition (0.1s) — Cannot start recipe: 'Shutter' not homed\n" +
        "10:51:51.266  [Recipe]  Recipe started: SMOKE-Si-deposition\n" +
        "10:52:00.000  [Execute]  deposit Si 60\n" +
        "10:53:32.499  [Execute]  Recipe complete\n" +
        "10:53:32.502  [Recipe]  Recipe complete: SMOKE-Si-deposition (01m 41s)\n" +
        "11:31:53.091  [Recipe]  Recipe started: ARM-A-probe\n" +
        "11:31:59.998  [Execute]  Recipe complete\n" +
        "11:32:00.000  [Recipe]  Recipe complete: ARM-A-probe (6.9s)\n";

    public RunArtifactsTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Path.Combine(Data, "2026-09", "03"));
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Recipes);

        // Agent-started run: TSV in the Data root, no snapshot, recipe present in Recipes\.
        File.WriteAllText(Path.Combine(Data, SmokeTsv), "Time\tElapsed\tS_pm\nHH:mm:ss\ts\tTorr\n10:51:51\t0.0\t1e-6\n");
        File.WriteAllText(Path.Combine(Recipes, "SMOKE-Si-deposition.rcpx"), "<Recipe/>");

        // Operator-started run: TSV + signed snapshot in the dated folder, recipe not on disk.
        string dated = Path.Combine(Data, "2026-09", "03");
        File.WriteAllText(Path.Combine(dated, ProbeTsv), "Time\tElapsed\nHH:mm:ss\ts\n11:31:53\t0.0\n");
        File.WriteAllText(Path.Combine(dated, "Deposition_2026-09-03_11-31-53.depo.json"),
            """{"schema":1,"kind":"deposition-snapshot","conditions":{},"outcome":{"result":"completed","dataFile":"2026-09-03_11-31-53_ARM-A-probe.tsv"}}""");
        // A decoy snapshot from another run in the same folder must not be matched.
        File.WriteAllText(Path.Combine(dated, "Deposition_2026-09-03_09-00-00.depo.json"),
            """{"schema":1,"kind":"deposition-snapshot","conditions":{},"outcome":{"result":"aborted","dataFile":"2026-09-03_09-00-00_Other.tsv"}}""");

        File.WriteAllText(Path.Combine(Logs, ProcessLog), ProcessLogText);
        // An older Control launch whose log must not be consulted for these runs.
        File.WriteAllText(Path.Combine(Logs, "Process_2026-09-02_095213.log"),
            "--- Process log started 2026-09-02 09:52:13 ---\n------ 2026-09-02  Wednesday ------\n09:52:13.000  [Core]  boot\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private RunArtifacts Sut() => new(Data, Logs, Recipes);

    [Fact]
    public void List_returns_runs_newest_first_identified_by_tsv_stem()
    {
        var runs = Sut().List(count: 10, liveTsvPath: null);

        Assert.Equal(2, runs.Count);
        Assert.Equal("2026-09-03_11-31-53_ARM-A-probe", runs[0].RunId);
        Assert.Equal("ARM-A-probe", runs[0].Recipe);
        Assert.Equal(new DateTime(2026, 9, 3, 11, 31, 53), runs[0].StartedAt);
        Assert.Equal("2026-09-03_10-51-51_SMOKE-Si-deposition", runs[1].RunId);
        Assert.Equal("SMOKE-Si-deposition", runs[1].Recipe);
    }

    [Fact]
    public void List_honours_count()
    {
        var runs = Sut().List(count: 1, liveTsvPath: null);
        Assert.Single(runs);
        Assert.Equal("2026-09-03_11-31-53_ARM-A-probe", runs[0].RunId);
    }

    [Fact]
    public void List_takes_outcome_and_end_time_from_the_process_log_window()
    {
        var runs = Sut().List(10, null);
        var smoke = runs.Single(r => r.Recipe == "SMOKE-Si-deposition");

        Assert.Equal("complete", smoke.Outcome);
        Assert.Equal(new DateTime(2026, 9, 3, 10, 53, 32, 502), smoke.EndedAt);
        Assert.Equal("(01m 41s)", smoke.Message);
    }

    [Fact]
    public void List_reports_which_artifacts_exist_per_run()
    {
        var runs = Sut().List(10, null);
        var smoke = runs.Single(r => r.Recipe == "SMOKE-Si-deposition");
        var probe = runs.Single(r => r.Recipe == "ARM-A-probe");

        Assert.Equal(["tsv", "log", "recipe"], smoke.Artifacts.Select(a => a.Kind).ToArray());
        Assert.Equal(["tsv", "log", "snapshot"], probe.Artifacts.Select(a => a.Kind).ToArray());
        Assert.Equal(SmokeTsv, smoke.Artifacts.Single(a => a.Kind == "tsv").FileName);
        Assert.Equal("SMOKE-Si-deposition.rcpx", smoke.Artifacts.Single(a => a.Kind == "recipe").FileName);
        Assert.Equal("Deposition_2026-09-03_11-31-53.depo.json", probe.Artifacts.Single(a => a.Kind == "snapshot").FileName);
        Assert.All(smoke.Artifacts, a => Assert.True(a.Bytes > 0));
    }

    [Fact]
    public void Read_log_returns_only_the_run_window_with_a_source_header()
    {
        var chunk = Sut().Read("2026-09-03_10-51-51_SMOKE-Si-deposition", "log", offset: 0, maxBytes: 1 << 20);
        string text = Encoding.UTF8.GetString(chunk.Bytes);

        Assert.StartsWith("--- " + ProcessLog, text);
        Assert.Contains("10:51:51.266  [Recipe]  Recipe started: SMOKE-Si-deposition", text);
        Assert.Contains("10:52:00.000  [Execute]  deposit Si 60", text);
        Assert.Contains("10:53:32.502  [Recipe]  Recipe complete: SMOKE-Si-deposition (01m 41s)", text);
        Assert.DoesNotContain("10:23:26", text);            // the earlier failed attempt
        Assert.DoesNotContain("ARM-A-probe", text);          // the next run
        Assert.True(chunk.Eof);
        Assert.Equal("2026-09-03_10-51-51_SMOKE-Si-deposition.log", chunk.FileName);
    }

    [Fact]
    public void Read_tsv_is_chunked_by_offset_and_reports_eof()
    {
        var sut = Sut();
        string runId = "2026-09-03_11-31-53_ARM-A-probe";
        long total = new FileInfo(Path.Combine(Data, "2026-09", "03", ProbeTsv)).Length;

        var first = sut.Read(runId, "tsv", 0, 10);
        Assert.Equal(10, first.Bytes.Length);
        Assert.Equal(total, first.TotalBytes);
        Assert.False(first.Eof);
        Assert.Equal(ProbeTsv, first.FileName);

        var rest = sut.Read(runId, "tsv", 10, 1 << 20);
        Assert.Equal(total - 10, rest.Bytes.Length);
        Assert.True(rest.Eof);

        byte[] joined = [.. first.Bytes, .. rest.Bytes];
        Assert.Equal(File.ReadAllBytes(Path.Combine(Data, "2026-09", "03", ProbeTsv)), joined);
    }

    [Fact]
    public void Read_snapshot_and_recipe_return_the_matching_files()
    {
        var sut = Sut();
        var snap = sut.Read("2026-09-03_11-31-53_ARM-A-probe", "snapshot", 0, 1 << 20);
        Assert.Contains("\"dataFile\":\"2026-09-03_11-31-53_ARM-A-probe.tsv\"", Encoding.UTF8.GetString(snap.Bytes));

        var rcpx = sut.Read("2026-09-03_10-51-51_SMOKE-Si-deposition", "recipe", 0, 1 << 20);
        Assert.Equal("<Recipe/>", Encoding.UTF8.GetString(rcpx.Bytes));
    }

    [Fact]
    public void Read_rejects_unknown_run_kind_and_absent_artifact()
    {
        var sut = Sut();
        Assert.Equal(AgentErrors.NotFound,
            Assert.Throws<AgentOpException>(() => sut.Read("2026-09-03_00-00-00_Nope", "tsv", 0, 10)).Code);
        Assert.Equal(AgentErrors.BadRequest,
            Assert.Throws<AgentOpException>(() => sut.Read("2026-09-03_11-31-53_ARM-A-probe", "screenshot", 0, 10)).Code);
        Assert.Equal(AgentErrors.NotFound,
            Assert.Throws<AgentOpException>(() => sut.Read("2026-09-03_11-31-53_ARM-A-probe", "recipe", 0, 10)).Code);
        // A run id is a bare stem: no separators, no traversal.
        Assert.Equal(AgentErrors.BadRequest,
            Assert.Throws<AgentOpException>(() => sut.Read("..\\..\\secret", "tsv", 0, 10)).Code);
    }

    [Fact]
    public void List_marks_only_the_run_owning_the_live_tsv_as_running()
    {
        string live = Path.Combine(Data, "2026-09-03_12-00-00_Live-run.tsv");
        File.WriteAllText(live, "Time\tElapsed\n");
        File.AppendAllText(Path.Combine(Logs, ProcessLog), "12:00:00.100  [Recipe]  Recipe started: Live-run\n");

        var running = Sut().List(1, liveTsvPath: live)[0];
        Assert.Equal("Live-run", running.Recipe);
        Assert.Equal("running", running.Outcome);
        Assert.Null(running.EndedAt);

        // Not the live file (an embedded recipe holds the slot without a TSV, or Control was
        // killed mid-run yesterday): a run with no terminal line is unknown, never running.
        var stale = Sut().List(1, liveTsvPath: null)[0];
        Assert.Equal("unknown", stale.Outcome);
        var staleOther = Sut().List(1, liveTsvPath: Path.Combine(Data, "2026-09-03_12-30-00_Other.tsv"))[0];
        Assert.Equal("unknown", staleOther.Outcome);
    }

    [Fact]
    public void A_live_run_is_running_even_when_no_process_log_covers_it()
    {
        // Recording with no GUI process panel (no Process_*.log at all for this launch).
        string live = Path.Combine(Data, "2026-09-05_08-00-00_Headless.tsv");
        File.WriteAllText(live, "Time\tElapsed\n");

        var run = Sut().List(1, liveTsvPath: live)[0];
        Assert.Equal("running", run.Outcome);
        Assert.DoesNotContain(run.Artifacts, a => a.Kind == "log");
    }

    [Fact]
    public void Incomplete_teardown_is_a_terminal_outcome_and_bounds_the_log_window()
    {
        File.WriteAllText(Path.Combine(Data, "2026-09-03_14-00-00_SYSTEM_SHUTDOWN.tsv"), "Time\tElapsed\n");
        File.AppendAllText(Path.Combine(Logs, ProcessLog),
            "14:00:00.100  [Recipe]  Recipe started: SYSTEM_SHUTDOWN\n" +
            "14:02:10.000  [Recipe]  Recipe INCOMPLETE: SYSTEM_SHUTDOWN (02m 10s) — 1 step(s) failed. The system may NOT be in a safe state — check the power supplies, gas valves, MFCs and the vent valve.\n" +
            "14:02:10.100  [Recipe]  INCOMPLETE step: power DC1 off — supply not responding\n" +
            "14:30:00.000  [Recipe]  Recipe started: ARM-A-probe\n");

        var run = Sut().List(10, null).Single(r => r.RunId == "2026-09-03_14-00-00_SYSTEM_SHUTDOWN");
        Assert.Equal("incomplete", run.Outcome);
        Assert.Equal(new DateTime(2026, 9, 3, 14, 2, 10), run.EndedAt);
        Assert.StartsWith("(02m 10s) — 1 step(s) failed", run.Message);

        string text = Encoding.UTF8.GetString(Sut().Read(run.RunId, "log", 0, 1 << 20).Bytes);
        Assert.Contains("Recipe INCOMPLETE: SYSTEM_SHUTDOWN", text);
        Assert.DoesNotContain("INCOMPLETE step", text);     // after the terminal line
        Assert.DoesNotContain("14:30:00", text);
    }

    [Fact]
    public void Recipe_names_containing_a_parenthesis_match_their_terminal_line()
    {
        File.WriteAllText(Path.Combine(Data, "2026-09-03_15-00-00_W-Si_(v2).tsv"), "Time\tElapsed\n");
        File.AppendAllText(Path.Combine(Logs, ProcessLog),
            "15:00:00.100  [Recipe]  Recipe started: W-Si (v2)\n" +
            "15:05:00.000  [Recipe]  Recipe complete: W-Si (v2) (05m 00s)\n" +
            "15:10:00.000  [Recipe]  Recipe started: ARM-A-probe\n");

        var run = Sut().List(10, null).Single(r => r.RunId == "2026-09-03_15-00-00_W-Si_(v2)");
        Assert.Equal("W-Si (v2)", run.Recipe);
        Assert.Equal("complete", run.Outcome);
        Assert.Equal("(05m 00s)", run.Message);
        Assert.Equal(new DateTime(2026, 9, 3, 15, 5, 0), run.EndedAt);
        string text = Encoding.UTF8.GetString(Sut().Read(run.RunId, "log", 0, 1 << 20).Bytes);
        Assert.DoesNotContain("15:10:00", text);
    }

    [Fact]
    public void Run_record_sidecar_is_authoritative_over_the_process_log()
    {
        // The run's recipe lives outside the Recipes tree under a name the stem does not match,
        // the log says "complete", the sidecar says otherwise: the sidecar wins on every field.
        string tsv = Path.Combine(Data, "2026-09", "03", "2026-09-03_16-00-00_Cu_seed.tsv");
        File.WriteAllText(tsv, "Time\tElapsed\n");
        string rcpx = Path.Combine(_root, "elsewhere", "Cu seed (as run).rcpx");
        Directory.CreateDirectory(Path.GetDirectoryName(rcpx)!);
        File.WriteAllText(rcpx, "<Recipe id='as-run'/>");
        File.WriteAllText(Path.Combine(Recipes, "Cu seed.rcpx"), "<Recipe id='edited later'/>");
        File.AppendAllText(Path.Combine(Logs, ProcessLog),
            "16:00:00.100  [Recipe]  Recipe started: Cu seed\n" +
            "16:03:00.000  [Recipe]  Recipe complete: Cu seed (03m 00s)\n");
        RunRecord.Write(tsv, new RunRecord
        {
            RunId = "2026-09-03_16-00-00_Cu_seed", Recipe = "Cu seed", RecipePath = rcpx,
            StartedAt = new DateTime(2026, 9, 3, 16, 0, 0), EndedAt = new DateTime(2026, 9, 3, 16, 3, 0, 250),
            Outcome = RunRecord.Aborted, Message = "(03m 00s)",
        });

        var run = Sut().List(10, liveTsvPath: tsv).Single(r => r.RunId == "2026-09-03_16-00-00_Cu_seed");
        Assert.Equal("aborted", run.Outcome);                  // not "running" either: the run has ended
        Assert.Equal(new DateTime(2026, 9, 3, 16, 3, 0, 250), run.EndedAt);
        Assert.Equal("Cu seed", run.Recipe);
        Assert.Equal("Cu seed (as run).rcpx", run.Artifacts.Single(a => a.Kind == "recipe").FileName);
        Assert.Equal("<Recipe id='as-run'/>",
            Encoding.UTF8.GetString(Sut().Read(run.RunId, "recipe", 0, 1 << 20).Bytes));
        Assert.Contains(run.Artifacts, a => a.Kind == "log");  // the window is still served
    }

    [Fact]
    public void Sidecar_end_time_bounds_a_log_window_that_has_no_terminal_line()
    {
        // The sidecar was written but the panel's terminal log line never landed (the bus
        // subscriber died first). Without the sidecar the window ran to end-of-file.
        string tsv = Path.Combine(Data, "2026-09-03_17-00-00_Crash.tsv");
        File.WriteAllText(tsv, "Time\tElapsed\n");
        File.AppendAllText(Path.Combine(Logs, ProcessLog),
            "17:00:00.100  [Recipe]  Recipe started: Crash\n" +
            "17:01:00.000  [Execute]  deposit Si 60\n" +
            "17:20:00.000  [Core]  Hardware configuration loaded\n" +
            "17:30:00.000  [Recipe]  Recipe started: ARM-A-probe\n");
        RunRecord.Write(tsv, new RunRecord
        {
            RunId = "2026-09-03_17-00-00_Crash", Recipe = "Crash",
            StartedAt = new DateTime(2026, 9, 3, 17, 0, 0), EndedAt = new DateTime(2026, 9, 3, 17, 2, 0),
            Outcome = RunRecord.Failed, Message = "(02m 00s) — PLC read failed",
        });

        var run = Sut().List(10, null).Single(r => r.Recipe == "Crash");
        Assert.Equal("failed", run.Outcome);
        string text = Encoding.UTF8.GetString(Sut().Read(run.RunId, "log", 0, 1 << 20).Bytes);
        Assert.Contains("deposit Si 60", text);
        Assert.DoesNotContain("17:20:00", text);
        Assert.DoesNotContain("ARM-A-probe", text);
    }

    [Fact]
    public void Run_ids_with_consecutive_dots_are_listed_and_fetchable()
    {
        File.WriteAllText(Path.Combine(Data, "2026-09-03_18-00-00_W-Si_v1..2.tsv"), "Time\tElapsed\n");

        var run = Sut().List(10, null).Single(r => r.RunId == "2026-09-03_18-00-00_W-Si_v1..2");
        var chunk = Sut().Read(run.RunId, "tsv", 0, 1 << 20);
        Assert.Equal("2026-09-03_18-00-00_W-Si_v1..2.tsv", chunk.FileName);
        Assert.True(chunk.Eof);
    }

    [Fact]
    public void An_unreadable_snapshot_in_the_folder_does_not_break_the_listing()
    {
        string dated = Path.Combine(Data, "2026-09", "03");
        File.WriteAllText(Path.Combine(dated, "Deposition_broken.depo.json"), "{ not json");
        // A snapshot held open without share: File.ReadAllText throws IOException.
        using var locked = new FileStream(Path.Combine(dated, "Deposition_locked.depo.json"),
            FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var probe = Sut().List(10, null).Single(r => r.Recipe == "ARM-A-probe");
        Assert.Equal("Deposition_2026-09-03_11-31-53.depo.json", probe.Artifacts.Single(a => a.Kind == "snapshot").FileName);
    }

    [Fact]
    public void List_resolves_dates_across_a_midnight_rollover_in_the_process_log()
    {
        File.WriteAllText(Path.Combine(Data, "2026-09-03_23-58-00_Night.tsv"), "Time\tElapsed\n");
        File.WriteAllText(Path.Combine(Logs, "Process_2026-09-03_235000.log"),
            "--- Process log started 2026-09-03 23:50:00 ---\n" +
            "------ 2026-09-03  Thursday ------\n" +
            "23:58:00.200  [Recipe]  Recipe started: Night\n" +
            "------ 2026-09-04  Friday ------\n" +
            "00:02:10.000  [Recipe]  Recipe aborted: Night (04m 10s)\n");

        var night = Sut().List(10, null).Single(r => r.Recipe == "Night");
        Assert.Equal("aborted", night.Outcome);
        Assert.Equal(new DateTime(2026, 9, 4, 0, 2, 10), night.EndedAt);
    }

    [Fact]
    public void List_omits_the_log_artifact_when_no_process_log_covers_the_run()
    {
        File.WriteAllText(Path.Combine(Data, "2026-09-01_08-00-00_Orphan.tsv"), "Time\tElapsed\n");

        var orphan = Sut().List(10, null).Single(r => r.Recipe == "Orphan");
        Assert.Equal("unknown", orphan.Outcome);
        Assert.DoesNotContain(orphan.Artifacts, a => a.Kind == "log");
    }

    [Fact]
    public void Recipe_names_with_spaces_match_their_sanitised_tsv_stem()
    {
        File.WriteAllText(Path.Combine(Data, "2026-09-03_13-00-00_Cu_seed_layer.tsv"), "Time\tElapsed\n");
        File.WriteAllText(Path.Combine(Recipes, "Cu seed layer.rcpx"), "<Recipe/>");
        File.AppendAllText(Path.Combine(Logs, ProcessLog),
            "13:00:00.100  [Recipe]  Recipe started: Cu seed layer\n" +
            "13:05:00.000  [Recipe]  Recipe complete: Cu seed layer (05m 00s)\n");

        var run = Sut().List(10, null).Single(r => r.RunId == "2026-09-03_13-00-00_Cu_seed_layer");
        Assert.Equal("Cu seed layer", run.Recipe);
        Assert.Equal("complete", run.Outcome);
        Assert.Equal("Cu seed layer.rcpx", run.Artifacts.Single(a => a.Kind == "recipe").FileName);
    }

    private const string UnloggedStem = "2026-09-03_12-00-00_HandRepair";

    /// <summary>Writes the record of a run that ran with recipe logging off — no TSV was ever
    /// opened — into the dated folder, exactly where Core.WriteRunRecord puts it.</summary>
    private string WriteUnloggedRecord()
    {
        string dated = Path.Combine(Data, "2026-09", "03");
        string path = Path.Combine(dated, UnloggedStem + RunRecord.Suffix);
        var at = new DateTime(2026, 9, 3, 12, 0, 0);
        RunRecord.WriteAt(path, new RunRecord
        {
            RunId = UnloggedStem, Recipe = "HandRepair",
            StartedAt = at, EndedAt = at.AddMinutes(21),
            Outcome = RunRecord.Complete, Message = "(21m 00s)",
            Pauses = [new PauseEvent(at.AddMinutes(1), "deposit Si 600 Home", "operator de-energised RF301",
                "retry", "operator", ManualCommands: ["12:05:12 Power: RF301 OutputOn"])],
        });
        return path;
    }

    /// <summary>
    /// A run with recipe logging OFF has no TSV at all, so a listing keyed on "*.tsv" cannot
    /// see it, yet its record is the only artefact carrying the fault pauses and everything the
    /// operator hand-drove during a hold.
    /// </summary>
    [Fact]
    public void List_finds_a_run_that_had_recipe_logging_off_from_its_record_alone()
    {
        WriteUnloggedRecord();

        var runs = Sut().List(count: 10, liveTsvPath: null);

        Assert.Equal(3, runs.Count);
        var unlogged = runs.Single(r => r.RunId == UnloggedStem);
        Assert.Equal("HandRepair", unlogged.Recipe);
        Assert.Equal(new DateTime(2026, 9, 3, 12, 0, 0), unlogged.StartedAt);
        Assert.Equal("complete", unlogged.Outcome);              // from the record, not a log window
        Assert.Equal("(21m 00s)", unlogged.Message);
        Assert.DoesNotContain(unlogged.Artifacts, a => a.Kind == "tsv");   // there never was one
        Assert.Equal(UnloggedStem, runs[0].RunId);               // newest first, by its stem stamp
    }

    /// <summary>A LOGGED run must still be represented by its real TSV and counted once, even
    /// though it has a sidecar of exactly the same stem sitting beside it.</summary>
    [Fact]
    public void A_logged_run_with_a_sidecar_is_listed_once_and_keeps_its_tsv()
    {
        string dated = Path.Combine(Data, "2026-09", "03");
        RunRecord.WriteAt(Path.Combine(dated, "2026-09-03_11-31-53_ARM-A-probe" + RunRecord.Suffix),
            new RunRecord { RunId = "2026-09-03_11-31-53_ARM-A-probe", Recipe = "ARM-A-probe",
                Outcome = RunRecord.Complete });

        var runs = Sut().List(10, null);

        Assert.Equal(2, runs.Count);
        var probe = runs.Single(r => r.RunId == "2026-09-03_11-31-53_ARM-A-probe");
        Assert.Equal(ProbeTsv, probe.Artifacts.Single(a => a.Kind == "tsv").FileName);
    }

    /// <summary>The snapshot of an unlogged run cannot be found through <c>outcome.dataFile</c> —
    /// there is no data file. Schema 2's <c>outcome.runRecord</c> is what names it.</summary>
    [Fact]
    public void A_snapshot_is_matched_to_an_unlogged_run_through_its_run_record()
    {
        WriteUnloggedRecord();
        string dated = Path.Combine(Data, "2026-09", "03");
        File.WriteAllText(Path.Combine(dated, "HandRepair_2026-09-03_12-00-00.depo.json"),
            """{"schema":2,"kind":"deposition-snapshot","conditions":{},"outcome":{"result":"completed","runRecord":"2026-09-03_12-00-00_HandRepair.run.json"}}""");

        var unlogged = Sut().List(10, null).Single(r => r.RunId == UnloggedStem);

        Assert.Equal("HandRepair_2026-09-03_12-00-00.depo.json",
            unlogged.Artifacts.Single(a => a.Kind == "snapshot").FileName);
    }

    /// <summary>The run resolves by the id the listing gave out, and asking for the one artifact
    /// it does not have says so rather than claiming the run is missing.</summary>
    [Fact]
    public void Read_of_an_unlogged_run_resolves_it_and_refuses_only_the_tsv()
    {
        WriteUnloggedRecord();
        string dated = Path.Combine(Data, "2026-09", "03");
        File.WriteAllText(Path.Combine(dated, "HandRepair_2026-09-03_12-00-00.depo.json"),
            """{"schema":2,"kind":"deposition-snapshot","conditions":{},"outcome":{"result":"completed","runRecord":"2026-09-03_12-00-00_HandRepair.run.json"}}""");

        var tsvFailure = Assert.Throws<AgentOpException>(() => Sut().Read(UnloggedStem, "tsv", 0, 4096));
        Assert.Contains("recipe logging off", tsvFailure.Message);

        var snap = Sut().Read(UnloggedStem, "snapshot", 0, 4096);
        Assert.Equal("HandRepair_2026-09-03_12-00-00.depo.json", snap.FileName);
        Assert.Contains("runRecord", System.Text.Encoding.UTF8.GetString(snap.Bytes));
    }
}
