using System.IO;
using APS.SystemControl.Core.CoreClass;

namespace CoreLogicTests;

public class ConfigResolverTests
{
    [Fact]
    public void RecipesDir_falls_back_to_configdir_recipes_when_blank()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
        });
        Assert.Equal(Path.Combine(@"C:\Configs", "ADS", "Recipes"), cfg.RecipesDir);
    }

    [Fact]
    public void RecipesDir_uses_absolute_override_as_is()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            RecipesDir = @"E:\MyRecipes",
        });
        Assert.Equal(@"E:\MyRecipes", cfg.RecipesDir);
    }

    [Fact]
    public void LogsDir_falls_back_to_workdir_logs_when_blank()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
        });
        Assert.Equal(Path.Combine(@"C:\Work", "Logs"), cfg.LogsDir);
    }

    [Fact]
    public void LogsDir_resolves_relative_override_against_workdir()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            LogsDir = "Snapshots",
        });
        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\Work", "Snapshots")), cfg.LogsDir);
    }

    [Fact]
    public void RecipesDir_falls_back_when_override_is_empty_string()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            RecipesDir = "",
        });
        Assert.Equal(Path.Combine(@"C:\Configs", "ADS", "Recipes"), cfg.RecipesDir);
    }

    [Fact]
    public void LogsDir_falls_back_when_override_is_empty_string()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            LogsDir = "",
        });
        Assert.Equal(Path.Combine(@"C:\Work", "Logs"), cfg.LogsDir);
    }

    [Fact]
    public void DataDir_falls_back_to_workdir_data_when_blank()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
        });
        Assert.Equal(Path.Combine(@"C:\Work", "Data"), cfg.DataDir);
    }

    [Fact]
    public void DataDir_falls_back_when_override_is_empty_string()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            DataDir = "",
        });
        Assert.Equal(Path.Combine(@"C:\Work", "Data"), cfg.DataDir);
    }

    [Fact]
    public void DataDir_resolves_relative_override_against_workdir()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            DataDir = "RunData",
        });
        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\Work", "RunData")), cfg.DataDir);
    }

    [Fact]
    public void DataDir_uses_absolute_override_as_is()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            DataDir = @"E:\RunData",
        });
        Assert.Equal(@"E:\RunData", cfg.DataDir);
    }
}

public class ProcessPanelConfigFileTests
{
    [Fact]
    public void ProcessPanelConfigFile_IsNextToPanelsXml()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
        });
        Assert.Equal(Path.GetDirectoryName(cfg.GUIPanelsConfigFile), Path.GetDirectoryName(cfg.ProcessPanelConfigFile));
        Assert.Equal("ProcessPanel.xml", Path.GetFileName(cfg.ProcessPanelConfigFile));
    }
}
