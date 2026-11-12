using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus.Services;

internal sealed class RuntimeStateAdapter : IRuntimeState
{
    private readonly Core _core;
    public RuntimeStateAdapter(Core core) { _core = core; }

    public bool IsRecipeRunning => _core.IsRecipeRunning;

    public bool IsManualControlGated => _core.IsManualControlGated;
}
