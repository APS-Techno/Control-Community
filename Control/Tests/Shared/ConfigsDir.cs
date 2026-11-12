namespace APS.SystemControl.Tests.Shared;

public static class ConfigsDir
{
    public static string Root { get; } = Locate();

    public static string Path(string relative) => System.IO.Path.Combine(Root, relative);

    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "Configs");
            if (Directory.Exists(candidate) && Directory.Exists(System.IO.Path.Combine(dir.FullName, "Control")))
                return candidate;
        }
        throw new DirectoryNotFoundException("No sibling Configs repository found above " + AppContext.BaseDirectory);
    }
}
