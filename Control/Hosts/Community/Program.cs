using APS.SystemControl.Hosting;

namespace APS.ControlCommunity;

internal static class Program
{
    [STAThread]
    private static void Main() => ControlApp.Run(ControlEdition.Community);
}
