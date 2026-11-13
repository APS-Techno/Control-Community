using System.IO;
using APS.Controls.Theme;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;

namespace APS.SystemControl.Forms;

public partial class frmAppSettings : Form
{
    private readonly AppConfig _editing;
    private bool _dirty = false;
    private HintManager _hints = null!;
    private readonly Action<bool, bool>? _onRecipeLogChanged;

    public frmAppSettings(AppConfig snapshot, Action<bool, bool>? onRecipeLogChanged = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _editing = snapshot;
        _onRecipeLogChanged = onRecipeLogChanged;

        InitializeComponent();
        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(txtConfigDir,   "Root folder that holds the per-project config folders.");
        _hints.Set(cboPrefix,      "Active project — the config folder name; must contain a Config.xml.");
        _hints.Set(txtWorkDir,     "Working folder where recipes and logs are read/written.");
        _hints.Set(txtHWDir,       "Folder holding the hardware driver libraries (plug-ins).");
        _hints.Set(txtGUIDir,      "Folder holding the machine-specific GUI panel libraries (plug-ins).");
        _hints.Set(txtRecipesDir,  "Folder the operator Load-Recipe dialog opens in. Blank = ConfigDir/Recipes.");
        _hints.Set(txtLogsDir,     "Root folder for dated text logs (events, process). Blank = WorkDir/Logs.");
        _hints.Set(txtDataDir,     "Root folder for TSV data files and .depo.json deposition snapshots. Blank = WorkDir/Data.");
        _hints.Set(chkLogUserRecipes,
            "Write the TSV data-log and step-by-step log for operator .recipe/.rcpx runs.");
        _hints.Set(chkLogEmbeddedRecipes,
            "Write the TSV data-log and step log for built-in recipes (SYSTEM_START, PUMP_OUT, " +
            "LOAD_SUBSTRATE…). Off keeps the Data folder from filling with routine housekeeping " +
            "runs; the one-line started/complete event lines still appear.");

        LoadFromConfig();

        txtConfigDir.TextChanged += OnAnyChanged;
        cboPrefix.TextChanged += OnAnyChanged;
        txtWorkDir.TextChanged += OnAnyChanged;
        txtHWDir.TextChanged += OnAnyChanged;
        txtGUIDir.TextChanged += OnAnyChanged;
        txtRecipesDir.TextChanged += OnAnyChanged;
        txtLogsDir.TextChanged += OnAnyChanged;
        txtDataDir.TextChanged += OnAnyChanged;
        chkLogUserRecipes.CheckedChanged += OnAnyChanged;
        chkLogEmbeddedRecipes.CheckedChanged += OnAnyChanged;

        txtConfigDir.Leave += (_, _) => RescanAndUpdatePrefixStatus();
        cboPrefix.TextChanged += (_, _) => UpdatePrefixStatus();

        btnBrowseConfigDir.Click += (_, _) => BrowseInto(txtConfigDir);
        btnBrowseWorkDir.Click += (_, _) => BrowseInto(txtWorkDir);
        btnBrowseHWDir.Click += (_, _) => BrowseInto(txtHWDir);
        btnBrowseGUIDir.Click += (_, _) => BrowseInto(txtGUIDir);
        btnBrowseRecipesDir.Click += (_, _) => BrowseInto(txtRecipesDir);
        btnBrowseLogsDir.Click += (_, _) => BrowseInto(txtLogsDir);
        btnBrowseDataDir.Click += (_, _) => BrowseInto(txtDataDir);

        btnOK.Click += (_, _) =>
        {
            if (TrySave()) DialogResult = DialogResult.OK;
        };
        btnApply.Click += (_, _) => TrySave();

        RescanAndUpdatePrefixStatus();

        // Suppress dirty noise from the initial cboPrefix population:
        // RescanAndUpdatePrefixStatus clears + repopulates cboPrefix.Items, which
        // fires TextChanged (and thus OnAnyChanged) before the user has touched
        // anything. Reset the dirty state to match the actual user-touched state.
        _dirty = false;
        btnApply.Enabled = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hints?.Dispose();
        base.OnFormClosed(e);
    }

    private void LoadFromConfig()
    {
        txtConfigDir.Text = _editing.ConfigDir ?? string.Empty;
        cboPrefix.Text = _editing.Prefix ?? string.Empty;
        txtWorkDir.Text = _editing.WorkDir ?? string.Empty;
        txtHWDir.Text = _editing.HWDir ?? string.Empty;
        txtGUIDir.Text = _editing.GUIDir ?? string.Empty;
        txtRecipesDir.Text = _editing.RecipesDir ?? string.Empty;
        txtLogsDir.Text = _editing.LogsDir ?? string.Empty;
        txtDataDir.Text = _editing.DataDir ?? string.Empty;
        chkLogUserRecipes.Checked = _editing.LogUserRecipes;
        chkLogEmbeddedRecipes.Checked = _editing.LogEmbeddedRecipes;
    }

    private void OnAnyChanged(object? sender, EventArgs e)
    {
        _dirty = true;
        btnApply.Enabled = _dirty;
    }

    private void RescanAndUpdatePrefixStatus()
    {
        var typed = cboPrefix.Text;
        cboPrefix.BeginUpdate();
        try
        {
            cboPrefix.Items.Clear();
            foreach (var name in AppSettingsProjectScanner.ScanProjects(ResolvedConfigDir()))
            {
                cboPrefix.Items.Add(name);
            }
        }
        finally
        {
            cboPrefix.EndUpdate();
        }
        cboPrefix.Text = typed;
        UpdatePrefixStatus();
    }

    private void UpdatePrefixStatus()
    {
        var dir = ResolvedConfigDir();
        var prefix = cboPrefix.Text;
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(prefix))
        {
            lblPrefixStatus.Text = string.Empty;
            return;
        }

        var configXml = Path.Combine(dir, prefix, "Config.xml");
        if (File.Exists(configXml))
        {
            lblPrefixStatus.Text = "✓ valid project";
            lblPrefixStatus.ForeColor = Color.FromArgb(80, 180, 80);
        }
        else
        {
            lblPrefixStatus.Text = "⚠ Config.xml not found";
            lblPrefixStatus.ForeColor = Color.FromArgb(220, 170, 60);
        }
    }

    private string ResolvedConfigDir() => Config.ResolvePath(txtConfigDir.Text, txtWorkDir.Text);

    private void BrowseInto(TextBox target)
    {
        string text = target == txtConfigDir ? ResolvedConfigDir() : target.Text;
        using var dlg = new FolderBrowserDialog
        {
            InitialDirectory = string.IsNullOrWhiteSpace(text)
                ? AppDomain.CurrentDomain.BaseDirectory
                : text,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dlg.SelectedPath;
        }
    }

    private bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(cboPrefix.Text))
        {
            ThemedMessageBox.Show(this,
                "Project (Prefix) must not be blank.",
                "Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            cboPrefix.Focus();
            return false;
        }

        var cfg = new AppConfig
        {
            ConfigDir = NullIfBlank(txtConfigDir.Text),
            Prefix = NullIfBlank(cboPrefix.Text),
            WorkDir = NullIfBlank(txtWorkDir.Text),
            HWDir = NullIfBlank(txtHWDir.Text),
            GUIDir = NullIfBlank(txtGUIDir.Text),
            // The dialog has no WorkerDir field; carry the existing value through
            // unchanged so saving App Settings never blanks <WorkerDir> (a blank
            // value sends the worker loader to bin\ instead of bin\Workers).
            WorkerDir = _editing.WorkerDir,
            RecipesDir = NullIfBlank(txtRecipesDir.Text),
            LogsDir = NullIfBlank(txtLogsDir.Text),
            DataDir = NullIfBlank(txtDataDir.Text),
            UIScale = _editing.UIScale,
            LogUserRecipes = chkLogUserRecipes.Checked,
            LogEmbeddedRecipes = chkLogEmbeddedRecipes.Checked,
            ExtraElements = _editing.ExtraElements,
        };

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RootConfig.xml");
        try
        {
            Config.SaveRootConfig(cfg, path);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this,
                "Failed to save RootConfig.xml:\n" + ex.Message,
                "Save failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        _dirty = false;
        btnApply.Enabled = false;
        // Apply the log toggles to the running Core immediately (no restart needed).
        _onRecipeLogChanged?.Invoke(chkLogUserRecipes.Checked, chkLogEmbeddedRecipes.Checked);
        if (RestartRequiringFieldChanged(cfg))
            OfferRestart();
        return true;
    }

    /// <summary>
    /// True when a field that only takes effect on restart changed vs. the loaded
    /// snapshot. The two recipe-log toggles are excluded — they apply live — so a
    /// save that changed only those does not prompt for a restart.
    /// </summary>
    private bool RestartRequiringFieldChanged(AppConfig saved) =>
        saved.ConfigDir != _editing.ConfigDir ||
        saved.Prefix != _editing.Prefix ||
        saved.WorkDir != _editing.WorkDir ||
        saved.HWDir != _editing.HWDir ||
        saved.GUIDir != _editing.GUIDir ||
        saved.WorkerDir != _editing.WorkerDir ||
        saved.RecipesDir != _editing.RecipesDir ||
        saved.LogsDir != _editing.LogsDir ||
        saved.DataDir != _editing.DataDir;

    private void OfferRestart()
    {
        var result = ThemedMessageBox.Show(this,
            "Settings saved.\n\nRestart Control now to apply changes?",
            "Restart required",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            APS.SystemControl.Hosting.ControlApp.Restart();
        }
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
