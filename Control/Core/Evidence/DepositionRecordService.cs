using System.Globalization;

namespace APS.SystemControl.Core.Evidence;

/// <summary>
/// Writes and finalizes the self-contained .depo.json snapshot under the dated Data folder.
/// Create() freezes + fingerprints the conditions at run start; Finalize() appends the unsigned
/// outcome at run end. Pure file I/O — no DB, no UI.
/// </summary>
public sealed class DepositionRecordService
{
    private readonly string _dataRoot;

    public DepositionRecordService(string dataRoot) => _dataRoot = dataRoot;

    public static string DatedDir(string dataRoot, DateTime now) =>
        Path.Combine(dataRoot, now.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                               now.ToString("dd", CultureInfo.InvariantCulture));

    public (DepositionSnapshot Snapshot, string Path) Create(DepositionConditions conditions, string? basedOn)
    {
        conditions.Fingerprint = DepositionFingerprint.Compute(
            conditions.Recipe.Content,
            conditions.Sources.Select(s => new FingerprintSource(
                s.Flange ?? -1, s.Material, s.Format, s.MagnetronModel,
                s.PowerSupply?.Id, s.PowerSupply?.Model)));

        var snapshot = new DepositionSnapshot { Conditions = conditions, BasedOn = basedOn };

        DateTime now = DateTime.Now;
        string dir = DatedDir(_dataRoot, now);
        Directory.CreateDirectory(dir);

        string safe = Sanitize(conditions.Specimen);
        string stamp = now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string path = Path.Combine(dir, $"{safe}_{stamp}.depo.json");

        File.WriteAllText(path, DepositionSnapshotSerializer.ToJson(snapshot));
        return (snapshot, path);
    }

    public void Finalize(string path, DepositionOutcome outcome)
    {
        var snapshot = DepositionSnapshotSerializer.FromJson(File.ReadAllText(path));
        snapshot.Outcome = outcome;
        File.WriteAllText(path, DepositionSnapshotSerializer.ToJson(snapshot));
    }

    private static string Sanitize(string? specimen)
    {
        string s = string.IsNullOrWhiteSpace(specimen) ? "Deposition" : specimen;
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_').Trim();
    }
}
