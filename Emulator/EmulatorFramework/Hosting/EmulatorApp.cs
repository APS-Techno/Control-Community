using APS.Controls.Theme;
using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Setup;
using EmulatorCore.Logging;

namespace APS.SystemEmulator.Hosting;

public static class EmulatorApp
{
    public static void Run(EmulatorEdition edition, string[] args)
    {
        var cli = CliOptions.Parse(args);

        using var mutex = new Mutex(true, @"Global\APS_SystemEmulator", out bool createdNew);
        if (!createdNew)
        {
            ThemedMessageBox.Show($"{edition.ProductName} is already running.",
                "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // The Emulator is ALWAYS dark. Force it before any window is created so
        // native chrome (scrollbars/menus) renders dark, and pin VacuumTheme so
        // every form themes itself regardless of the (unreliable) .NET 9
        // color-mode APIs — on a light-themed Windows box neither
        // Application.SystemColorMode nor Application.ColorMode reports Dark even
        // after SetColorMode(Dark).
        Application.SetColorMode(SystemColorMode.Dark);
        VacuumTheme.ForceDark = true;
        EmulatorLog.Log(LogLevel.Info, "Theme",
            $"Forced dark. Application.ColorMode={Application.ColorMode}, " +
            $"Application.SystemColorMode={Application.SystemColorMode}, IsDark={VacuumTheme.IsDark}");

        if (!edition.AuthorizeProduct()) return;

        var settings = EmulatorSettings.Load(EmulatorSettings.SettingsPathFor(edition.SettingsFolder));
        VacuumTheme.LoadPalette(ProjectLifecycleController.ProjectPath(
            settings, string.IsNullOrWhiteSpace(cli.Project) ? settings.LastProject : cli.Project));
        Application.Run(new FrmDashboard(cli, edition));
    }
}
