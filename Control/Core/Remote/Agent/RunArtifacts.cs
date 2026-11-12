using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using APS.SystemControl.Core.Evidence;
using APS.SystemControl.Core.Logging;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>One on-disk artifact of a run: <c>tsv</c>, <c>log</c>, <c>recipe</c> or <c>snapshot</c>.</summary>
internal sealed record RunArtifactInfo(string Kind, string FileName, long Bytes);

/// <summary>A run as the agent sees it: identified by its DataLogger TSV stem
/// (<c>yyyy-MM-dd_HH-mm-ss_&lt;recipe&gt;</c>), with the outcome and end time taken from the run's
/// <c>.run.json</c> sidecar (or, for runs older than the sidecar, the process log's <c>[Recipe]</c>
/// lines) and the list of artifacts that exist for it.</summary>
internal sealed record RunSummary(
    string RunId, string Recipe, DateTime StartedAt, DateTime? EndedAt,
    string Outcome, string? Message, IReadOnlyList<RunArtifactInfo> Artifacts);

internal sealed record RunFileChunk(string RunId, string Kind, string FileName, long Offset, long TotalBytes, byte[] Bytes)
{
    public bool Eof => Offset + Bytes.Length >= TotalBytes;
}

/// <summary>
/// Post-run artifact discovery for the Agent bridge (<c>runs.list</c> / <c>runs.file</c>).
/// Pure file I/O over the three configured roots — no Core state, so it is testable on a temp tree.
///
/// A run is a STEM (<c>yyyy-MM-dd_HH-mm-ss_&lt;recipe&gt;</c>) in the Data tree, evidenced by its
/// DataLogger TSV or — for a run with recipe logging OFF, which has no TSV at all — by its
/// <c>.run.json</c> alone. Around the stem:
/// <list type="bullet">
/// <item><c>&lt;stem&gt;.run.json</c> — the sidecar Core writes at recipe end (<see cref="RunRecord"/>):
/// recipe name and source file, outcome, end time, terminal message. Authoritative when present.</item>
/// <item><c>log</c> — the window of the <c>Process_*.log</c> written by the Control launch that
/// ran it, from its <c>Recipe started:</c> line to its <c>Recipe complete/failed/aborted/INCOMPLETE:</c>
/// line. One process log covers a whole Control launch and many runs; only the window is served.
/// For runs without a sidecar the outcome is read from this window.</item>
/// <item><c>recipe</c> — the <c>.rcpx</c> the sidecar names, else the file in the Recipes tree whose
/// sanitised name matches the stem. Served as it is on disk now: a post-run edit shows.</item>
/// <item><c>snapshot</c> — the <c>.depo.json</c> beside the TSV whose <c>outcome.dataFile</c> names it
/// (operator-authorised runs only; agent-started runs have none). It embeds the recipe as run.</item>
/// </list>
/// Paths never cross the wire: callers name a run id and a kind, and this class resolves files.
/// The <c>log</c> and <c>recipe</c> kinds are also mentioned in a run's <c>.run.json</c>, whose
/// paths stay on the machine.
/// </summary>
internal sealed partial class RunArtifacts(string dataDir, string logsDir, string recipesDir)
{
    public static readonly string[] Kinds = ["tsv", "log", "recipe", "snapshot"];

    /// <summary>Default and ceiling for one <c>runs.file</c> chunk. Base64 inflates by 4/3 and the
    /// AgentServer line limit is 4M chars, so 2 MiB raw (~2.8M chars) stays inside it.</summary>
    public const int DefaultMaxBytes = 1 << 20;
    public const int MaxMaxBytes = 2 << 20;

    /// <summary>How far a <c>Recipe started:</c> log line may sit from the TSV start stamp, either
    /// side: an embedded run opens the TSV before logging the line, a user/agent run logs the line
    /// first and opens the TSV after StartRecipe (sub-second apart in practice). A generous window
    /// absorbs a slow disk without reaching into a neighbouring run of the same recipe.</summary>
    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(60);

    /// <summary>When a sidecar gives the end time but the log has no terminal line for the run (a
    /// crash between the two writes), the window is cut at the last line stamped before this
    /// much after the recorded end.</summary>
    private static readonly TimeSpan EndTolerance = TimeSpan.FromSeconds(5);

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive,
    };
    private static readonly EnumerationOptions TopOnly = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive,
    };

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(.+)$")]
    private static partial Regex StemPattern();

    [GeneratedRegex(@"^Process_(\d{4}-\d{2}-\d{2}_\d{6})\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex LogNamePattern();

    /// <summary>A <c>[Recipe]</c> bus line as BaseProcessPanel writes it: "HH:mm:ss.fff  [Recipe]  Recipe started: name"
    /// or "... Recipe complete|failed|aborted|INCOMPLETE: name (elapsed) — detail" (CoreThread.Terminal).</summary>
    [GeneratedRegex(@"^(\d{2}:\d{2}:\d{2}\.\d{3})  \[Recipe\]  Recipe (started|complete|failed|aborted|INCOMPLETE): (.+)$")]
    private static partial Regex RecipeLinePattern();

    [GeneratedRegex(@"^------ (\d{4}-\d{2}-\d{2})  ")]
    private static partial Regex DaySeparatorPattern();

    /// <param name="count">How many runs, newest first.</param>
    /// <param name="liveTsvPath">The TSV DataLogger is writing right now (Core.ActiveDataLogPath),
    /// or null. The run owning it is reported <c>running</c>; nothing else ever is.</param>
    public IReadOnlyList<RunSummary> List(int count, string? liveTsvPath)
    {
        var runs = new List<RunSummary>();
        var logCache = new Dictionary<string, ParsedLog>(StringComparer.OrdinalIgnoreCase);
        foreach (var found in EnumerateRuns())
        {
            if (runs.Count >= Math.Max(1, count)) break;
            bool live = found.HasTsv && liveTsvPath is not null && PathEquals(found.StemPath, liveTsvPath);
            if (Summarize(found, logCache, live) is { } run) runs.Add(run);
        }
        return runs;
    }

    /// <summary>Null when the evidence that made this a run vanished between enumeration and
    /// now — the TSV for a logged run, the <c>.run.json</c> for an unlogged one.</summary>
    private RunSummary? Summarize(RunOnDisk run, Dictionary<string, ParsedLog> logCache, bool live)
    {
        var artifacts = new List<RunArtifactInfo>();
        if (run.HasTsv)
        {
            if (LengthOf(run.StemPath) is not { } tsvBytes) return null;
            artifacts.Add(new("tsv", Path.GetFileName(run.StemPath), tsvBytes));
        }

        var record = RunRecord.TryRead(run.StemPath);
        // No TSV and no record is not a run at all — only the two together make an unlogged one
        // visible, and the record is what carries its outcome, its pauses and whatever the
        // operator hand-drove during a hold.
        if (!run.HasTsv && record is null) return null;
        var window = FindLogWindow(run, logCache, record?.EndedAt);

        string recipe = record?.Recipe ?? window?.Recipe ?? run.StemRecipe;
        // A sidecar exists only once the run has ended, so it settles "running" too.
        string outcome = record?.Outcome
            ?? (live ? "running" : window?.Outcome ?? "unknown");
        DateTime? ended = record?.EndedAt ?? window?.EndedAt;
        string? message = record?.Message ?? window?.Message;

        if (window is not null)
            artifacts.Add(new("log", run.RunId + ".log", Encoding.UTF8.GetByteCount(window.Text)));
        if (FindRecipe(record, run.StemRecipe) is { } rcpx && LengthOf(rcpx) is { } rcpxBytes)
            artifacts.Add(new("recipe", Path.GetFileName(rcpx), rcpxBytes));
        if (FindSnapshot(run) is { } snap && LengthOf(snap) is { } snapBytes)
            artifacts.Add(new("snapshot", Path.GetFileName(snap), snapBytes));

        return new RunSummary(run.RunId, recipe, run.StartedAt, ended, outcome, message, artifacts);
    }

    public RunFileChunk Read(string runId, string kind, long offset, int maxBytes)
    {
        if (!Kinds.Contains(kind))
            throw new AgentOpException(AgentErrors.BadRequest,
                $"unknown artifact kind '{kind}'; expected one of {string.Join(", ", Kinds)}");
        var run = ResolveRun(runId);
        maxBytes = Math.Clamp(maxBytes, 1, MaxMaxBytes);
        if (offset < 0) throw new AgentOpException(AgentErrors.BadRequest, "offset must be >= 0");

        var record = kind is "log" or "recipe" ? RunRecord.TryRead(run.StemPath) : null;
        if (kind == "log")
        {
            var window = FindLogWindow(run, new Dictionary<string, ParsedLog>(StringComparer.OrdinalIgnoreCase), record?.EndedAt)
                ?? throw new AgentOpException(AgentErrors.NotFound, $"no process log covers run '{runId}'");
            return Slice(runId, kind, runId + ".log", Encoding.UTF8.GetBytes(window.Text), offset, maxBytes);
        }

        string path = kind switch
        {
            // An unlogged run has no TSV to serve, and says so rather than reporting the run
            // itself missing — it is listed, and its other artifacts are readable.
            "tsv" => run.HasTsv ? run.StemPath
                : throw new AgentOpException(AgentErrors.NotFound,
                    $"run '{runId}' has no TSV (it ran with recipe logging off)"),
            "recipe" => FindRecipe(record, run.StemRecipe)
                ?? throw new AgentOpException(AgentErrors.NotFound, $"no .rcpx on disk for run '{runId}'"),
            _ => FindSnapshot(run)
                ?? throw new AgentOpException(AgentErrors.NotFound, $"no .depo.json snapshot for run '{runId}'"),
        };
        try { return SliceFile(runId, kind, path, offset, maxBytes); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            throw new AgentOpException(AgentErrors.NotFound,
                $"the {kind} artifact of run '{runId}' is not readable on the machine ({ex.GetType().Name})");
        }
    }

    private static RunFileChunk Slice(string runId, string kind, string fileName, byte[] all, long offset, int maxBytes)
    {
        long total = all.LongLength;
        if (offset > total) throw new AgentOpException(AgentErrors.BadRequest, $"offset {offset} is past the end ({total} bytes)");
        int len = (int)Math.Min(maxBytes, total - offset);
        return new RunFileChunk(runId, kind, fileName, offset, total, all.AsSpan((int)offset, len).ToArray());
    }

    private static RunFileChunk SliceFile(string runId, string kind, string path, long offset, int maxBytes)
    {
        // ReadWrite share: the TSV of a running recipe is still open in DataLogger's StreamWriter.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long total = fs.Length;
        if (offset > total) throw new AgentOpException(AgentErrors.BadRequest, $"offset {offset} is past the end ({total} bytes)");
        int len = (int)Math.Min(maxBytes, total - offset);
        var buf = new byte[len];
        fs.Position = offset;
        int read = 0;
        while (read < len)
        {
            int n = fs.Read(buf, read, len - read);
            if (n <= 0) break;
            read += n;
        }
        if (read < len) Array.Resize(ref buf, read);
        return new RunFileChunk(runId, kind, Path.GetFileName(path), offset, total, buf);
    }

    /// <summary>One run on disk. <paramref name="StemPath"/> is the CANONICAL TSV path for the
    /// stem — <c>&lt;folder&gt;\&lt;stem&gt;.tsv</c> — and exists only when
    /// <paramref name="HasTsv"/>; for an unlogged run it is a path that was never written, kept
    /// so everything addressed by "the TSV's folder and stem" (the <c>.run.json</c> sidecar, the
    /// snapshot beside it) resolves through one field either way.</summary>
    private sealed record RunOnDisk(string RunId, string StemPath, DateTime StartedAt, string StemRecipe, bool HasTsv);

    private IEnumerable<RunOnDisk> EnumerateRuns()
    {
        if (!Directory.Exists(dataDir)) yield break;
        var found = new List<RunOnDisk>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in Directory.EnumerateFiles(dataDir, "*.tsv", Recursive))
        {
            string stem = Path.GetFileNameWithoutExtension(f);
            if (!TryParseStem(stem, out var started, out var recipe) || !seen.Add(stem)) continue;
            found.Add(new RunOnDisk(stem, f, started, recipe, HasTsv: true));
        }

        // Runs with recipe logging OFF: no TSV was ever opened, but Core still writes the record
        // (every fault pause, and everything the operator hand-drove during a hold). Second pass,
        // so a logged run is always represented by its real TSV and never by its sidecar.
        foreach (var f in Directory.EnumerateFiles(dataDir, "*" + RunRecord.Suffix, Recursive))
        {
            string name = Path.GetFileName(f);
            string stem = name[..^RunRecord.Suffix.Length];
            if (!TryParseStem(stem, out var started, out var recipe) || !seen.Add(stem)) continue;
            string canonicalTsv = Path.Combine(Path.GetDirectoryName(f) ?? "", stem + ".tsv");
            found.Add(new RunOnDisk(stem, canonicalTsv, started, recipe, HasTsv: false));
        }

        foreach (var r in found.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.RunId, StringComparer.Ordinal))
            yield return r;
    }

    private static bool TryParseStem(string stem, out DateTime started, out string recipe)
    {
        started = default; recipe = "";
        var m = StemPattern().Match(stem);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value, DataLogger.StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out started)) return false;
        recipe = m.Groups[2].Value;
        return true;
    }

    /// <summary>A run id is a bare stem: the leading stamp is pinned by the pattern and any
    /// path separator fails the invalid-file-name-char check, so "..\..\x" cannot get through.
    /// Consecutive dots inside the recipe part are legal ("v1..2" is a valid file name).
    /// <para>Resolved by the TSV first and by the <c>.run.json</c> second, mirroring
    /// <see cref="EnumerateRuns"/>: a run that ran with recipe logging off is addressable by the
    /// same id the listing gave out.</para></summary>
    private RunOnDisk ResolveRun(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId)
            || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || runId.Contains('/') || runId.Contains('\\')
            || !TryParseStem(runId, out var started, out var recipe))
            throw new AgentOpException(AgentErrors.BadRequest,
                "runId must be a run stem of the form yyyy-MM-dd_HH-mm-ss_<recipe> as returned by runs.list");
        string missing = $"no run '{runId}' (neither its TSV nor its {RunRecord.Suffix} is under the Data folder)";
        if (!Directory.Exists(dataDir))
            throw new AgentOpException(AgentErrors.NotFound, missing);

        // Where DataLogger puts a TSV: the Data root (agent runs) or the dated specimen folder
        // (operator runs). Probe both before walking the whole tree. The record shares the stem
        // and the folder, so the same two probes find it.
        string file = runId + ".tsv";
        string sidecar = runId + RunRecord.Suffix;
        var dirs = new[] { dataDir, DepositionRecordService.DatedDir(dataDir, started) };
        foreach (var dir in dirs)
        {
            string candidate = Path.Combine(dir, file);
            if (File.Exists(candidate)) return new RunOnDisk(runId, candidate, started, recipe, HasTsv: true);
        }
        foreach (var f in Directory.EnumerateFiles(dataDir, file, Recursive))
            return new RunOnDisk(runId, f, started, recipe, HasTsv: true);

        foreach (var dir in dirs)
        {
            string candidate = Path.Combine(dir, sidecar);
            if (File.Exists(candidate))
                return new RunOnDisk(runId, Path.Combine(dir, file), started, recipe, HasTsv: false);
        }
        foreach (var f in Directory.EnumerateFiles(dataDir, sidecar, Recursive))
            return new RunOnDisk(runId, Path.Combine(Path.GetDirectoryName(f) ?? "", file), started, recipe, HasTsv: false);

        throw new AgentOpException(AgentErrors.NotFound, missing);
    }

    /// <summary>The sidecar's recipe file when it still exists, else the Recipes-tree file whose
    /// sanitised name matches the stem (runs older than the sidecar, or a file moved since).</summary>
    private string? FindRecipe(RunRecord? record, string stemRecipe)
    {
        if (record?.RecipePath is { } p && File.Exists(p)) return p;
        if (!Directory.Exists(recipesDir)) return null;
        return Directory.EnumerateFiles(recipesDir, "*.rcpx", Recursive)
            .FirstOrDefault(f => NameMatches(Path.GetFileNameWithoutExtension(f), stemRecipe));
    }

    /// <summary>The DataLogger stem carries the recipe name with invalid chars and spaces
    /// replaced by '_'; compare through the same sanitiser so "Cu seed layer" ↔ "Cu_seed_layer".</summary>
    private static bool NameMatches(string recipeName, string stemRecipe) =>
        string.Equals(DataLogger.SanitizeFileName(recipeName), stemRecipe, StringComparison.OrdinalIgnoreCase);

    /// <summary>The snapshot whose outcome names this run — by its TSV (<c>dataFile</c>) or, for
    /// a run with recipe logging off which has no TSV to be named by, by its record
    /// (<c>runRecord</c>, snapshot schema 2). Either key alone identifies the run: both are the
    /// stem plus a suffix.</summary>
    private static string? FindSnapshot(RunOnDisk run)
    {
        string dir = Path.GetDirectoryName(run.StemPath)!;
        string tsvName = Path.GetFileName(run.StemPath);
        string recordName = run.RunId + RunRecord.Suffix;
        if (!Directory.Exists(dir)) return null;
        foreach (var f in Directory.EnumerateFiles(dir, "*.depo.json", TopOnly))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                if (doc.RootElement.TryGetProperty("outcome", out var outcome)
                    && outcome.ValueKind == JsonValueKind.Object
                    && (Names(outcome, "dataFile", tsvName) || Names(outcome, "runRecord", recordName)))
                    return f;
            }
            // Not a snapshot we can read (malformed, locked, ACL-denied): skip it. One bad
            // sidecar must not fail the whole listing — and its path must not reach the wire.
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return null;

        static bool Names(JsonElement outcome, string property, string fileName) =>
            outcome.TryGetProperty(property, out var v)
            && v.ValueKind == JsonValueKind.String
            && string.Equals(v.GetString(), fileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>File length, or null when the file is gone or unreadable (never throws).</summary>
    private static long? LengthOf(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? fi.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool PathEquals(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private sealed record LogWindow(string Recipe, string? Outcome, string? Message, DateTime? EndedAt, string Text);

    /// <summary>A process log parsed once: every raw line plus the absolute timestamp of the
    /// timestamped ones, resolved through the "------ yyyy-MM-dd  dddd ------" day separators
    /// (lines before the first separator take the launch date from the file name).</summary>
    private sealed class ParsedLog
    {
        public required string Path;
        public required string[] Lines;
        public required DateTime?[] At;
    }

    private LogWindow? FindLogWindow(RunOnDisk tsv, Dictionary<string, ParsedLog> cache, DateTime? endedAt)
    {
        // Newest launch that started before the run first; an older one only if the newer file
        // does not contain the run (e.g. a second panel's log or a launch that never ran it).
        foreach (var (launch, path) in EnumerateLogs().Where(l => l.Launch <= tsv.StartedAt + StartTolerance)
                                                     .OrderByDescending(l => l.Launch).Take(3))
        {
            if (!cache.TryGetValue(path, out var log))
                cache[path] = log = Parse(path, launch);
            if (Window(log, tsv, endedAt) is { } w) return w;
        }
        return null;
    }

    private IEnumerable<(DateTime Launch, string Path)> EnumerateLogs()
    {
        if (!Directory.Exists(logsDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(logsDir, "Process_*.log", TopOnly))
        {
            var m = LogNamePattern().Match(Path.GetFileName(f));
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd_HHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var launch))
                yield return (launch, f);
        }
    }

    // One-entry parse cache across calls: runs.file pages a log window in 1 MiB chunks, and the
    // process log grows for the whole 24/7 launch. Keyed on the file's length + write time so a
    // live log is re-read once it changes.
    private static readonly object ParseSync = new();
    private static (string Path, long Length, DateTime Written, ParsedLog Log)? _lastParsed;

    private static ParsedLog Parse(string path, DateTime launch)
    {
        var fi = new FileInfo(path);
        long length = fi.Exists ? fi.Length : -1;
        DateTime written = fi.Exists ? fi.LastWriteTimeUtc : default;
        lock (ParseSync)
        {
            if (_lastParsed is { } c && c.Length == length && c.Written == written
                && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase))
                return c.Log;
        }

        string[] lines;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
            lines = sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        var at = new DateTime?[lines.Length];
        DateTime day = launch.Date;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            var sep = DaySeparatorPattern().Match(line);
            if (sep.Success)
            {
                if (DateTime.TryParseExact(sep.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d)) day = d;
                continue;
            }
            if (line.Length >= 12 && TimeSpan.TryParseExact(line.AsSpan(0, 12), @"hh\:mm\:ss\.fff",
                    CultureInfo.InvariantCulture, out var tod))
                at[i] = day + tod;
        }
        var parsed = new ParsedLog { Path = path, Lines = lines, At = at };
        lock (ParseSync) _lastParsed = (path, length, written, parsed);
        return parsed;
    }

    private static LogWindow? Window(ParsedLog log, RunOnDisk tsv, DateTime? endedAt)
    {
        // The start line: a "Recipe started: <name>" for this recipe, closest to the TSV stamp
        // and within tolerance.
        int startIdx = -1; TimeSpan best = TimeSpan.MaxValue; string recipe = tsv.StemRecipe;
        for (int i = 0; i < log.Lines.Length; i++)
        {
            if (log.At[i] is not { } t) continue;
            var delta = t - tsv.StartedAt;
            if (delta < -StartTolerance || delta > StartTolerance) continue;
            var m = RecipeLinePattern().Match(log.Lines[i]);
            if (!m.Success || m.Groups[2].Value != "started" || !NameMatches(m.Groups[3].Value, tsv.StemRecipe)) continue;
            var abs = delta.Duration();
            if (abs < best) { best = abs; startIdx = i; recipe = m.Groups[3].Value; }
        }
        if (startIdx < 0) return null;

        // The terminal line for the same recipe; stop before a later "started" of it.
        int endIdx = log.Lines.Length - 1; string? outcome = null, message = null; DateTime? ended = null;
        bool terminated = false;
        for (int i = startIdx + 1; i < log.Lines.Length; i++)
        {
            var m = RecipeLinePattern().Match(log.Lines[i]);
            if (!m.Success) continue;
            string rest = m.Groups[3].Value;
            if (m.Groups[2].Value == "started")
            {
                if (NameMatches(rest, tsv.StemRecipe)) { endIdx = i - 1; terminated = true; break; }
                continue;
            }
            if (SplitTerminal(rest, tsv.StemRecipe) is not { } name) continue;
            outcome = m.Groups[2].Value.ToLowerInvariant();
            message = rest.Length > name.Length ? rest[name.Length..].Trim() : null;
            ended = log.At[i];
            endIdx = i;
            terminated = true;
            break;
        }
        // No terminal line but the sidecar knows when the run ended (Control died between the
        // two writes): cut at the last line stamped before that, not at end-of-file.
        if (!terminated && endedAt is { } cut)
        {
            DateTime limit = cut + EndTolerance;
            for (int i = startIdx + 1; i < log.Lines.Length; i++)
                if (log.At[i] is { } t && t > limit) { endIdx = i - 1; break; }
        }

        var sb = new StringBuilder();
        sb.Append("--- ").Append(Path.GetFileName(log.Path)).Append(": ").Append(recipe)
          .Append(' ').Append(tsv.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
          .Append(" to ").Append(ended?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "(no terminal line)")
          .Append(" ---\n");
        for (int i = startIdx; i <= endIdx; i++) sb.Append(log.Lines[i]).Append('\n');
        return new LogWindow(recipe, outcome, message, ended, sb.ToString());
    }

    /// <summary>Terminal lines read "name (elapsed)…" and a recipe name may itself contain " ("
    /// ("W-Si (v2).rcpx" is a legal file name), so the name is the prefix before whichever " ("
    /// makes it match this run's stem; null when none does (another recipe's line).</summary>
    private static string? SplitTerminal(string rest, string stemRecipe)
    {
        for (int p = rest.IndexOf(" (", StringComparison.Ordinal); p >= 0;
             p = rest.IndexOf(" (", p + 1, StringComparison.Ordinal))
            if (NameMatches(rest[..p], stemRecipe)) return rest[..p];
        return NameMatches(rest, stemRecipe) ? rest : null;
    }
}
