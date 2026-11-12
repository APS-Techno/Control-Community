using APS.Controls.Theme;
using APS.SystemControl.Accounts;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Forms;

/// <summary>
/// Modeless runtime override form. Lets an engineer/operator disable
/// individual alarm rules and interlocks at runtime, with session or
/// persist semantics. Every toggle is audit-logged via the registry.
/// </summary>
internal sealed partial class frmOverrides : Form
{
    private readonly Core.Core core;
    private readonly OverrideRegistry registry;
    private readonly string overridesFilePath;

    private sealed record AlarmRow(string Id, string Label);
    private sealed record InterlockRow(string Id, string Label);

    private readonly List<AlarmRow> alarmRows = new();
    private readonly List<InterlockRow> interlockRows = new();

    // Stored so Dispose() can unsubscribe — otherwise the registry retains a
    // dead reference to this form, and a reopened form stacks a second handler
    // that BeginInvokes onto a disposed window on the next OverridesChanged.
    private readonly EventHandler _onOverridesChanged;
    private HintManager _hints = null!;

    public frmOverrides(Core.Core core, string overridesFilePath)
    {
        this.core = core;
        this.registry = core.Overrides
            ?? throw new InvalidOperationException("Core.Overrides not initialized");
        this.overridesFilePath = overridesFilePath;

        InitializeComponent();
        UIScaling.ApplyToForm(this);
        BuildRows();
        RefreshGrids();

        // BuildRows is single-shot; configs are immutable for the session
        // (no hot-reload path exists in Control today).

        filterBox.TextChanged += (_, _) => RefreshGrids();
        gridAlarms.CellValueChanged += OnAlarmCellChanged;
        gridInterlocks.CellValueChanged += OnInterlockCellChanged;
        clearAllButton.Click += OnClearAllClicked;

        _onOverridesChanged = (_, _) =>
        {
            if (InvokeRequired) BeginInvoke(RefreshGrids);
            else RefreshGrids();
        };
        registry.OverridesChanged += _onOverridesChanged;

        tabControl.ApplyPageTheme();
        DarkModeHelper.FixControls(this);
        LoadSavedSize();

        _hints = new HintManager(this);
        _hints.Set(filterBox,      "Filter alarm/interlock rules by label or reason.");
        _hints.Set(gridAlarms,     "Tick Disabled to override (bypass) an alarm. Persist keeps it across restarts; Reason is audit-logged. Overrides bypass safety — use with care.");
        _hints.Set(gridInterlocks, "Tick Disabled to override (bypass) an interlock. Persist keeps it across restarts; Reason is audit-logged. Overrides bypass safety — use with care.");
        _hints.Set(clearAllButton, "Re-enable every overridden alarm and interlock at once.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        registry.OverridesChanged -= _onOverridesChanged;
        SaveSize();
        _hints?.Dispose();
        base.OnFormClosed(e);
    }

    // Size persistence — matches the local-file pattern used by frmMain for
    // theme.config. A single "WxH" line next to the exe.
    private static string SizeConfigPath
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frmOverrides.size");

    private void LoadSavedSize()
    {
        try
        {
            if (!File.Exists(SizeConfigPath)) return;
            var parts = File.ReadAllText(SizeConfigPath).Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h)
                && w >= 500 && h >= 400 && w < 4000 && h < 3000)
            {
                Width  = w;
                Height = h;
            }
        }
        catch { /* ignore; keep designer defaults */ }
    }

    private void SaveSize()
    {
        try { File.WriteAllText(SizeConfigPath, $"{Width}x{Height}"); }
        catch { /* best effort */ }
    }

    private void BuildRows()
    {
        alarmRows.Clear();
        foreach (var alarms in core.chamberAlarms.Values)
        {
            foreach (var a in alarms.Rules)
            {
                alarmRows.Add(new AlarmRow(
                    OverrideIds.Alarm(alarms.ChamberId, a.Sensor, a.State, a.Condition, a.Value),
                    OverrideIds.AlarmLabel(alarms.ChamberId, a.Message, a.Sensor, a.State, a.Condition, a.Value)));
            }
        }

        interlockRows.Clear();
        foreach (var kvp in core.Interlocks.GetAllInterlocks())
        {
            interlockRows.Add(new InterlockRow(
                kvp.Key,
                OverrideIds.InterlockLabel(kvp.Key, kvp.Value)));
        }
    }

    private bool suppressToggle;

    private void RefreshGrids()
    {
        suppressToggle = true;
        try
        {
            string filter = filterBox.Text?.Trim() ?? "";

            var visibleAlarms = alarmRows
                .Where(r => Match(filter, r.Label, ReasonFor(true, r.Id)))
                .ToList();
            FillAlarmGrid(visibleAlarms);

            var visibleInterlocks = interlockRows
                .Where(r => Match(filter, r.Label, ReasonFor(false, r.Id)))
                .ToList();
            FillInterlockGrid(visibleInterlocks);

            int alarmOverridden     = alarmRows.Count(r => registry.IsAlarmOverridden(r.Id));
            int interlockOverridden = interlockRows.Count(r => registry.IsInterlockOverridden(r.Id));
            tabAlarms.Text     = $"Alarms ({alarmOverridden} overridden / {alarmRows.Count} total)";
            tabInterlocks.Text = $"Interlocks ({interlockOverridden} overridden / {interlockRows.Count} total)";
            Text = $"Alarm & Interlock Overrides — {registry.ActiveCount} active";
        }
        finally
        {
            suppressToggle = false;
        }
    }

    private void FillAlarmGrid(List<AlarmRow> rows)
    {
        gridAlarms.Rows.Clear();
        foreach (var row in rows)
        {
            bool overridden = registry.IsAlarmOverridden(row.Id);
            OverrideEntry? entry = overridden ? registry.PeekAlarm(row.Id) : null;
            int idx = gridAlarms.Rows.Add(
                overridden,
                entry?.Persist ?? false,
                row.Label,
                entry?.Reason ?? "",
                entry?.DisabledAt.ToString("s") ?? "",
                entry?.DisabledBy ?? "");
            gridAlarms.Rows[idx].Tag = row.Id;
            if (overridden)
            {
                gridAlarms.Rows[idx].DefaultCellStyle.BackColor = Color.MistyRose;
                if (string.IsNullOrWhiteSpace(entry?.Reason))
                    gridAlarms.Rows[idx].Cells["Reason"].Style.BackColor = Color.LightYellow;
            }
        }
    }

    private void FillInterlockGrid(List<InterlockRow> rows)
    {
        gridInterlocks.Rows.Clear();
        foreach (var row in rows)
        {
            bool overridden = registry.IsInterlockOverridden(row.Id);
            OverrideEntry? entry = overridden ? registry.PeekInterlock(row.Id) : null;
            int idx = gridInterlocks.Rows.Add(
                overridden,
                entry?.Persist ?? false,
                row.Label,
                entry?.Reason ?? "",
                entry?.DisabledAt.ToString("s") ?? "",
                entry?.DisabledBy ?? "");
            gridInterlocks.Rows[idx].Tag = row.Id;
            if (overridden)
            {
                gridInterlocks.Rows[idx].DefaultCellStyle.BackColor = Color.MistyRose;
                if (string.IsNullOrWhiteSpace(entry?.Reason))
                    gridInterlocks.Rows[idx].Cells["Reason"].Style.BackColor = Color.LightYellow;
            }
        }
    }

    private static bool Match(string filter, params string[] fields)
        => string.IsNullOrEmpty(filter)
           || fields.Any(f => f != null && f.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private string ReasonFor(bool isAlarm, string id)
    {
        var entry = isAlarm ? registry.PeekAlarm(id) : registry.PeekInterlock(id);
        return entry?.Reason ?? "";
    }

    private void OnAlarmCellChanged(object? sender, DataGridViewCellEventArgs e)
        => HandleToggle(gridAlarms, e, isAlarm: true);

    private void OnInterlockCellChanged(object? sender, DataGridViewCellEventArgs e)
        => HandleToggle(gridInterlocks, e, isAlarm: false);

    private void HandleToggle(DataGridView grid, DataGridViewCellEventArgs e, bool isAlarm)
    {
        if (suppressToggle || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string col = grid.Columns[e.ColumnIndex].Name;
        if (col != "Disabled" && col != "Persist" && col != "Reason") return;

        var row = grid.Rows[e.RowIndex];
        string id = (string)row.Tag!;
        string label = (row.Cells["Label"].Value as string) ?? id;
        bool wantDisabled = Convert.ToBoolean(row.Cells["Disabled"].Value ?? false);

        // Re-enabling is always allowed without confirmation.
        if (!wantDisabled)
        {
            ApplyToggle(id, false, false, "", isAlarm);
            return;
        }

        // Non-Disabled columns (Persist, Reason) toggled on an already-disabled row:
        // just re-apply with the new values, no confirmation.
        if (col != "Disabled")
        {
            bool curPersist = Convert.ToBoolean(row.Cells["Persist"].Value ?? false);
            string curReason = (row.Cells["Reason"].Value as string) ?? "";
            ApplyToggle(id, true, curPersist, curReason, isAlarm);
            return;
        }

        using var dlg = new frmOverrideConfirm(label);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            // User cancelled — revert the checkbox without re-triggering the handler.
            suppressToggle = true;
            row.Cells["Disabled"].Value = false;
            suppressToggle = false;
            return;
        }

        ApplyToggle(id, true, dlg.Persist, dlg.Reason, isAlarm);
    }

    /// <summary>
    /// Whether RuntimeOverrides.xml must be rewritten after a toggle.
    /// Session-only overrides skip the disk write — unless the entry was
    /// previously persisted, in which case Save must run to REMOVE it.
    /// </summary>
    internal static bool SaveNeeded(bool disabled, bool persist, bool previousPersisted)
        => (disabled && persist) || !disabled || previousPersisted;

    private void ApplyToggle(string id, bool disabled, bool persist, string reason, bool isAlarm)
    {
        // Snapshot the current persisted state so we can roll back if save fails.
        // Presence of an entry means the rule is currently overridden; null means enabled.
        var previous = isAlarm ? registry.PeekAlarm(id) : registry.PeekInterlock(id);

        if (isAlarm) registry.SetAlarmOverride(id, disabled, persist, reason, CurrentUser.Name);
        else         registry.SetInterlockOverride(id, disabled, persist, reason, CurrentUser.Name);

        if (!SaveNeeded(disabled, persist, previous?.Persist == true)) return;

        try { registry.Save(overridesFilePath); }
        catch (Exception ex)
        {
            // Roll in-memory state back to what is actually on disk.
            if (previous is null)
            {
                if (isAlarm) registry.SetAlarmOverride(id, false, false, "", CurrentUser.Name);
                else         registry.SetInterlockOverride(id, false, false, "", CurrentUser.Name);
            }
            else
            {
                if (isAlarm) registry.SetAlarmOverride(id, true, previous.Persist, previous.Reason, CurrentUser.Name);
                else         registry.SetInterlockOverride(id, true, previous.Persist, previous.Reason, CurrentUser.Name);
            }
            RefreshGrids();
            ThemedMessageBox.Show(this, "Failed to persist override:\n" + ex.Message +
                "\n\nIn-memory state has been reverted to match the file on disk.",
                "Persist failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnClearAllClicked(object? sender, EventArgs e)
    {
        if (ThemedMessageBox.Show(this, "Clear all overrides?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        registry.ClearAll(CurrentUser.Name);
        TrySave();
    }

    private void TrySave()
    {
        try { registry.Save(overridesFilePath); }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, "Failed to persist overrides:\n" + ex.Message,
                "Persist failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
