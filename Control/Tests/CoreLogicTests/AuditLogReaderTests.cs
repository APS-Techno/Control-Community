using System;
using System.IO;
using System.Linq;
using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class AuditLogReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aps-audit-" + Guid.NewGuid().ToString("N"));

    public AuditLogReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void WriteChain()
    {
        var day = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var clock = day;
        using var w = new FileAuditLogWriter(_dir, () => clock);
        w.WriteAudit(new AuditEvent { User = "alice", Event = "auth.login", Reason = "roles=Admin" });
        w.WriteAudit(new AuditEvent { User = "alice", Event = "override.set", Kind = "interlock", Id = "GVL", State = "disabled", Reason = "maint" });
        clock = day.AddDays(1);
        w.WriteAudit(new AuditEvent { User = "bob", Event = "auth.logout", Reason = "logout" });
    }

    [Fact]
    public void ListDays_returns_dates_newest_first()
    {
        WriteChain();
        Assert.Equal(new[] { "2026-06-02", "2026-06-01" }, AuditLogReader.ListDays(_dir).ToArray());
    }

    [Fact]
    public void ReadDay_parses_events_for_that_day()
    {
        WriteChain();
        var d1 = AuditLogReader.ReadDay(_dir, "2026-06-01");
        Assert.Equal(2, d1.Count);
        Assert.Equal("alice", d1[0].User);
        Assert.Equal("auth.login", d1[0].Event);
    }

    [Fact]
    public void VerifyAll_ok_for_untampered_chain()
    {
        WriteChain();
        var r = AuditLogReader.VerifyAll(_dir);
        Assert.True(r.Ok);
        Assert.Equal(3, r.RecordsChecked);
    }

    [Fact]
    public void VerifyAll_detects_tampering()
    {
        WriteChain();
        var f = Path.Combine(_dir, "2026-06-01.jsonl");
        var lines = File.ReadAllLines(f);
        lines[0] = lines[0].Replace("alice", "mallory");
        File.WriteAllLines(f, lines);

        var r = AuditLogReader.VerifyAll(_dir);
        Assert.False(r.Ok);
        Assert.Equal(1, r.BrokenAt);
    }

    [Fact]
    public void VerifyAll_detects_cross_day_tampering()
    {
        WriteChain();
        var f = Path.Combine(_dir, "2026-06-02.jsonl");   // first (only) record of day 2
        var lines = File.ReadAllLines(f);
        lines[0] = lines[0].Replace("bob", "mallory");
        File.WriteAllLines(f, lines);

        var r = AuditLogReader.VerifyAll(_dir);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Reads_while_writer_holds_todays_file_open()
    {
        // Reproduces the live-app case: FileAuditLogWriter keeps today's file open for append.
        // A reader must open with FileShare.ReadWrite or it throws "being used by another process".
        var day = new DateTime(2026, 6, 18, 8, 0, 0, DateTimeKind.Utc);
        using var w = new FileAuditLogWriter(_dir, () => day);
        w.WriteAudit(new AuditEvent { User = "alice", Event = "auth.login" });
        // writer intentionally NOT disposed — its append handle is still open

        var events = AuditLogReader.ReadDay(_dir, "2026-06-18");
        Assert.Single(events);
        Assert.Equal("alice", events[0].User);

        var r = AuditLogReader.VerifyAll(_dir);
        Assert.True(r.Ok);
        Assert.Equal(1, r.RecordsChecked);
    }

    [Fact]
    public void Missing_directory_is_empty_and_verifies_clean()
    {
        var empty = Path.Combine(_dir, "nope");
        Assert.Empty(AuditLogReader.ListDays(empty));
        Assert.Empty(AuditLogReader.ReadDay(empty, "2026-06-01"));
        Assert.True(AuditLogReader.VerifyAll(empty).Ok);
    }
}
