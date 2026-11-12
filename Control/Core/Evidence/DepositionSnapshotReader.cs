namespace APS.SystemControl.Core.Evidence;

public static class DepositionSnapshotReader
{
    public static DepositionSnapshot Load(string path) =>
        DepositionSnapshotSerializer.FromJson(File.ReadAllText(path));

    public static string ExtractRecipeToTemp(DepositionSnapshot snapshot)
    {
        string ext = string.IsNullOrWhiteSpace(snapshot.Conditions.Recipe.Format)
            ? "rcpx" : snapshot.Conditions.Recipe.Format;
        string path = Path.Combine(Path.GetTempPath(), $"aps-rerun-{Guid.NewGuid():N}.{ext}");
        File.WriteAllText(path, snapshot.Conditions.Recipe.Content);
        return path;
    }
}
