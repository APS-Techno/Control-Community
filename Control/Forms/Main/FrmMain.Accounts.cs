using APS.Accounts.Domain;
using APS.Icons;
using APS.SystemControl.Accounts;
using APS.SystemControl.Core.Menus;
using APS.Controls.Theme;
using APS.SystemControl.Forms.Accounts;

namespace APS.SystemControl;

/// <summary>
/// In Standalone mode the local admin holds every permission, so role-gating is a no-op
/// and the UI is unchanged.
/// </summary>
public partial class FrmMain
{
    private MenuComposer? _menuComposer;
    private ToolStripStatusLabel? _userStatusLabel;
    private ToolStripMenuItem? _accountMenu;
    private ToolStripMenuItem? _auditLogItem;

    // Theme text colour (#C8D6E5) so the status label is readable on the dark strip.
    private static readonly Color UserLabelColor = Color.FromArgb(200, 214, 229);

    private void InitializeAccountsUi()
    {
        var sessions = CurrentUser.Sessions;
        if (sessions is null) return;

        _userStatusLabel = new ToolStripStatusLabel { ForeColor = UserLabelColor };
        statusStrip1.Items.Add(_userStatusLabel);

        _accountMenu = new ToolStripMenuItem("Account");
        _auditLogItem = new ToolStripMenuItem("Audit Log…") { Image = IconLibrary.Get(IconName.AuditLog, 16) };
        _auditLogItem.Click += (_, _) => OpenAuditLog();
        _accountMenu.DropDownItems.Add(_auditLogItem);

        int fileIdx = menuStrip1.Items.IndexOf(fileToolStripMenuItem);
        if (fileIdx >= 0) menuStrip1.Items.Insert(fileIdx + 1, _accountMenu);
        else menuStrip1.Items.Add(_accountMenu);

        sessions.CurrentChanged += (_, _) =>
        {
            if (IsDisposed || Disposing) return;   // drop late events during teardown
            if (InvokeRequired) BeginInvoke(OnSessionChanged);
            else OnSessionChanged();
        };

        OnSessionChanged();
    }

    private void OnSessionChanged()
    {
        UpdateUserStatusLabel();
        ApplyRoleGating();
    }

    /// <summary>Called on every system-state change: pause metering during a run and resume after.</summary>
    private void OnRunStateChangedForQuota()
    {
        var sessions = CurrentUser.Sessions;
        if (sessions is null) return;

        if (systemThread?.Core?.IsRecipeRunning == true)
            sessions.PauseQuota();
        else
            sessions.ResumeQuota();
    }

    private void UpdateUserStatusLabel()
    {
        if (_userStatusLabel is null) return;
        var session = CurrentUser.Session;
        _userStatusLabel.Text = session is null
            ? "Not signed in"
            : $"User: {session.DisplayName}{(session.IsOffline ? " (offline)" : string.Empty)}";
    }

    /// <summary>
    /// Also re-evaluates contributed (plugin) menu items. Called on load and whenever the
    /// current user changes (switch-user / logout).
    /// </summary>
    private void ApplyRoleGating()
    {
        serviceToolStripMenuItem.Visible   = CurrentUser.Can(Permission.ServiceMenu);
        overridesToolStripMenuItem.Enabled = CurrentUser.Can(Permission.SafetyOverride);
        settingsToolStripMenuItem.Enabled  = CurrentUser.Can(Permission.ManageConfig);
        // Online-Remote hands the run to the host: Run / Start are the operator's, so they lock too
        // (spec 2026-09-06 §4.4). Abort stays enabled — it is a safety action.
        bool remote = systemThread?.Core?.ControlMode.IsRemote ?? false;
        runToolStripMenuItem.Enabled       = CurrentUser.Can(Permission.RunRecipe) && !remote;

        if (btnStart is not null) btnStart.Enabled = CurrentUser.Can(Permission.RunRecipe) && !remote;
        if (btnEdit is not null)  btnEdit.Enabled  = CurrentUser.Can(Permission.EditRecipe);
        if (_auditLogItem is not null) _auditLogItem.Enabled = CurrentUser.Can(Permission.ViewAuditLog);

        _menuComposer?.Refresh();
    }

    private void OpenAuditLog()
    {
        var dir = systemThread?.Core?.AuditLogDirectory;
        if (string.IsNullOrEmpty(dir))
        {
            ThemedMessageBox.Show(this, "Audit log is not available yet.", "Audit Log", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new frmAuditLog(dir);
        f.ShowDialog(this);
    }
}
