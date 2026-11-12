using APS.Controls.Theme;
using APS.Icons;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.Forms;

namespace APS.SystemControl;

public partial class FrmMain
{
    private static readonly (string Text, int Ms)[] MeasurementIntervals =
        [("200 ms", 200), ("500 ms", 500), ("1 s", 1000), ("2 s", 2000), ("5 s", 5000)];

    private ToolStripMenuItem? _startLogItem;
    private ToolStripMenuItem? _stopLogItem;
    private ToolStripMenuItem? _logIntervalItem;
    private ToolStripButton? _btnStartLog;
    private ToolStripButton? _btnStopLog;
    private ToolStripStatusLabel? _lblMeasurementLog;
    private System.Windows.Forms.Timer? _measurementLogTimer;

    private void InitializeManualLoggingUi()
    {
        int menuPx = DpiScale.Px(16);
        int barPx = DpiScale.Px(24);

        toolsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

        _startLogItem = new ToolStripMenuItem
        {
            Name = "startMeasurementLogToolStripMenuItem",
            Text = "Start Measurement Log",
            Image = IconLibrary.Get(IconName.Run, menuPx),
            ToolTipText = "Record pressure, throttle valve, valve and pump states to a data file while you work by hand"
        };
        _startLogItem.Click += (_, _) => StartMeasurementLog();
        toolsToolStripMenuItem.DropDownItems.Add(_startLogItem);

        _stopLogItem = new ToolStripMenuItem
        {
            Name = "stopMeasurementLogToolStripMenuItem",
            Text = "Stop Measurement Log",
            Image = IconLibrary.Get(IconName.Stop, menuPx),
            ToolTipText = "Close the measurement data file"
        };
        _stopLogItem.Click += (_, _) => StopMeasurementLog();
        toolsToolStripMenuItem.DropDownItems.Add(_stopLogItem);

        _logIntervalItem = new ToolStripMenuItem
        {
            Name = "measurementLogIntervalToolStripMenuItem",
            Text = "Measurement Log Interval",
            Image = IconLibrary.Get(IconName.Tune, menuPx),
            ToolTipText = "Wall-clock time between rows. At simulation speed N one row spans interval x N simulated seconds"
        };
        foreach (var (text, ms) in MeasurementIntervals)
        {
            var choice = new ToolStripMenuItem(text) { Tag = ms, CheckOnClick = false };
            choice.Click += (_, _) =>
            {
                if (systemThread?.Core is { } core) core.ManualLogIntervalMs = ms;
                RefreshMeasurementLogUi();
            };
            _logIntervalItem.DropDownItems.Add(choice);
        }
        toolsToolStripMenuItem.DropDownItems.Add(_logIntervalItem);

        var openItem = new ToolStripMenuItem
        {
            Name = "openMeasurementLogToolStripMenuItem",
            Text = "Open Measurement Log in Graph Viewer",
            Image = IconLibrary.Get(IconName.GraphLog, menuPx),
            ToolTipText = "Plot the latest measurement data file"
        };
        openItem.Click += (_, _) => OpenMeasurementLog();
        toolsToolStripMenuItem.DropDownItems.Add(openItem);

        var saveItem = new ToolStripMenuItem
        {
            Name = "saveMeasurementDataToolStripMenuItem",
            Text = "Save Measurement Data As…",
            Image = IconLibrary.Get(IconName.SaveAs, menuPx),
            ToolTipText = "Export the latest measurement data as a TSV or CSV file"
        };
        saveItem.Click += (_, _) => SaveMeasurementData();
        toolsToolStripMenuItem.DropDownItems.Add(saveItem);

        var folderItem = new ToolStripMenuItem
        {
            Name = "openMeasurementFolderToolStripMenuItem",
            Text = "Open Measurement Data Folder",
            Image = IconLibrary.Get(IconName.OpenFolder, menuPx),
            ToolTipText = "Show the measurement data files in Explorer"
        };
        folderItem.Click += (_, _) => OpenMeasurementFolder();
        toolsToolStripMenuItem.DropDownItems.Add(folderItem);

        toolsToolStripMenuItem.DropDownOpening += (_, _) => RefreshMeasurementLogUi();

        toolStrip1.Items.Add(new ToolStripSeparator());

        _btnStartLog = new ToolStripButton
        {
            Name = "btnStartMeasurementLog",
            Text = "Start Log",
            Image = IconLibrary.Get(IconName.Run, barPx),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = "Start recording measurement data to a file"
        };
        _btnStartLog.Click += (_, _) => StartMeasurementLog();
        toolStrip1.Items.Add(_btnStartLog);

        _btnStopLog = new ToolStripButton
        {
            Name = "btnStopMeasurementLog",
            Text = "Stop Log",
            Image = IconLibrary.Get(IconName.Stop, barPx),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = "Stop recording measurement data"
        };
        _btnStopLog.Click += (_, _) => StopMeasurementLog();
        toolStrip1.Items.Add(_btnStopLog);

        _lblMeasurementLog = new ToolStripStatusLabel { Name = "lblMeasurementLog", Visible = false };
        statusStrip1.Items.Add(_lblMeasurementLog);

        _measurementLogTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _measurementLogTimer.Tick += (_, _) => RefreshMeasurementLogUi();
        _measurementLogTimer.Start();
        Disposed += (_, _) => _measurementLogTimer?.Dispose();

        RefreshMeasurementLogUi();
    }

    private void RefreshMeasurementLogUi()
    {
        if (IsDisposed || Disposing || _startLogItem is null) return;
        var core = systemThread?.Core;
        bool recording = core?.IsManualLogging ?? false;
        bool ready = core is not null;

        _startLogItem.Enabled = ready && !recording;
        _stopLogItem!.Enabled = recording;
        _btnStartLog!.Enabled = ready && !recording;
        _btnStopLog!.Enabled = recording;

        int interval = core?.ManualLogIntervalMs ?? 0;
        foreach (ToolStripMenuItem choice in _logIntervalItem!.DropDownItems)
            choice.Checked = choice.Tag is int ms && ms == interval;

        _lblMeasurementLog!.Visible = recording;
        if (recording)
            _lblMeasurementLog.Text = $"● Measurement log: {core!.ManualLogRows} rows, interval {interval} ms";
    }

    private void StartMeasurementLog()
    {
        var core = systemThread?.Core;
        if (core is null)
        {
            ThemedMessageBox.Show(this, "Control has not finished starting.", "Measurement log",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!core.TryStartManualLogging(out string message))
            ThemedMessageBox.Show(this, $"Measurement logging did not start: {message}", "Measurement log",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        RefreshMeasurementLogUi();
    }

    private void StopMeasurementLog()
    {
        systemThread?.Core?.StopManualLogging();
        RefreshMeasurementLogUi();
    }

    private bool TryGetMeasurementFiles(out IReadOnlyList<string> files)
    {
        files = systemThread?.Core?.ManualLogFiles ?? [];
        if (files.Count > 0) return true;
        ThemedMessageBox.Show(this, "No measurement log yet. Use Tools > Start Measurement Log first.",
            "Measurement log", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void OpenMeasurementLog()
    {
        if (!TryGetMeasurementFiles(out var files)) return;
        var viewer = new frmGraphLog();
        viewer.Show();
        viewer.OpenFile(files[^1]);
    }

    private void SaveMeasurementData()
    {
        if (!TryGetMeasurementFiles(out var files)) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Save measurement data",
            Filter = "Tab-separated values (*.tsv)|*.tsv|Comma-separated values (*.csv)|*.csv",
            FileName = Path.GetFileNameWithoutExtension(files[^1]),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var written = ManualLogExport.Write(files, dialog.FileName, csv: dialog.FilterIndex == 2);
            ThemedMessageBox.Show(this, "Saved:\n" + string.Join("\n", written), "Measurement log",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"Could not save the data: {ex.Message}", "Measurement log",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenMeasurementFolder()
    {
        string folder = systemThread?.Core?.ManualLogPath is { } last
            ? Path.GetDirectoryName(last)!
            : Path.Combine(AppPaths.DataDir, "Manual");
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
