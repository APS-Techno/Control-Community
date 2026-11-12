using System.Globalization;
using System.IO;
using APS.Icons;
using ScottPlot;
using ScottPlot.WinForms;
using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

/// <summary>
/// Standalone form for reviewing recorded TSV data files.
/// Displays time-series data in ScottPlot charts organized by data group tabs.
/// </summary>
public class frmGraphLog : Form
{
    private readonly ToolStrip toolStrip;
    private readonly DarkTabControl tabControl;
    private readonly StatusStrip statusStrip;
    private readonly ToolStripStatusLabel statusLabel;
    private HintManager _hints = null!;

    private string[] _headers = [];
    private string[] _units = [];
    private double[] _elapsedData = [];
    private Dictionary<string, double[]> _columnData = new();

    // Group definitions: prefix in header -> group name
    private static readonly Dictionary<string, string> GroupMap = new()
    {
        { "S_", "Pressure" },
        { "MFC_", "Flow" },
        { "PSU_", "Power" },
        { "STG_", "Position" },
        { "TV_", "Valve" },     // throttle-valve position (%), own axis — not a pressure
        { "VLV_", "State" },
        { "PMP_", "State" },
    };

    public frmGraphLog()
    {
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "Graph Log Viewer";
        Size = new Size(DpiScale.Px(900), DpiScale.Px(600));
        StartPosition = FormStartPosition.CenterScreen;
        Icon = null;

        // Tool strip, 24 logical px icons (ImageScalingSize is not DPI-scaled by WinForms)
        toolStrip = new ToolStrip { AutoSize = true };
        IconLibrary.ConfigureToolStrip(toolStrip, DpiScale.Px(24), IconName.OpenFile, IconName.ExportImage);
        var btnOpen = new ToolStripButton("Open TSV...") { ImageKey = IconName.OpenFile };
        btnOpen.Click += BtnOpen_Click;
        toolStrip.Items.Add(btnOpen);

        var btnExport = new ToolStripButton("Export PNG...") { ImageKey = IconName.ExportImage };
        btnExport.Click += BtnExport_Click;
        toolStrip.Items.Add(btnExport);

        tabControl = new DarkTabControl
        {
            Dock = DockStyle.Fill
        };

        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("No file loaded.");
        statusStrip.Items.Add(statusLabel);

        Controls.Add(tabControl);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);

        DarkModeHelper.FixControls(this);
        tabControl.ApplyPageTheme();

        _hints = new HintManager(this);
        _hints.Set(tabControl, "Data tabs — each tab groups one category of recorded signals: Pressure (S_ columns, Torr), Flow (MFC_ columns, sccm), Power (PSU_ columns, W), Position (STG_ columns, mm). Unmatched columns appear in 'Other'.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    private void BtnOpen_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
            Title = "Open Data Log File",
            InitialDirectory = APS.SystemControl.Core.CoreClass.AppPaths.DataDir,
        };

        if (ofd.ShowDialog() == DialogResult.OK)
        {
            LoadTsvFile(ofd.FileName);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (tabControl.SelectedTab == null) return;

        FormsPlot? plot = null;
        foreach (Control c in tabControl.SelectedTab.Controls)
        {
            if (c is FormsPlot fp)
            {
                plot = fp;
                break;
            }
        }

        if (plot == null) return;

        using var sfd = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            Title = "Export Chart as PNG",
            FileName = $"chart_{tabControl.SelectedTab.Text}.png"
        };

        if (sfd.ShowDialog() == DialogResult.OK)
        {
            plot.Plot.SavePng(sfd.FileName, (int)plot.Width, (int)plot.Height);
            statusLabel.Text = $"Exported to {sfd.FileName}";
        }
    }

    public void OpenFile(string filePath) => LoadTsvFile(filePath);

    private void LoadTsvFile(string filePath)
    {
        try
        {
            // ReadWrite share: the file may still be open in DataLogger's writer (a running measurement log).
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var textReader = new StreamReader(stream);
            var lineList = new List<string>();
            while (textReader.ReadLine() is { } line) lineList.Add(line);
            string[] lines = [.. lineList];
            if (lines.Length < 3)
            {
                statusLabel.Text = "File has too few lines (need header, units, and at least one data row).";
                return;
            }

            _headers = lines[0].Split('\t');
            _units = lines[1].Split('\t');

            int elapsedIdx = Array.IndexOf(_headers, "Elapsed");
            if (elapsedIdx < 0)
            {
                statusLabel.Text = "No 'Elapsed' column found in file.";
                return;
            }

            int dataRowCount = lines.Length - 2;
            _elapsedData = new double[dataRowCount];
            _columnData = new Dictionary<string, double[]>();

            for (int col = 0; col < _headers.Length; col++)
            {
                if (col == elapsedIdx || APS.SystemControl.Core.Logging.DataLogger.ClockColumns.Contains(_headers[col]))
                    continue;
                _columnData[_headers[col]] = new double[dataRowCount];
            }

            for (int row = 0; row < dataRowCount; row++)
            {
                string[] fields = lines[row + 2].Split('\t');

                if (elapsedIdx < fields.Length &&
                    double.TryParse(fields[elapsedIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out double elapsed))
                {
                    _elapsedData[row] = elapsed;
                }

                for (int col = 0; col < _headers.Length; col++)
                {
                    if (col == elapsedIdx || APS.SystemControl.Core.Logging.DataLogger.ClockColumns.Contains(_headers[col]))
                        continue;

                    if (_columnData.TryGetValue(_headers[col], out double[]? colData))
                    {
                        if (col < fields.Length &&
                            double.TryParse(fields[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                        {
                            colData[row] = double.IsFinite(val) ? val : double.NaN;
                        }
                        else
                        {
                            colData[row] = double.NaN;
                        }
                    }
                }
            }

            BuildTabs();

            string fileName = Path.GetFileName(filePath);
            statusLabel.Text = $"Loaded: {fileName} ({dataRowCount} rows, {_columnData.Count} columns)";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error loading file: {ex.Message}";
        }
    }

    /// <summary>
    /// Groups columns by prefix and creates one tab with a ScottPlot chart per group.
    /// Columns that don't match any group are placed in an "Other" tab.
    /// </summary>
    private void BuildTabs()
    {
        tabControl.TabPages.Clear();

        var groups = new Dictionary<string, List<string>>();
        foreach (var header in _columnData.Keys)
        {
            string group = "Other";
            foreach (var kvp in GroupMap)
            {
                if (header.StartsWith(kvp.Key, StringComparison.Ordinal))
                {
                    group = kvp.Value;
                    break;
                }
            }

            if (!groups.ContainsKey(group))
                groups[group] = [];
            groups[group].Add(header);
        }

        foreach (var kvp in groups)
        {
            var tabPage = new TabPage(kvp.Key);
            var plot = new FormsPlot { Dock = DockStyle.Fill };

            IPalette palette = new ScottPlot.Palettes.Category10();
            int colorIdx = 0;

            var statsPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                AutoScroll = true
            };
            var statsLabel = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                AutoSize = false,
            };
            statsPanel.Controls.Add(statsLabel);

            var statsLines = new List<string>();

            foreach (string columnName in kvp.Value)
            {
                if (!_columnData.TryGetValue(columnName, out double[]? yData))
                    continue;

                int headerIdx = Array.IndexOf(_headers, columnName);
                string unit = (headerIdx >= 0 && headerIdx < _units.Length) ? _units[headerIdx] : "";

                double min = double.MaxValue, max = double.MinValue;
                bool hasValidValue = false;
                foreach (double value in yData)
                {
                    if (!double.IsFinite(value)) continue;
                    hasValidValue = true;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
                if (!hasValidValue)
                {
                    statsLines.Add($"{columnName} [{unit}]: unavailable");
                    continue;
                }
                var scatter = plot.Plot.Add.Scatter(_elapsedData, yData);
                scatter.LegendText = columnName;
                scatter.Color = palette.GetColor(colorIdx);
                scatter.LineWidth = 1.5f;
                scatter.MarkerSize = 3;
                colorIdx++;
                statsLines.Add($"{columnName} [{unit}]: min={min:G4}, max={max:G4}");
            }

            plot.Plot.XLabel("Elapsed (s)");
            plot.Plot.YLabel(kvp.Key);
            plot.Plot.Title(kvp.Key);
            plot.Plot.ShowLegend();
            if (VacuumTheme.IsDark)
            {
                plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#0D1B2A");
                plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#0D1B2A");
                plot.Plot.Axes.Color(ScottPlot.Color.FromHex("#C8D6E5"));
                plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#28375A");
                plot.Plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#1E2C42");
                plot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#0D1B2A");
                plot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#C8D6E5");
                plot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#28375A");
            }
            plot.Plot.Axes.AutoScale();
            plot.Refresh();

            statsLabel.Text = string.Join("    ", statsLines);

            tabPage.Controls.Add(plot);
            tabPage.Controls.Add(statsPanel);
            tabControl.TabPages.Add(tabPage);
            DarkModeHelper.FixControls(tabPage);
        }
    }
}
