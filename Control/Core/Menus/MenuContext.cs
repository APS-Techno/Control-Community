using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus;

internal sealed class MenuContext : IMenuContext
{
    public required IHwCommandSink Hw { get; init; }
    public required IHardwareRegistry Hardware { get; init; }
    public required IUiServices Ui { get; init; }
    public required IFlagService Flags { get; init; }
    public required IRuntimeState Runtime { get; init; }
    public required IAuthorization Auth { get; init; }
    public required Form Owner { get; init; }
}
