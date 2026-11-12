using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Hosting;

namespace APS.SystemControl;

public partial class FrmMain
{
    internal MenuStrip MainMenu => menuStrip1;

    internal ToolStripMenuItem FileMenu => fileToolStripMenuItem;

    internal ToolStripMenuItem ToolsMenu => toolsToolStripMenuItem;

    internal ToolStripMenuItem HelpMenu => helpToolStripMenuItem;

    internal TabControl MainTabs => tabControlMain;

    internal StatusStrip StatusBar => statusStrip1;

    internal ToolStrip MainToolStrip => toolStrip1;

    internal APS.SystemControl.Core.Core? CoreInstance => systemThread?.Core;

    internal ChannelPool? Pool => pool;

    internal SourceManager Sources => sourceManager;

    internal Config Config => config;

    internal ToolStripMenuItem? AccountMenu => _accountMenu;

    internal ToolStripStatusLabel? UserStatusLabel => _userStatusLabel;

    internal TabPage SystemTab => tabSystem;

    internal TabPage ProcessTab => tabProcess;

    internal TabPage GraphsTab => tabGraphs;

    internal TabPage LogTab => tabLog;

    internal IReadOnlyList<(Control Parent, Control Panel)> DynamicPanels => _dynamicPanels;

    private void RunModules(string hook, Action<IControlModule> call)
    {
        foreach (var module in ControlApp.Edition.Modules)
        {
            try { call(module); }
            catch (Exception ex)
            {
                var msg = $"Module {module.GetType().Name}.{hook} failed: {ex.Message}";
                System.Diagnostics.Trace.TraceWarning(msg);
                PublishEvent("Modules", msg, LogEventLevel.Operator);
            }
        }
    }

    private void CompleteBoot() => CompleteBoot(this, EventLogBus.MarkBootComplete, RunModules);

    internal static void CompleteBoot(
        FrmMain form,
        Action openBootGate,
        Action<string, Action<IControlModule>> runModules)
    {
        openBootGate();
        runModules(nameof(IControlModule.OnBootComplete), m => m.OnBootComplete(form));
    }
}
