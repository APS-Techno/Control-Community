using System.Text.Json;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class AuditLogTests : IDisposable
{
    private readonly string _dir;
    private DateTime _clock = new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc);

    public AuditLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "audit-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }

    private FileAuditLogWriter NewWriter()
        => new(_dir, clock: () => _clock);

    private static AuditEvent SetEvent(string kind, string id, string user = "op")
        => new()
        {
            User = user, Event = "override.set", Kind = kind, Id = id,
            State = "disabled", Persist = false, Reason = "test",
        };

    private IEnumerable<string> LinesIn(string date)
    {
        string path = Path.Combine(_dir, $"{date}.jsonl");
        if (!File.Exists(path)) return [];
        // FileShare.ReadWrite so this helper coexists with the writer's
        // still-open append handle during a test.
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(s);
        var lines = new List<string>();
        while (r.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    [Fact]
    public void Single_record_first_in_chain_uses_zeros_as_prev()
    {
        using var w = NewWriter();
        w.WriteAudit(SetEvent("alarm", "a1"));
        var evt = JsonSerializer.Deserialize<AuditEvent>(LinesIn("2026-04-14").Single())!;
        Assert.Equal(FileAuditLogWriter.ChainSeed, evt.Prev);
        Assert.Equal(1, evt.Seq);
        Assert.Equal("op", evt.User);
        Assert.Equal(64, evt.Hash.Length);
    }

    [Fact]
    public void Second_record_prev_equals_first_records_hash()
    {
        using var w = NewWriter();
        w.WriteAudit(SetEvent("alarm", "a1"));
        w.WriteAudit(SetEvent("alarm", "a2"));
        var lines = LinesIn("2026-04-14");
        Assert.Equal(2, lines.Count());
        var first = JsonSerializer.Deserialize<AuditEvent>(lines.ElementAt(0))!;
        var second = JsonSerializer.Deserialize<AuditEvent>(lines.ElementAt(1))!;
        Assert.Equal(first.Hash, second.Prev);
        Assert.Equal(2, second.Seq);
    }

    [Fact]
    public void Record_hash_is_sha256_over_record_without_hash_field()
    {
        using var w = NewWriter();
        w.WriteAudit(SetEvent("alarm", "a1"));
        var evt = JsonSerializer.Deserialize<AuditEvent>(LinesIn("2026-04-14").Single())!;
        string recomputed = FileAuditLogWriter.ComputeHash(evt);
        Assert.Equal(recomputed, evt.Hash);
    }

    [Fact]
    public void Writes_rotate_per_day_but_chain_is_continuous()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "day1-a"));
            w.WriteAudit(SetEvent("alarm", "day1-b"));
        }

        // Advance the clock to the next day and open a fresh writer —
        // simulates a restart.
        _clock = _clock.AddDays(1);
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "day2-a"));
        }

        var day1 = LinesIn("2026-04-14").Select(l => JsonSerializer.Deserialize<AuditEvent>(l)!).ToArray();
        var day2 = LinesIn("2026-04-15").Select(l => JsonSerializer.Deserialize<AuditEvent>(l)!).ToArray();
        Assert.Equal(2, day1.Length);
        Assert.Single(day2);
        Assert.Equal(day1[1].Hash, day2[0].Prev);  // cross-day chain
        Assert.Equal(1, day2[0].Seq);               // per-file seq restarts
    }

    [Fact]
    public void Bootstrap_resumes_same_day_file()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "first"));
        }
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "second"));
        }

        var lines = LinesIn("2026-04-14")
            .Select(l => JsonSerializer.Deserialize<AuditEvent>(l)!)
            .ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(1, lines[0].Seq);
        Assert.Equal(2, lines[1].Seq);
        Assert.Equal(lines[0].Hash, lines[1].Prev);
    }

    [Fact]
    public void Empty_directory_bootstrap_uses_chain_seed()
    {
        using var w = NewWriter();
        w.WriteAudit(SetEvent("alarm", "only"));
        var evt = JsonSerializer.Deserialize<AuditEvent>(LinesIn("2026-04-14").Single())!;
        Assert.Equal(FileAuditLogWriter.ChainSeed, evt.Prev);
    }

    [Fact]
    public void Write_string_method_is_noop_for_file_sink()
    {
        using var w = NewWriter();
        w.Write("ignored human-readable line");
        Assert.False(File.Exists(Path.Combine(_dir, "2026-04-14.jsonl")));
    }

    [Fact]
    public void Verifier_accepts_honest_chain()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "a1"));
            w.WriteAudit(SetEvent("alarm", "a2"));
            w.WriteAudit(SetEvent("interlock", "il1"));
        }

        var result = AuditLogVerifier.VerifyFile(
            Path.Combine(_dir, "2026-04-14.jsonl"),
            FileAuditLogWriter.ChainSeed);
        Assert.True(result.Ok);
        Assert.Equal(3, result.RecordsChecked);
        Assert.Null(result.BrokenAt);
    }

    [Fact]
    public void Verifier_catches_single_byte_edit_in_middle_record()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "a1"));
            w.WriteAudit(SetEvent("alarm", "a2"));
            w.WriteAudit(SetEvent("alarm", "a3"));
        }

        // Edit record 2: change the reason field. Hash no longer matches.
        string path = Path.Combine(_dir, "2026-04-14.jsonl");
        var lines = File.ReadAllLines(path);
        lines[1] = lines[1].Replace("\"test\"", "\"tamp\"");
        File.WriteAllLines(path, lines);

        var result = AuditLogVerifier.VerifyFile(path, FileAuditLogWriter.ChainSeed);
        Assert.False(result.Ok);
        Assert.Equal(2, result.BrokenAt);
        Assert.Contains("hash mismatch", result.Reason);
    }

    [Fact]
    public void Verifier_catches_reordered_records()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "a1"));
            w.WriteAudit(SetEvent("alarm", "a2"));
            w.WriteAudit(SetEvent("alarm", "a3"));
        }

        string path = Path.Combine(_dir, "2026-04-14.jsonl");
        var lines = File.ReadAllLines(path);
        (lines[1], lines[2]) = (lines[2], lines[1]);
        File.WriteAllLines(path, lines);

        var result = AuditLogVerifier.VerifyFile(path, FileAuditLogWriter.ChainSeed);
        Assert.False(result.Ok);
        Assert.Equal(2, result.BrokenAt);
    }

    [Fact]
    public void Verifier_catches_dropped_record()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "a1"));
            w.WriteAudit(SetEvent("alarm", "a2"));
            w.WriteAudit(SetEvent("alarm", "a3"));
        }

        string path = Path.Combine(_dir, "2026-04-14.jsonl");
        var lines = File.ReadAllLines(path).Where((_, i) => i != 1).ToArray();
        File.WriteAllLines(path, lines);

        var result = AuditLogVerifier.VerifyFile(path, FileAuditLogWriter.ChainSeed);
        Assert.False(result.Ok);
        // Line 2 in the modified file is what used to be record 3, so
        // its Prev points at the hash of dropped record 2 — mismatch.
        Assert.Equal(2, result.BrokenAt);
    }

    [Fact]
    public void Verifier_catches_wrong_initial_prev()
    {
        using (var w = NewWriter())
            w.WriteAudit(SetEvent("alarm", "a1"));

        string wrongSeed = new string('f', 64);
        var result = AuditLogVerifier.VerifyFile(
            Path.Combine(_dir, "2026-04-14.jsonl"), wrongSeed);
        Assert.False(result.Ok);
        Assert.Equal(1, result.BrokenAt);
        Assert.Contains("prev mismatch", result.Reason);
    }

    [Fact]
    public void Verifier_tolerates_blank_lines()
    {
        using (var w = NewWriter())
        {
            w.WriteAudit(SetEvent("alarm", "a1"));
            w.WriteAudit(SetEvent("alarm", "a2"));
        }

        string path = Path.Combine(_dir, "2026-04-14.jsonl");
        var lines = File.ReadAllLines(path).ToList();
        lines.Insert(1, "");
        lines.Add("");
        File.WriteAllLines(path, lines);

        var result = AuditLogVerifier.VerifyFile(path, FileAuditLogWriter.ChainSeed);
        Assert.True(result.Ok);
        Assert.Equal(2, result.RecordsChecked);
    }

    [Fact]
    public void Verifier_reports_missing_file()
    {
        var result = AuditLogVerifier.VerifyFile(
            Path.Combine(_dir, "does-not-exist.jsonl"),
            FileAuditLogWriter.ChainSeed);
        Assert.False(result.Ok);
        Assert.Contains("file not found", result.Reason);
    }

    [Fact]
    public void Verifier_rejects_malformed_json()
    {
        string path = Path.Combine(_dir, "2026-04-14.jsonl");
        File.WriteAllText(path, "{this is not json\n");
        var result = AuditLogVerifier.VerifyFile(path, FileAuditLogWriter.ChainSeed);
        Assert.False(result.Ok);
        Assert.Equal(1, result.BrokenAt);
    }

    [Fact]
    public void Writer_plus_verifier_survive_hundred_mixed_events()
    {
        using (var w = NewWriter())
        {
            for (int i = 0; i < 100; i++)
            {
                string kind = i % 2 == 0 ? "alarm" : "interlock";
                w.WriteAudit(SetEvent(kind, $"rule-{i}"));
            }
        }

        var result = AuditLogVerifier.VerifyFile(
            Path.Combine(_dir, "2026-04-14.jsonl"),
            FileAuditLogWriter.ChainSeed);
        Assert.True(result.Ok);
        Assert.Equal(100, result.RecordsChecked);
    }
}
