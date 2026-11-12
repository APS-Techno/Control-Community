using APS.SystemEmulator.Hosting;

namespace APS.EmulatorCommunity;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => EmulatorApp.Run(EmulatorEdition.Community, args);
}
