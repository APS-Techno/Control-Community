using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace APS.SystemControl.Core.Classes;

/// <summary>Read-side helper for the hash-chained audit log: list days, read a day's events,
/// and verify the full multi-day chain. Pure file I/O — no live-system dependency.</summary>
public static class AuditLogReader
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static IReadOnlyList<string> ListDays(string auditDir)
    {
        if (string.IsNullOrEmpty(auditDir) || !Directory.Exists(auditDir)) return Array.Empty<string>();
        return Directory.GetFiles(auditDir, "*.jsonl")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderByDescending(d => d, System.StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<AuditEvent> ReadDay(string auditDir, string day)
    {
        var path = Path.Combine(auditDir ?? "", day + ".jsonl");
        if (!File.Exists(path)) return Array.Empty<AuditEvent>();
        var events = new List<AuditEvent>();
        foreach (var line in ReadSharedLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<AuditEvent>(line, Json) is { } e) events.Add(e); }
            catch { /* skip malformed line */ }
        }
        return events;
    }

    public static AuditLogVerifier.VerifyResult VerifyAll(string auditDir)
    {
        if (string.IsNullOrEmpty(auditDir) || !Directory.Exists(auditDir))
            return AuditLogVerifier.VerifyResult.Success(0);

        var files = Directory.GetFiles(auditDir, "*.jsonl")
            .OrderBy(p => Path.GetFileNameWithoutExtension(p), System.StringComparer.Ordinal)
            .ToList();

        // Each daily file resets Seq to 1; VerifyChain cannot span files directly.
        // Verify each file individually, seeding initialPrev from the last record of
        // the previous file so the cross-day hash chain is also checked.
        string prevHash = FileAuditLogWriter.ChainSeed;
        long totalRecords = 0;

        foreach (var f in files)
        {
            var result = AuditLogVerifier.VerifyChain(ReadSharedLines(f), prevHash);
            totalRecords += result.RecordsChecked;
            if (!result.Ok)
                return result;

            prevHash = ReadLastHash(f) ?? prevHash;
        }

        return AuditLogVerifier.VerifyResult.Success(totalRecords);
    }

    private static string? ReadLastHash(string path)
    {
        string? lastLine = null;
        foreach (var line in ReadSharedLines(path))
            if (!string.IsNullOrWhiteSpace(line))
                lastLine = line;
        if (lastLine is null) return null;
        try
        {
            var evt = JsonSerializer.Deserialize<AuditEvent>(lastLine, Json);
            return evt?.Hash is { Length: 64 } h ? h : null;
        }
        catch { return null; }
    }

    /// <summary>Lazily reads lines with <see cref="FileShare.ReadWrite"/> so a read coexists with
    /// the writer's open append handle on today's file (a default-share open throws
    /// "the process cannot access the file because it is being used by another process").</summary>
    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(fs);
        while (r.ReadLine() is { } line)
            yield return line;
    }
}
