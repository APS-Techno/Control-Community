using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Setup;

namespace EmulatorTests;

public class ProjectPathTests
{
    [Fact]
    public void ProjectPath_CombinesConfigsRootAndName()
    {
        var settings = new EmulatorSettings { ConfigsRoot = Path.Combine("root", "Configs") };

        Assert.Equal(Path.Combine("root", "Configs", "DemoProfile"),
            ProjectLifecycleController.ProjectPath(settings, "DemoProfile"));
    }

    [Theory]
    [InlineData("", "DemoProfile")]
    [InlineData("root", null)]
    [InlineData("root", " ")]
    public void ProjectPath_IsNullWhenRootOrNameMissing(string root, string? name)
        => Assert.Null(ProjectLifecycleController.ProjectPath(new EmulatorSettings { ConfigsRoot = root }, name));
}
