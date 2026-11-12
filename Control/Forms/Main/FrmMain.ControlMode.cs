using APS.Accounts.Domain;
using APS.Controls.Theme;
using APS.SystemControl.Accounts;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Core.Remote.Agent;
using APS.SystemControl.Forms;

namespace APS.SystemControl;

/// <summary>Control mode (Offline / Local / Remote) on the main window — spec
/// docs/superpowers/specs/2026-09-06-control-mode-design.md §4.2, §5. Startup resolution, the
/// status-strip indicator, the Service menu item, and the single helper every operator-originated
/// command send goes through.</summary>
public partial class FrmMain
{
    private ToolStripStatusLabel? _modeLabel;
    private ToolStripMenuItem? _controlModeItem;

    /// <summary>Resolve the startup mode (spec §3/§7), hook UI refresh, add the indicator and the
    /// Service menu item. Call from FrmMain_Load BEFORE StartAgentServer (the dispatcher closes over
    /// the service) and AFTER the status strip exists.</summary>
    private void InitControlMode()
    {
        var core = systemThread?.Core;
        if (core is null) return;
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var cfg = AgentServerConfig.Load(Path.Combine(baseDir, "AgentServer.xml"));   // defaults when absent
        core.ControlMode.Initialize(new ControlModeStartup(
            Path.Combine(baseDir, "ControlMode.state"),
            cfg.RestoreRemote,
            Environment.GetEnvironmentVariable("APS_DEV_CONTROL_MODE"),
            LogControlMode));
        core.ControlMode.Changed += (_, _) => ExecuteOnUIThread(RefreshControlModeUi);

        _modeLabel = new ToolStripStatusLabel
        {
            Name = "lblControlMode",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Margin = new Padding(6, 0, 6, 0),
            ToolTipText = "Control mode — click to change",
        };
        _modeLabel.Click += (_, _) => ShowControlModeDialog();
        statusStrip1.ShowItemToolTips = true;
        statusStrip1.Items.Insert(Math.Min(1, statusStrip1.Items.Count), _modeLabel);   // right of the HELD banner

        _controlModeItem = new ToolStripMenuItem("Control mode…") { Name = "controlModeToolStripMenuItem" };
        _controlModeItem.Click += (_, _) => ShowControlModeDialog();
        serviceToolStripMenuItem.DropDownItems.Insert(0, _controlModeItem);

        RefreshControlModeUi();
    }

    private void RefreshControlModeUi()
    {
        var svc = systemThread?.Core?.ControlMode;
        if (svc is null || _modeLabel is null) return;
        (_modeLabel.Text, _modeLabel.ForeColor) = svc.Mode switch
        {
            ControlMode.Remote  => ("REMOTE — panels locked", Color.FromArgb(255, 183, 77)),
            ControlMode.Offline => ("OFFLINE", VacuumTheme.TextDim),
            _                   => ("LOCAL", Color.FromArgb(200, 214, 229)),
        };
        if (svc.LastChange is { } c)
            _modeLabel.ToolTipText = $"Control mode {c.To} since {c.Utc.ToLocalTime():HH:mm:ss} by {c.By} — click to change";
        ApplyRoleGating();                       // Run / Start follow the mode too
    }

    private void ShowControlModeDialog()
    {
        var svc = systemThread?.Core?.ControlMode;
        if (svc is null) return;
        using var dlg = new frmControlMode(svc, CurrentUser.Can(Permission.ServiceMenu),
            CurrentUser.Session?.DisplayName ?? "operator");
        dlg.ShowDialog(this);
    }

    /// <summary>Service/adapter lines already carry a "[ControlMode]" tag that the event log would
    /// repeat as the source column; strip it so the line reads once.</summary>
    internal void LogControlMode(string msg) =>
        PublishEvent("ControlMode", msg.StartsWith("[ControlMode] ", StringComparison.Ordinal) ? msg[14..] : msg,
            LogEventLevel.Operator);

    /// <summary>Every operator-originated command send. Refused (logged + toast) while the control
    /// mode is Remote unless <paramref name="cls"/> is Safety. Disposes a refused message.</summary>
    private void SendOperatorCommand(ThreadMsg msg, CommandClass cls = CommandClass.Mutate)
    {
        string? deny = systemThread?.Core?.ControlMode.Authorize(CommandOrigin.Operator, cls);
        if (deny is not null)
        {
            LogControlMode($"Refused {msg.Target} {msg.Device}: {deny}");
            ShowErrorToast(deny);
            msg.Dispose();
            return;
        }
        pool!.WriteHW(msg);
    }

    /// <summary>Operator-origin gate handed to the XML-menu adapter and the Stream Deck pipe server.</summary>
    internal Func<CommandClass, string?> OperatorAuthorize =>
        cls => systemThread?.Core?.ControlMode.Authorize(CommandOrigin.Operator, cls);
}
