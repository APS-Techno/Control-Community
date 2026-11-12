using APS.SystemEmulator.Setup;

namespace EmulatorTests.Setup;

public class RuntimeCheckTests
{
    [Fact]
    public void Detects_desktop_runtime_present()
    {
        const string output = """
            Microsoft.AspNetCore.App 8.0.1 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
            Microsoft.NETCore.App 10.0.0 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.WindowsDesktop.App 10.0.2 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
            """;

        var row = RuntimeCheck.ParseAndEvaluate(output);

        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.Contains("10.0.2", row.Detail);
    }

    [Fact]
    public void Reports_missing_when_no_desktop_runtime()
    {
        const string output = """
            Microsoft.AspNetCore.App 8.0.1 [...]
            Microsoft.NETCore.App 9.0.0 [...]
            """;

        var row = RuntimeCheck.ParseAndEvaluate(output);

        Assert.Equal(CheckStatus.Missing, row.Status);
    }

    [Fact]
    public void Reports_missing_when_desktop_runtime_is_older_major()
    {
        const string output = """
            Microsoft.WindowsDesktop.App 9.0.23 [...]
            """;

        var row = RuntimeCheck.ParseAndEvaluate(output);

        Assert.Equal(CheckStatus.Missing, row.Status);
    }

    [Fact]
    public void Reports_the_net10_runtime_when_net9_is_listed_first()
    {
        const string output = """
            Microsoft.WindowsDesktop.App 9.0.23 [...]
            Microsoft.WindowsDesktop.App 10.0.15 [...]
            """;

        var row = RuntimeCheck.ParseAndEvaluate(output);

        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.Contains("10.0.15", row.Detail);
    }
}
