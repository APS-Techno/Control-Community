using APS.Emulator.Devices.Demo;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;

namespace APS.SystemEmulator.Hosting;

public sealed class EmulatorEdition(
    string productName,
    string settingsFolder,
    IReadOnlyList<IDeviceRegistration> registrations,
    Func<bool> authorizeProduct,
    IReadOnlyList<IDashboardExtension> dashboardExtensions,
    IReadOnlyList<string> extraHelpFolders)
{
    public string ProductName { get; } = productName;
    public string SettingsFolder { get; } = settingsFolder;
    public IReadOnlyList<IDeviceRegistration> Registrations { get; } = registrations;
    public Func<bool> AuthorizeProduct { get; } = authorizeProduct;
    public IReadOnlyList<IDashboardExtension> DashboardExtensions { get; } = dashboardExtensions;
    public IReadOnlyList<string> ExtraHelpFolders { get; } = extraHelpFolders;

    public static EmulatorEdition Community { get; } = new(
        "APS Emulator Community",
        "APS.EmulatorCommunity",
        [new S7DeviceRegistration(), new DemoDeviceRegistration()],
        () => true,
        [],
        []);
}
