using APS.Controls.Theme;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Forms.Accounts;

/// <summary>Read-only viewer for the hash-chained audit log (gated by ViewAuditLog).</summary>
internal sealed class frmAuditLog : Form
{
    private readonly string _auditDir;
    private readonly DarkComboBox _dayPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _filter = new();
    private readonly Button _verify = new() { Text = "Verify integrity" };
    private readonly Label _verifyResult = new() { AutoSize = true };
    private readonly DataGridView _grid = new();
    private IReadOnlyList<AuditEvent> _day = System.Array.Empty<AuditEvent>();
    private HintManager _hints = null!;

    public frmAuditLog(string auditDir)
    {
        _auditDir = auditDir;
        Text = "Audit Log — APS System Control";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(DpiScale.Px(820), DpiScale.Px(560));
        MinimumSize = new Size(DpiScale.Px(640), DpiScale.Px(400));

        var lblDay = new Label { Text = "Day", AutoSize = true, Location = P(12, 14) };
        _dayPicker.Location = P(44, 10); _dayPicker.Size = new Size(DpiScale.Px(140), DpiScale.Px(26));
        _dayPicker.SelectedIndexChanged += (_, _) => LoadDay();

        var lblFilter = new Label { Text = "Filter", AutoSize = true, Location = P(200, 14) };
        _filter.Location = P(244, 10); _filter.Size = new Size(DpiScale.Px(240), DpiScale.Px(26));
        _filter.TextChanged += (_, _) => Render();

        _verify.Location = P(500, 9); _verify.Size = new Size(DpiScale.Px(130), DpiScale.Px(28));
        _verify.Click += (_, _) => DoVerify();
        _verifyResult.Location = P(640, 14); _verifyResult.MaximumSize = new Size(DpiScale.Px(170), 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false; _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.FromArgb(30, 36, 46);
        _grid.Columns.Add("Time", "Time");
        _grid.Columns.Add("User", "User");
        _grid.Columns.Add("Event", "Event");
        _grid.Columns.Add("Details", "Details");
        _grid.Columns["Time"]!.FillWeight = 60;
        _grid.Columns["User"]!.FillWeight = 50;
        _grid.Columns["Event"]!.FillWeight = 60;
        _grid.Columns["Details"]!.FillWeight = 130;

        var top = new Panel { Dock = DockStyle.Top, Height = DpiScale.Px(46) };
        top.Controls.AddRange(new Control[] { lblDay, _dayPicker, lblFilter, _filter, _verify, _verifyResult });

        Controls.Add(_grid);
        Controls.Add(top);
        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(_dayPicker, "Choose which day's audit log to view (newest first).");
        _hints.Set(_filter,    "Filter rows by user, event, or details text.");
        _hints.Set(_verify,    "Re-check the audit log's hash chain to confirm it hasn't been tampered with.");

        ReloadDays();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    private static Point P(int x, int y) => new(DpiScale.Px(x), DpiScale.Px(y));

    private void ReloadDays()
    {
        try
        {
            _dayPicker.Items.Clear();
            foreach (var d in AuditLogReader.ListDays(_auditDir)) _dayPicker.Items.Add(d);
            if (_dayPicker.Items.Count > 0) _dayPicker.SelectedIndex = 0;   // newest → triggers LoadDay
            else { _day = System.Array.Empty<AuditEvent>(); Render(); }
        }
        catch (Exception ex) { Warn(ex); }
    }

    private void LoadDay()
    {
        try
        {
            _day = _dayPicker.SelectedItem is string day ? AuditLogReader.ReadDay(_auditDir, day) : System.Array.Empty<AuditEvent>();
            Render();
        }
        catch (Exception ex) { Warn(ex); }
    }

    private void Render()
    {
        var f = _filter.Text.Trim();
        _grid.Rows.Clear();
        foreach (var e in _day)
        {
            var time = FormatTime(e.Ts);
            var details = Details(e);
            if (f.Length > 0 &&
                !(Contains(e.User, f) || Contains(e.Event, f) || Contains(details, f))) continue;
            _grid.Rows.Add(time, e.User, e.Event, details);
        }
    }

    private void DoVerify()
    {
        try
        {
            var r = AuditLogReader.VerifyAll(_auditDir);
            if (r.Ok)
            {
                _verifyResult.ForeColor = Color.FromArgb(129, 199, 132);
                _verifyResult.Text = $"Chain verified ({r.RecordsChecked} records)";
            }
            else
            {
                _verifyResult.ForeColor = Color.FromArgb(229, 115, 115);
                _verifyResult.Text = $"BROKEN at record {r.BrokenAt}: {r.Reason}";
            }
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static bool Contains(string? s, string f) =>
        s is not null && s.Contains(f, StringComparison.OrdinalIgnoreCase);

    private static string FormatTime(string ts) =>
        DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("HH:mm:ss") : ts;

    private static string Details(AuditEvent e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(e.Kind)) parts.Add(e.Kind!);
        if (!string.IsNullOrEmpty(e.Id)) parts.Add(e.Id!);
        if (!string.IsNullOrEmpty(e.State)) parts.Add("→ " + e.State);
        if (!string.IsNullOrEmpty(e.Reason)) parts.Add(e.Reason);
        if (!string.IsNullOrEmpty(e.Message)) parts.Add(e.Message!);
        return string.Join(" ", parts);
    }

    private void Warn(Exception ex) =>
        ThemedMessageBox.Show(this, "Could not read the audit log: " + ex.Message, "Audit Log",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
