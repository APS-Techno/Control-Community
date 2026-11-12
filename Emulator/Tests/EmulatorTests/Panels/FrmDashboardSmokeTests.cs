using APS.SystemEmulator;
using APS.SystemEmulator.Hosting;
using APS.SystemEmulator.Setup;

namespace EmulatorTests.Panels;

public class FrmDashboardSmokeTests
{
    [STAThread, Fact]
    public void Constructor_DoesNotThrow()
    {
        using var form = new FrmDashboard(new CliOptions(), EmulatorEdition.Community);
        Assert.NotNull(form);
    }
}
