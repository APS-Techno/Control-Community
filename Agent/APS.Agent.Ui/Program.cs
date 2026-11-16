using APS.Controls.Theme;

namespace APS.Agent.Ui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);
        VacuumTheme.ForceDark = true;
        Application.Run(new FrmAgentOperator(AgentUiSettings.FromArgs(args)));
    }
}
