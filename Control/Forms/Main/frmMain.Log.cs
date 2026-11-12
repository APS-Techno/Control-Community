using APS.Controls.Theme;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Logging;

namespace APS.SystemControl;

public class LogEvent
{
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? Sender { get; set; }
    public string? Message { get; set; }
}


public partial class FrmMain : Form
{
    private void ConfigureLogGrid()
    {
        logGridView.AutoGenerateColumns = false;
        logGridView.DataSource = events;

        logGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Date", HeaderText = "Date" });
        logGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Time", HeaderText = "Time" });
        logGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Sender", HeaderText = "Sender" });
        var messageColumn = new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Message",
            HeaderText = "Message",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        };
        logGridView.Columns.Add(messageColumn);

        WrapLogGridInDarkScrollHost();

        EventLogBus.Entry += OnBusEntryForGrid;
        button5.Click += (_, _) => SaveEventLog();
    }

    private void SaveEventLog()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save event log",
            Filter = "Tab-separated values (*.tsv)|*.tsv|Comma-separated values (*.csv)|*.csv",
            FileName = "events_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var rows = events.Select(static e => (e.Date, e.Time, e.Sender, e.Message));
            EventLogExport.Write(dialog.FileName, rows, csv: dialog.FilterIndex == 2);
            ThemedMessageBox.Show(this, "Saved:\n" + dialog.FileName, "Event log",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"Could not save the event log: {ex.Message}", "Event log",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void WrapLogGridInDarkScrollHost()
    {
        if (logGridView.Parent is not TableLayoutPanel tlp) return;
        int col = tlp.GetColumn(logGridView);
        int row = tlp.GetRow(logGridView);
        int colSpan = tlp.GetColumnSpan(logGridView);
        int rowSpan = tlp.GetRowSpan(logGridView);

        tlp.Controls.Remove(logGridView);
        var host = new DarkScrollHost(logGridView) { Dock = DockStyle.Fill };
        tlp.Controls.Add(host, col, row);
        if (colSpan > 1) tlp.SetColumnSpan(host, colSpan);
        if (rowSpan > 1) tlp.SetRowSpan(host, rowSpan);
    }

    private void OnBusEntryForGrid(EventLogEntry entry)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => OnBusEntryForGrid(entry));
            return;
        }
        events.Add(new LogEvent
        {
            Date = entry.Timestamp.ToString("yyyy-MM-dd"),
            Time = entry.Timestamp.ToString("HH:mm:ss"),
            Sender = entry.Source,
            Message = entry.Message,
        });

        // Bound the grid like every sibling sink (EventsLogControl trims at 2000): an
        // uncapped list grows memory indefinitely on a 24/7 deposition controller and slows
        // grid redraw. Full history still persists in LogThread's events_*.log. (audit M-8)
        while (events.Count > MaxLogRows)
            events.RemoveAt(0);
    }

    /// <summary>Cap on the in-memory log-grid backing list (full history on disk).</summary>
    private const int MaxLogRows = 5000;

    /// <summary>
    /// Publishes to the bus; the grid is populated by <see cref="OnBusEntryForGrid"/>.
    /// </summary>
    private void DisplayLogMessage(string device, string message)
        => EventLogBus.PublishClassified(device, message);

    private void DisplayLogMessage(ThreadMsg msg)
        => DisplayLogMessage(msg.Device, ((ValMsg<string>)msg).Value);
}
