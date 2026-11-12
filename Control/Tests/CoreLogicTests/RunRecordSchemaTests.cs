using APS.SystemControl.Core.Logging;
using Xunit;

namespace CoreLogicTests;

public class RunRecordSchemaTests
{
    private static string TempTsv() =>
        Path.Combine(Path.GetTempPath(), $"aps-run-{Guid.NewGuid():N}", "2026-09-04_10-00-00_X.tsv");

    [Fact]
    public void Round_trips_pauses_and_skipped_steps()
    {
        string tsv = TempTsv();
        Directory.CreateDirectory(Path.GetDirectoryName(tsv)!);
        try
        {
            var at = new DateTime(2026, 9, 4, 10, 5, 0, DateTimeKind.Utc);
            RunRecord.Write(tsv, new RunRecord
            {
                RunId = "2026-09-04_10-00-00_X", Recipe = "X",
                StartedAt = at, EndedAt = at.AddMinutes(3),
                Outcome = RunRecord.CompleteWithSkips, Message = "(03m 00s) — 1 step(s) skipped",
                Pauses = [new PauseEvent(at, "power PSU-A true", "supply not responding", "skip", "operator")],
                SkippedSteps = ["power PSU-A true"],
            });

            var r = RunRecord.TryRead(tsv);
            Assert.NotNull(r);
            Assert.Equal(3, r!.Schema);
            Assert.Equal("complete-with-skips", r.Outcome);
            var p = Assert.Single(r.Pauses);
            Assert.Equal("skip", p.Decision);
            Assert.Equal("operator", p.By);
            Assert.Equal("power PSU-A true", Assert.Single(r.SkippedSteps));
        }
        finally { Directory.Delete(Path.GetDirectoryName(tsv)!, recursive: true); }
    }

    [Fact]
    public void Round_trips_manual_commands_and_step_already_completed()
    {
        string tsv = TempTsv();
        Directory.CreateDirectory(Path.GetDirectoryName(tsv)!);
        try
        {
            var at = new DateTime(2026, 9, 4, 10, 5, 0, DateTimeKind.Utc);
            RunRecord.Write(tsv, new RunRecord
            {
                RunId = "2026-09-04_10-00-00_X", Recipe = "X",
                StartedAt = at, EndedAt = at.AddMinutes(3),
                Outcome = RunRecord.Complete, Message = "(03m 00s)",
                Pauses =
                [
                    new PauseEvent(at, "deposit Si 100 Home", "operator de-energised PSU-A (the step had already completed)",
                        "retry", "operator", ["10:05:01 panel: PSU-A OutputOn"], StepAlreadyCompleted: true),
                ],
            });

            var r = RunRecord.TryRead(tsv);
            Assert.NotNull(r);
            Assert.Equal(3, r!.Schema);
            var p = Assert.Single(r.Pauses);
            Assert.True(p.StepAlreadyCompleted);
            Assert.Equal("10:05:01 panel: PSU-A OutputOn", Assert.Single(p.ManualCommands!));
        }
        finally { Directory.Delete(Path.GetDirectoryName(tsv)!, recursive: true); }
    }

    [Fact]
    public void Reads_schema_2_sidecar_without_the_new_pause_fields()
    {
        string tsv = TempTsv();
        Directory.CreateDirectory(Path.GetDirectoryName(tsv)!);
        try
        {
            File.WriteAllText(RunRecord.PathFor(tsv), """
                { "schema": 2, "kind": "run-record", "runId": "2026-09-04_10-00-00_X",
                  "recipe": "X", "embedded": false,
                  "startedAt": "2026-09-04T10:00:00", "endedAt": "2026-09-04T10:03:00",
                  "outcome": "complete", "message": "(03m 00s)",
                  "pauses": [ { "atUtc": "2026-09-04T10:05:00", "step": "deposit Si 100 Home",
                                "reason": "operator de-energised PSU-A", "decision": "retry",
                                "by": "operator" } ],
                  "skippedSteps": [] }
                """);
            var r = RunRecord.TryRead(tsv);
            Assert.NotNull(r);
            Assert.Equal(2, r!.Schema);                      // the sidecar's OWN schema is untouched
            var p = Assert.Single(r.Pauses);
            Assert.Null(p.ManualCommands);                   // absent on the wire — default, not a crash
            Assert.False(p.StepAlreadyCompleted);             // absent on the wire — default false
        }
        finally { Directory.Delete(Path.GetDirectoryName(tsv)!, recursive: true); }
    }

    [Fact]
    public void Reads_schema_1_sidecar_without_the_new_fields()
    {
        string tsv = TempTsv();
        Directory.CreateDirectory(Path.GetDirectoryName(tsv)!);
        try
        {
            File.WriteAllText(RunRecord.PathFor(tsv), """
                { "schema": 1, "kind": "run-record", "runId": "2026-09-04_10-00-00_X",
                  "recipe": "X", "embedded": false,
                  "startedAt": "2026-09-04T10:00:00", "endedAt": "2026-09-04T10:03:00",
                  "outcome": "complete", "message": "(03m 00s)" }
                """);
            var r = RunRecord.TryRead(tsv);
            Assert.NotNull(r);
            Assert.Equal(1, r!.Schema);
            Assert.Empty(r.Pauses);
            Assert.Empty(r.SkippedSteps);
        }
        finally { Directory.Delete(Path.GetDirectoryName(tsv)!, recursive: true); }
    }
}
