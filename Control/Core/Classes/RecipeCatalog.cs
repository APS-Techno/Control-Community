namespace APS.SystemControl.Core.Classes;

/// <summary>One recipe file as the chooser lists it: display name without the extension,
/// the folder it lives in relative to the recipe root ("" for the root itself), the path
/// relative to the root for loading, and the file's last-write time.</summary>
public sealed record RecipeEntry(string Name, string Folder, string RelativePath, DateTime Modified);

public enum RecipeSortKey { Name, Modified }

/// <summary>
/// Pure catalogue logic behind the recipe chooser: folder grouping and the name/date
/// sort. Folders are groups, never re-ordered by a sort — the root group first, then
/// sub-folders alphabetically — while the entries inside each group follow the chosen
/// key and direction.
/// </summary>
public static class RecipeCatalog
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <param name="files">(path relative to the recipe root, last-write time) pairs; either separator.</param>
    public static IReadOnlyList<RecipeEntry> Build(IEnumerable<(string RelativePath, DateTime Modified)> files)
    {
        var entries = new List<RecipeEntry>();
        foreach (var (rel, modified) in files)
        {
            var parts = rel.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string folder = string.Join('\\', parts[..^1]);
            entries.Add(new RecipeEntry(Path.GetFileNameWithoutExtension(parts[^1]), folder, rel, modified));
        }
        return entries;
    }

    /// <summary>Folder names in display order: the root ("") first, then sub-folders
    /// alphabetically, case-insensitively.</summary>
    public static IReadOnlyList<string> Folders(IEnumerable<RecipeEntry> entries) =>
        entries.Select(e => e.Folder).Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(f => f.Length == 0 ? 0 : 1)
               .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>Entries in display order: by folder (see <see cref="Folders"/>), then by
    /// <paramref name="key"/> in the requested direction, name as the tie-break so a
    /// date sort is deterministic.</summary>
    public static IReadOnlyList<RecipeEntry> Sort(IEnumerable<RecipeEntry> entries, RecipeSortKey key, bool ascending)
    {
        var list = entries.ToList();
        var folderRank = Folders(list).Select((f, i) => (f, i)).ToDictionary(t => t.f, t => t.i, StringComparer.OrdinalIgnoreCase);
        int Cmp(RecipeEntry a, RecipeEntry b)
        {
            int c = folderRank[a.Folder].CompareTo(folderRank[b.Folder]);
            if (c != 0) return c;
            c = key == RecipeSortKey.Modified
                ? a.Modified.CompareTo(b.Modified)
                : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            if (!ascending) c = -c;
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        }
        list.Sort(Cmp);
        return list;
    }
}
