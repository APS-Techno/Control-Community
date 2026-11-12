using System.Text.RegularExpressions;

namespace APS.SystemControl.Core.Remote.Agent;

internal static partial class RecipeStore
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _\-.]{0,63}$")]
    private static partial Regex NamePattern();

    /// <summary>Windows reserved device names (case-insensitive, extension-less base name) that
    /// fail at file creation — CON/PRN/AUX/NUL and COM1-9/LPT1-9.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Uploads through the agent server run one at a time: the name lookup and the write
    /// must not interleave with another session's, or two uploads of a new name could both pass
    /// the lookup. Writers outside this process are not covered.</summary>
    private static readonly object SaveLock = new();

    /// <summary>Filename (letters/digits/space/_/-/.), no separators, no leading dot, max 64
    /// chars; also rejects Windows reserved device names and trailing dot/space, both of which
    /// fail at file creation. The same rule validates a key's recipe folder name.</summary>
    internal static bool IsValidRecipeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || !NamePattern().IsMatch(name))
            return false;
        if (name.EndsWith('.') || name.EndsWith(' '))
            return false;
        // Windows treats the reserved-device check against the base name (the part before
        // the first dot) — "CON.rcpx" is just as unusable as "CON", but "ok.name" is fine.
        string baseName = name[..(name.IndexOf('.') is var dot && dot >= 0 ? dot : name.Length)];
        return !ReservedNames.Contains(baseName);
    }

    /// <summary>Recursive *.rcpx search matching the filename without extension, case-insensitive.</summary>
    internal static string? FindRecipePath(string recipesDir, string name) =>
        FindRecipePaths(recipesDir, name).FirstOrDefault();

    /// <summary>Every *.rcpx in the library with this name, case-insensitive — the same enumeration
    /// recipe.start and the operator's selector use, so a scoped key's checks see what they see.</summary>
    internal static IReadOnlyList<string> FindRecipePaths(string recipesDir, string name)
    {
        if (!Directory.Exists(recipesDir)) return [];
        return Directory
            .GetFiles(recipesDir, "*.rcpx", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    internal static string SavePathFor(string recipesDir, string name) =>
        Path.Combine(recipesDir, name + ".rcpx");

    /// <summary>The directory a key works in: its recipe folder under the recipes directory, or the
    /// recipes directory itself for an unscoped key (<paramref name="folder"/> null).</summary>
    internal static string ScopeDir(string recipesDir, string? folder) =>
        folder is null ? recipesDir : Path.Combine(recipesDir, folder);

    /// <summary>Every *.rcpx under the key's directory, recursive; empty when it does not exist yet.</summary>
    internal static IReadOnlyList<string> ListRecipeFiles(string recipesDir, string? folder)
    {
        string dir = ScopeDir(recipesDir, folder);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.rcpx", SearchOption.AllDirectories) : [];
    }

    /// <summary>Where an upload of <paramref name="name"/> goes, or <c>already_exists</c>.</summary>
    internal static string ResolveSavePath(string recipesDir, string name, bool overwrite, string? folder,
        Func<string, bool>? isEmbedded = null) =>
        Resolve(recipesDir, name, overwrite, folder, isEmbedded).Path;

    /// <summary>An unscoped key keeps the original rule: a same-named recipe anywhere refuses the
    /// upload without overwrite; with overwrite it is written at the root. A scoped key is confined:
    /// recipes are started and listed by name alone, so it may not take the name of an embedded
    /// recipe or of any file outside its folder, and an overwrite replaces its own single copy where
    /// it is (two copies in its folder are ambiguous and refused). <c>Replace</c> is true only when
    /// the caller may replace an existing file at <c>Path</c>.</summary>
    private static (string Path, bool Replace) Resolve(string recipesDir, string name, bool overwrite,
        string? folder, Func<string, bool>? isEmbedded)
    {
        if (folder is null)
        {
            // Recursive lookup, not just File.Exists on the root save path: a same-named
            // recipe living in a subfolder would otherwise be silently shadowed by a new
            // root-level file of the same name instead of tripping the overwrite guard.
            if (!overwrite && FindRecipePath(recipesDir, name) is not null)
                throw Exists(name);
            return (SavePathFor(recipesDir, name), overwrite);
        }

        if (isEmbedded?.Invoke(name) == true)
            throw new AgentOpException(AgentErrors.AlreadyExists,
                $"'{name}' is an embedded system recipe; choose another name");
        string scope = ScopeDir(recipesDir, folder);
        var matches = FindRecipePaths(recipesDir, name);
        if (matches.Any(m => !IsUnder(m, scope)))
            throw new AgentOpException(AgentErrors.AlreadyExists,
                $"'{name}.rcpx' exists outside this key's recipe folder; choose another name");
        if (matches.Count > 1)
            throw new AgentOpException(AgentErrors.AlreadyExists,
                $"'{name}.rcpx' exists more than once in this key's recipe folder; choose another name");
        if (matches.Count == 1)
            return overwrite ? (matches[0], true) : throw Exists(name);
        return (SavePathFor(scope, name), false);
    }

    /// <summary>Resolve the path and write the recipe as one step under <see cref="SaveLock"/>. A file
    /// that is not being replaced is opened with CreateNew, so one that appears after the lookup is
    /// refused, never overwritten. A scoped key never writes through a link.</summary>
    internal static string Save(string recipesDir, string name, string content, bool overwrite, string? folder,
        Func<string, bool>? isEmbedded = null)
    {
        lock (SaveLock)
        {
            var (path, replace) = Resolve(recipesDir, name, overwrite, folder, isEmbedded);
            if (folder is not null && replace && IsLink(path))
                throw new AgentOpException(AgentErrors.AlreadyExists,
                    $"'{name}.rcpx' is a link; it cannot be replaced by this key");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var stream = OpenForWrite(path, replace, name);
            try
            {
                using (stream)
                using (var writer = new StreamWriter(stream))     // UTF-8 without BOM, as File.WriteAllText
                    writer.Write(content);
            }
            catch when (!replace)
            {
                // Do not leave a truncated file holding the name.
                try { File.Delete(path); } catch { }
                throw;
            }
            return path;
        }
    }

    /// <summary>Opens the upload's file: Create when replacing an allowed existing file, otherwise
    /// CreateNew, so a file that appeared after the lookup is refused (already_exists), never truncated.</summary>
    internal static FileStream OpenForWrite(string path, bool replace, string name)
    {
        try
        {
            return new FileStream(path, replace ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (IOException) when (!replace && File.Exists(path))
        {
            throw Exists(name);
        }
    }

    /// <summary>recipe.start runs an embedded recipe before any file of that name; compared ignoring case.</summary>
    internal static bool IsEmbeddedName(IEnumerable<string>? knownNames, string name) =>
        knownNames?.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>Known limitation: IsUnder compares path text, so a directory junction or symlink created
    /// inside a key's folder (local access to the Control PC) makes its target's files count as inside.</summary>
    private static bool IsLink(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    private static AgentOpException Exists(string name) =>
        new(AgentErrors.AlreadyExists, $"'{name}.rcpx' exists; pass overwrite=true to replace it");

    private static bool IsUnder(string path, string dir)
    {
        string full = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
