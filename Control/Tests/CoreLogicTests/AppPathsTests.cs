using APS.SystemControl.Core.CoreClass;

namespace CoreLogicTests;

public class AppPathsTests
{
    [Fact]
    public void Initialize_copies_resolved_dirs_from_config()
    {
        var cfg = new Config(new AppConfig
        {
            ConfigDir = @"C:\Configs", Prefix = "ADS", WorkDir = @"C:\Work",
            RecipesDir = @"E:\R", LogsDir = @"E:\L", DataDir = @"E:\D",
        });

        AppPaths.Initialize(cfg);

        Assert.Equal(@"E:\R", AppPaths.RecipesDir);
        Assert.Equal(@"E:\L", AppPaths.LogsDir);
        Assert.Equal(@"E:\D", AppPaths.DataDir);
    }
}
