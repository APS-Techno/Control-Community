using System;
using System.IO;
using APS.SystemControl.Core.CoreClass;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Config.LoadRootConfig must fail loudly, naming the file and the reason (C5).
/// RootConfig.xml is hand-edited on target machines; a swallowed failure leaves
/// appConfig null, and the first downstream `appConfig!` access then dies with
/// an unexplained NullReferenceException.
/// </summary>
public class RootConfigLoadFailureTests
{
    [Fact]
    public void Missing_root_config_throws_naming_file_and_reason()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-rootcfg-{Guid.NewGuid():N}.xml");

        var ex = Assert.Throws<InvalidOperationException>(() => new Config(path));
        Assert.Contains(path, ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Malformed_root_config_throws_naming_file_and_reason()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-rootcfg-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, "<AppConfig><ConfigDir>oops");   // truncated XML
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new Config(path));
            Assert.Contains(path, ex.Message);
            Assert.NotNull(ex.InnerException);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Valid_root_config_still_loads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-rootcfg-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <AppConfig>
                <ConfigDir>D:\Configs</ConfigDir>
                <Prefix>DemoProfile</Prefix>
            </AppConfig>
            """);
        try
        {
            var cfg = new Config(path);
            Assert.Equal(@"D:\Configs\DemoProfile", cfg.ConfigDir);
        }
        finally { File.Delete(path); }
    }
}
