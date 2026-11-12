using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APS.SystemControl.Core.Logging;

/// <summary>
/// The per-run sidecar Core writes when the recipe ends: <c>&lt;stem&gt;.run.json</c>. It
/// records what the run WAS — recipe name and the file it was started from, start/end time,
/// outcome and the terminal message, plus every fault pause and everything the operator
/// hand-drove during a hold — from the code that knows, so run discovery (<c>runs.list</c>) does
/// not have to reconstruct it from process-log text. Older runs have no sidecar; readers fall
/// back to the process log for those.
/// <para>Normally it sits beside the run's DataLogger TSV and shares its stem
/// (<see cref="PathFor"/>). A USER run with recipe logging OFF has no TSV but still has a record
/// to keep, and it goes to the same dated data folder under the stem the DataLogger would have
/// used (<see cref="PathIn"/>, addressed by <c>Core.BeginRunRecord</c>) — so
/// <see cref="TryRead"/>, which is addressed by a TSV, simply never sees those.</para>
/// </summary>
public sealed class RunRecord
{
    public const int CurrentSchema = 3;
    public const string Suffix = ".run.json";

    /// <summary>Outcome vocabulary shared with the process-log <c>[Recipe]</c> terminal lines.</summary>
    public const string Complete = "complete", Failed = "failed", Aborted = "aborted",
        Incomplete = "incomplete", Unknown = "unknown";

    /// <summary>Reached its end, but at least one step was skipped by the operator at a fault
    /// pause. Its own declaration: a doc comment placed mid-declarator in the list above is
    /// silently dropped by the compiler.</summary>
    public const string CompleteWithSkips = "complete-with-skips";

    public int Schema { get; set; } = CurrentSchema;
    public string Kind { get; set; } = "run-record";
    /// <summary>TSV stem: <c>yyyy-MM-dd_HH-mm-ss_&lt;sanitised recipe&gt;</c>.</summary>
    public string RunId { get; set; } = "";
    /// <summary>Recipe name as the operator sees it (unsanitised).</summary>
    public string Recipe { get; set; } = "";
    /// <summary>Full path of the .rcpx the run was started from; null for embedded recipes.</summary>
    public string? RecipePath { get; set; }
    public bool Embedded { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    /// <summary>One of <see cref="Complete"/>, <see cref="Failed"/>, <see cref="Aborted"/>,
    /// <see cref="Incomplete"/>.</summary>
    public string Outcome { get; set; } = Unknown;
    /// <summary>The tail of the terminal log line after the recipe name: "(elapsed) — detail".</summary>
    public string? Message { get; set; }

    /// <summary>Schema 2: every fault pause during the run — the failed step, the driver's
    /// reason, the decision (retry / skip / abort) and who took it (operator / watchdog /
    /// operator-stop). Empty on a run with no pause and on schema-1 sidecars. Schema 3 adds
    /// each <see cref="PauseEvent"/>'s <c>ManualCommands</c> and <c>StepAlreadyCompleted</c> —
    /// both default on an older sidecar, so a schema-2 record still round-trips.</summary>
    public List<PauseEvent> Pauses { get; set; } = [];

    /// <summary>Schema 2: steps the operator chose to skip at a fault pause.</summary>
    public List<string> SkippedSteps { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Sidecar path for a TSV: same folder, same stem, <see cref="Suffix"/>.</summary>
    public static string PathFor(string tsvPath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(tsvPath) ?? "",
            System.IO.Path.GetFileNameWithoutExtension(tsvPath) + Suffix);

    /// <summary>Sidecar path for a run that has NO TSV to be a sibling of — recipe logging was
    /// off. Same dated run folder and same stem shape the DataLogger would have used, so the
    /// record still sits beside its <c>.depo.json</c> and <see cref="RunId"/> still parses.
    /// </summary>
    public static string PathIn(string directory, string stem) =>
        System.IO.Path.Combine(directory, stem + Suffix);

    /// <summary>Writes the sidecar beside <paramref name="tsvPath"/>. Throws on I/O failure —
    /// the caller decides whether that is fatal (Core: never; it is logged).</summary>
    public static string Write(string tsvPath, RunRecord record) => WriteAt(PathFor(tsvPath), record);

    /// <summary>Writes the sidecar at an already-resolved path. Same contract as
    /// <see cref="Write"/>; used for a run with no TSV, whose path comes from
    /// <see cref="PathIn"/> rather than from a data file.</summary>
    public static string WriteAt(string path, RunRecord record)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(record, Json));
        return path;
    }

    /// <summary>The sidecar for <paramref name="tsvPath"/>, or null when absent, unreadable or
    /// not a run record. Never throws.</summary>
    public static RunRecord? TryRead(string tsvPath)
    {
        string path = PathFor(tsvPath);
        try
        {
            if (!File.Exists(path)) return null;
            var r = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), Json);
            return r is { Kind: "run-record" } && !string.IsNullOrEmpty(r.RunId) ? r : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public override string ToString() =>
        $"{RunId}: {Outcome} {Message} ({StartedAt.ToString("s", CultureInfo.InvariantCulture)}" +
        $" → {EndedAt.ToString("s", CultureInfo.InvariantCulture)})";
}

/// <summary>One fault pause in a run's sidecar. <paramref name="Decision"/> is
/// "retry" / "skip" / "abort"; <paramref name="By"/> is "operator", "watchdog" or "operator-stop".
/// <paramref name="ManualCommands"/> is what the operator hand-drove during the hold, if anything
/// (schema 3). <paramref name="StepAlreadyCompleted"/> is true only for an operator cut that
/// landed AFTER the step it interrupted had already reported success (a late cut drained by
/// <c>ExecuteWithFaultPause</c>'s <c>if (ok)</c> block) — the step did not fail, and the run
/// record must not read as if it did (schema 3). Both default so a schema-2 sidecar (or any
/// caller built before they existed) still deserialises.</summary>
public sealed record PauseEvent(DateTime AtUtc, string Step, string Reason, string Decision, string By,
    IReadOnlyList<string>? ManualCommands = null, bool StepAlreadyCompleted = false);
