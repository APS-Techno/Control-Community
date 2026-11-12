namespace EmulatorTests;

public static class ConfigsDir
{
    public static string Path(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "Configs");
            if (Directory.Exists(candidate))
                return System.IO.Path.Combine(candidate, relative);
        }
        throw new DirectoryNotFoundException(
            $"No sibling 'Configs' directory found above {AppContext.BaseDirectory}.");
    }
}
