using System.IO;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class AppSettingsProjectScannerTests : IDisposable
{
    private readonly string _tmpDir;

    public AppSettingsProjectScannerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "scanner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, true); } catch { /* best-effort */ }
    }

    private void MkProject(string name, bool withConfigXml = true)
    {
        var dir = Path.Combine(_tmpDir, name);
        Directory.CreateDirectory(dir);
        if (withConfigXml)
            File.WriteAllText(Path.Combine(dir, "Config.xml"), "<Configurations/>");
    }

    [Fact]
    public void Returns_only_subdirs_with_Config_xml()
    {
        MkProject("ADS_FULL");
        MkProject("DemoProfile");
        MkProject("EmptyDir", withConfigXml: false);

        var result = AppSettingsProjectScanner.ScanProjects(_tmpDir).ToList();

        Assert.Contains("ADS_FULL", result);
        Assert.Contains("DemoProfile", result);
        Assert.DoesNotContain("EmptyDir", result);
    }

    [Fact]
    public void Returns_empty_for_null_or_empty_or_missing_dir()
    {
        Assert.Empty(AppSettingsProjectScanner.ScanProjects(null!));
        Assert.Empty(AppSettingsProjectScanner.ScanProjects(""));
        Assert.Empty(AppSettingsProjectScanner.ScanProjects(@"C:\definitely\not\a\real\path\xyz"));
    }

    [Fact]
    public void Returns_names_sorted_alphabetically()
    {
        MkProject("Charlie");
        MkProject("Alpha");
        MkProject("Bravo");

        var result = AppSettingsProjectScanner.ScanProjects(_tmpDir).ToList();

        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, result);
    }
}
