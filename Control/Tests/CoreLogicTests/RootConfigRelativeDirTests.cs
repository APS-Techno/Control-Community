using System.IO;
using APS.SystemControl.Core.CoreClass;

namespace CoreLogicTests;

public class RootConfigRelativeDirTests
{
    private static string ResolveUnderForeignCwd(string? workDir)
    {
        string original = Environment.CurrentDirectory;
        string foreign = Path.Combine(Path.GetTempPath(), $"aps-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(foreign);
        try
        {
            Environment.CurrentDirectory = foreign;
            var config = new Config(new AppConfig { ConfigDir = @"..\Configs", Prefix = "Sputter", WorkDir = workDir });
            return config.ConfigDir;
        }
        finally
        {
            Environment.CurrentDirectory = original;
            Directory.Delete(foreign, recursive: true);
        }
    }

    private static readonly string Expected =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\Configs", "Sputter"));

    [Fact]
    public void RelativeConfigDir_ResolvesAgainstExeFolder_NotCwd()
        => Assert.Equal(Expected, ResolveUnderForeignCwd(null));

    [Fact]
    public void RelativeConfigDir_EmptyWorkDir_ResolvesAgainstExeFolder_NotCwd()
        => Assert.Equal(Expected, ResolveUnderForeignCwd(""));
}

public class AppSettingsRelativeConfigDirTests
{
    [Fact]
    public void ResolvePath_RelativeConfigDir_UsesExeFolderNotCwd_ForDialogScanAndStatus()
    {
        string original = Environment.CurrentDirectory;
        string foreign = Path.Combine(Path.GetTempPath(), $"aps-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(foreign);
        try
        {
            Environment.CurrentDirectory = foreign;
            string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\Configs"));
            Assert.Equal(expected, Config.ResolvePath(@"..\Configs", null));
            Assert.Equal(expected, Config.ResolvePath(@"..\Configs", ""));
            Assert.Equal(@"C:\abs\Configs", Config.ResolvePath(@"C:\abs\Configs", null));
            Assert.Equal("", Config.ResolvePath("", null));
        }
        finally
        {
            Environment.CurrentDirectory = original;
            Directory.Delete(foreign, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_RelativeToWorkDir_FeedsProjectScanner()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aps-wd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Configs", "P1"));
        File.WriteAllText(Path.Combine(root, "Configs", "P1", "Config.xml"), "<Configurations/>");
        string original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = Path.GetTempPath();
            var found = APS.SystemControl.Core.Classes.AppSettingsProjectScanner
                .ScanProjects(Config.ResolvePath("Configs", root));
            Assert.Equal(new[] { "P1" }, found);
        }
        finally
        {
            Environment.CurrentDirectory = original;
            Directory.Delete(root, recursive: true);
        }
    }
}
