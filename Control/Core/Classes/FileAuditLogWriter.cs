using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Append-only, hash-chained JSONL audit sink for safety-override events.
/// Files rotate daily under <c>{directory}/yyyy-MM-dd.jsonl</c>; each line
/// is one serialised <see cref="AuditEvent"/> whose <see cref="AuditEvent.Hash"/>
/// is SHA-256 over the previous record's hash plus this record's
/// canonical JSON (with the Hash field zeroed).
///
/// Tamper evidence: a retroactive edit to any past record changes that
/// record's computed hash, which breaks every subsequent record's Prev
/// reference. <see cref="AuditLogVerifier"/> walks a file and reports the
/// first broken line.
///
/// Thread-safety: this writer serialises its own mutating state (_seq,
/// _prevHash, _writer) under <see cref="_sync"/>. The hash chain integrity
/// depends on the seq read, hash compute, write and post-increment being
/// atomic; it must NOT rely on callers happening to hold an external lock,
/// because the registry's audit calls are not all made under its lock and
/// the same writer is shared by the Core poll thread and the UI thread.
/// </summary>
internal sealed class FileAuditLogWriter : IOverrideLogWriter, IDisposable
{
    public const string ChainSeed = "0000000000000000000000000000000000000000000000000000000000000000";

    // Serialises the chain mutation (seq/prevHash/writer + file rollover) so a
    // duplicated/skipped sequence number or forked Prev hash cannot occur when
    // the poll thread and UI thread write audit records concurrently.
    private readonly object _sync = new();

    private readonly string _directory;
    private readonly Func<DateTime> _clock;
    private readonly JsonSerializerOptions _json;

    private string? _currentDate;         // yyyy-MM-dd of the currently-open file
    private string _prevHash;             // hash of the last record written / loaded
    private long _seq;                    // next sequence number for the current file
    private StreamWriter? _writer;

    public FileAuditLogWriter(string directory, Func<DateTime>? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
        _clock = clock ?? (() => DateTime.UtcNow);
        _json = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        Directory.CreateDirectory(_directory);
        (_prevHash, _seq) = BootstrapFromNewest();
    }

    /// <summary>
    /// Legacy string-log path — audit file ignores these because the
    /// structured <see cref="WriteAudit"/> call carries the same information
    /// in a verifiable form.
    /// </summary>
    public void Write(string line) { }

    public void WriteAudit(AuditEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var now = _clock();
        string date = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        lock (_sync)
        {
            EnsureFileFor(date);

            var sealed_evt = evt with
            {
                Seq = _seq,
                Ts = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                    System.Globalization.CultureInfo.InvariantCulture),
                Prev = _prevHash,
            };

            string hash = ComputeHash(sealed_evt, _json);
            var final = sealed_evt with { Hash = hash };
            string line = JsonSerializer.Serialize(final, _json);

            _writer!.WriteLine(line);
            _writer.Flush();

            _prevHash = hash;
            _seq++;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>
    /// Computes the chain hash of a record as SHA-256 of the record's
    /// canonical JSON with Hash = "". Exposed so <see cref="AuditLogVerifier"/>
    /// can reproduce the exact hash from a parsed record.
    /// </summary>
    public static string ComputeHash(AuditEvent evt, JsonSerializerOptions? options = null)
    {
        options ??= new JsonSerializerOptions { WriteIndented = false };
        var unhashed = evt with { Hash = "" };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(unhashed, options));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private string PathFor(string date) => Path.Combine(_directory, $"{date}.jsonl");

    private void EnsureFileFor(string date)
    {
        if (_currentDate == date && _writer != null) return;

        _writer?.Dispose();
        // FileShare.ReadWrite so external readers (verifier, CountLines /
        // TryReadLastLine called from this writer itself on startup) can
        // coexist with the open write handle on Windows.
        _writer = new StreamWriter(
            new FileStream(PathFor(date), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = false,
        };
        _currentDate = date;

        // Sequence numbers restart per-file so each day's file is
        // self-describing. Chain continuity is carried by _prevHash.
        _seq = CountLines(PathFor(date)) + 1;
    }

    /// <summary>
    /// Walks the directory, finds the most recent file, and seeds the
    /// chain from its last record. Returns (prevHash, nextSeq) where
    /// nextSeq is the sequence number to use for the next record in the
    /// CURRENT day's file (or 1 if today's file does not yet exist).
    /// </summary>
    private (string prevHash, long nextSeq) BootstrapFromNewest()
    {
        var files = Directory.GetFiles(_directory, "*.jsonl")
            .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
            .ToArray();

        string prev = ChainSeed;
        foreach (var path in files)
        {
            string? last = TryReadLastLine(path);
            if (last is null) continue;
            try
            {
                var evt = JsonSerializer.Deserialize<AuditEvent>(last);
                if (evt is not null && evt.Hash.Length == 64)
                    prev = evt.Hash;
            }
            catch { /* skip malformed trailer */ }
        }

        // Today's file may already have content (same-day restart).
        string today = _clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        long seq = CountLines(PathFor(today)) + 1;
        return (prev, seq);
    }

    private static long CountLines(string path)
    {
        if (!File.Exists(path)) return 0;
        long count = 0;
        using var r = OpenSharedReader(path);
        while (r.ReadLine() is not null) count++;
        return count;
    }

    private static string? TryReadLastLine(string path)
    {
        if (!File.Exists(path)) return null;
        string? last = null;
        using var r = OpenSharedReader(path);
        while (r.ReadLine() is { } line)
            if (!string.IsNullOrWhiteSpace(line))
                last = line;
        return last;
    }

    /// <summary>
    /// Opens a reader with <see cref="FileShare.ReadWrite"/> so it coexists
    /// with an already-open append handle on the same file.
    /// </summary>
    private static StreamReader OpenSharedReader(string path)
        => new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
}
