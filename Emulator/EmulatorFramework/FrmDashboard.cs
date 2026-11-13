using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Hosting;
using APS.SystemEmulator.Setup;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;
using APS.Controls.Theme;
using APS.SystemEmulator.Help;
using Protocols.Shared;
using S7Server;
using TcpSerialServer;
using VirtualSerialAdapter;

namespace APS.SystemEmulator;

public class FrmDashboard : Form
{
    private static readonly Color StatusGreen = Color.FromArgb(0, 255, 136);
    private static readonly Color StatusRed = Color.FromArgb(255, 68, 102);
    private static readonly Color StatusOrange = Color.FromArgb(255, 170, 51);
    private static readonly Color StatusYellow = Color.FromArgb(255, 221, 51);
    private static readonly Color StatusGray = Color.FromArgb(119, 136, 153);

    private SimulationEngine? _engine;
    private EmulatorConfig? _config;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private EmulatorSettings _settings = new();
    private FormWindowState _lastNonMinimizedState = FormWindowState.Normal;
    private ProjectLifecycleController? _controller;
    private readonly CliOptions? _cli;
    private ToolStripMenuItem? _recentProjectsMenu;
    private ToolStripMenuItem? _loadLastOnStartupItem;
    private ToolStripMenuItem? _saveStateItem;
    private ToolStripMenuItem? _loadStateItem;

    private readonly DarkTabControl _tabs;

    private readonly Panels.ChambersPanel _chambersPanel = new();
    private readonly Panels.PumpsPanel    _pumpsPanel    = new();
    private readonly Panels.ValvesPanel   _valvesPanel   = new();
    private readonly Panels.MfcsPanel     _mfcsPanel     = new();
    private readonly Label                _lblStatusBar  = new();
    private TableLayoutPanel?             _vacuumLayout;

    private readonly Label _lblReplayFile = new();
    private readonly Label _lblReplayTime = new();
    private readonly ComboBox _cboSpeed = new();
    private readonly Button _btnBrowse = new();
    private readonly Button _btnPlay = new();
    private readonly Button _btnPause = new();
    private readonly Button _btnStop = new();
    private readonly CheckBox _chkSound = new();
    private LogReplaySource? _replaySource;

    private readonly TextBox _txtConfig = new();
    private readonly Label _lblProtocolStatus = new();
    private readonly Label _lblDeviceCount = new();

    private readonly DataGridView _logGrid = new();
    private readonly ComboBox _cboLogSource = new();
    private readonly ComboBox _cboLogLevel = new();
    private readonly CheckBox _chkAutoScroll = new();
    private readonly Button _btnClearLog = new();
    private int _lastLogCount;

    private readonly DataGridView _powerGrid = new();

    private HelpForm? _helpForm;

    private Panel? _busyOverlay;
    private Label? _busyLabel;

    private readonly EmulatorEdition _edition;
    private ToolStripMenuItem? _helpMenu;
    private ToolStripItem? _aboutMenuItem;

    public FrmDashboard(CliOptions cli, EmulatorEdition edition)
    {
        _cli = cli;
        _edition = edition;
        Text = edition.ProductName;
        ClientSize = new Size(DpiScale.Px(1024), DpiScale.Px(700));
        StartPosition = FormStartPosition.CenterScreen;
        _settings = EmulatorSettings.Load(EmulatorSettings.SettingsPathFor(edition.SettingsFolder));
        ApplyWindowPlacement();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "EmulatorIcon.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);

        _tabs = new DarkTabControl { Dock = DockStyle.Fill };
        Controls.Add(_tabs);

        BuildVacuumTab();
        BuildReplayTab();
        BuildConfigTab();
        BuildLogTab();
        BuildPowerSuppliesTab();

        _tabs.ApplyPageTheme();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _uiTimer.Tick += UiTimer_Tick;

        Load += FrmDashboard_Load;
        FormClosing += FrmDashboard_FormClosing;

        var menuStrip = new MenuStrip();

        var fileMenu = new ToolStripMenuItem("&File");

        var openProjectItem = new ToolStripMenuItem("&Open Project...")
        {
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.O,
        };
        openProjectItem.Click += (_, _) => OpenProjectDialog();
        fileMenu.DropDownItems.Add(openProjectItem);

        _recentProjectsMenu = new ToolStripMenuItem("&Recent Projects");
        fileMenu.DropDownItems.Add(_recentProjectsMenu);

        fileMenu.DropDownItems.Add(new ToolStripSeparator());

        _saveStateItem = new ToolStripMenuItem("&Save State...") { ShortcutKeys = Keys.Control | Keys.S };
        _saveStateItem.Click += (_, _) => SaveState();
        fileMenu.DropDownItems.Add(_saveStateItem);

        _loadStateItem = new ToolStripMenuItem("&Load State...") { ShortcutKeys = Keys.Control | Keys.O };
        _loadStateItem.Click += (_, _) => LoadState();
        fileMenu.DropDownItems.Add(_loadStateItem);

        _loadLastOnStartupItem = new ToolStripMenuItem("Load Last State on Startup") { CheckOnClick = true };
        _loadLastOnStartupItem.CheckedChanged += (_, _) =>
            _controller?.SetLoadLastStateOnStartup(_loadLastOnStartupItem!.Checked);
        fileMenu.DropDownItems.Add(_loadLastOnStartupItem);

        fileMenu.DropDownItems.Add(new ToolStripSeparator());

        var resetItem = new ToolStripMenuItem("&Reset Simulation") { ShortcutKeys = Keys.Control | Keys.R };
        resetItem.Click += (_, _) => ResetSimulation();
        fileMenu.DropDownItems.Add(resetItem);

        fileMenu.DropDownItems.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("E&xit");
        exitItem.Click += (_, _) => Close();
        fileMenu.DropDownItems.Add(exitItem);

        menuStrip.Items.Add(fileMenu);

        var helpMenu = new ToolStripMenuItem("&Help");
        var helpTopicsItem = new ToolStripMenuItem("Help &Topics");
        helpTopicsItem.ShortcutKeys = Keys.F1;
        helpTopicsItem.ShortcutKeyDisplayString = "F1";
        helpTopicsItem.Click += (_, _) => ShowHelp();
        helpMenu.DropDownItems.Add(helpTopicsItem);

        helpMenu.DropDownItems.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("&About");
        aboutItem.Click += (_, _) => ShowAbout();
        helpMenu.DropDownItems.Add(aboutItem);
        _helpMenu = helpMenu;
        _aboutMenuItem = aboutItem;

        menuStrip.Items.Add(helpMenu);

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);

        DarkModeHelper.FixControls(this);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F1)
            {
                e.Handled = true;
                ShowHelp();
            }
        };
    }

    private void BuildVacuumTab()
    {
        var page = new TabPage("Vacuum");
        _tabs.TabPages.Add(page);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(DpiScale.Px(6)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(30)));

        _chambersPanel.Dock = DockStyle.Fill;
        _pumpsPanel.Dock    = DockStyle.Fill;
        _valvesPanel.Dock   = DockStyle.Fill;
        _mfcsPanel.Dock     = DockStyle.Fill;

        layout.Controls.Add(_chambersPanel, 0, 0);
        layout.Controls.Add(_pumpsPanel,    1, 0);
        layout.Controls.Add(_valvesPanel,   0, 1); layout.SetColumnSpan(_valvesPanel, 2);
        layout.Controls.Add(_mfcsPanel,     0, 2); layout.SetColumnSpan(_mfcsPanel, 2);

        StyleStatusBar(_lblStatusBar);
        layout.Controls.Add(_lblStatusBar, 0, 3);
        layout.SetColumnSpan(_lblStatusBar, 2);

        page.Controls.Add(layout);
        _vacuumLayout = layout;
    }

    private static void StyleStatusBar(Label label)
    {
        label.Text = "Mode: Simulation | Tick: 0 | SimTime: 0.0s";
        label.AutoSize = false;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.BorderStyle = BorderStyle.Fixed3D;
        label.Font = new Font("Consolas", 10f);
    }

    private void CollapseIfEmpty()
    {
        if (_vacuumLayout == null) return;

        bool chambersHas = _chambersPanel.HasContent;
        bool pumpsHas    = _pumpsPanel.HasContent;
        bool valvesHas   = _valvesPanel.HasContent;
        bool mfcsHas     = _mfcsPanel.HasContent;

        _chambersPanel.Visible = chambersHas;
        _pumpsPanel.Visible    = pumpsHas;
        _valvesPanel.Visible   = valvesHas;
        _mfcsPanel.Visible     = mfcsHas;

        if (!chambersHas && !pumpsHas) _vacuumLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, 0);
        if (!valvesHas)                _vacuumLayout.RowStyles[1] = new RowStyle(SizeType.Absolute, 0);
        if (!mfcsHas)                  _vacuumLayout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0);
    }

    private void BuildReplayTab()
    {
        var page = new TabPage("Log Replay");
        _tabs.TabPages.Add(page);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            Padding = new Padding(DpiScale.Px(10)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiScale.Px(100)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiScale.Px(100)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(40)));

        layout.Controls.Add(MakeHeaderLabel("Log File:"), 0, 0);
        _lblReplayFile.Text = "(none)";
        _lblReplayFile.AutoSize = true;
        _lblReplayFile.Dock = DockStyle.Fill;
        _lblReplayFile.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_lblReplayFile, 1, 0);

        _btnBrowse.Text = "Browse...";
        _btnBrowse.Dock = DockStyle.Fill;
        _btnBrowse.Click += BtnBrowse_Click;
        layout.Controls.Add(_btnBrowse, 2, 0);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _btnPlay.Text = "Play";
        _btnPlay.Size = new Size(DpiScale.Px(70), DpiScale.Px(23));
        _btnPlay.Click += BtnPlay_Click;
        btnPanel.Controls.Add(_btnPlay);

        _btnPause.Text = "Pause";
        _btnPause.Size = new Size(DpiScale.Px(70), DpiScale.Px(23));
        _btnPause.Click += BtnPause_Click;
        btnPanel.Controls.Add(_btnPause);

        _btnStop.Text = "Stop";
        _btnStop.Size = new Size(DpiScale.Px(70), DpiScale.Px(23));
        _btnStop.Click += BtnStop_Click;
        btnPanel.Controls.Add(_btnStop);

        _chkSound.Text = "Sound";
        _chkSound.AutoSize = true;
        _chkSound.CheckedChanged += (_, _) => SoundManager.Enabled = _chkSound.Checked;
        btnPanel.Controls.Add(_chkSound);

        layout.Controls.Add(btnPanel, 0, 1);
        layout.SetColumnSpan(btnPanel, 3);

        layout.Controls.Add(MakeHeaderLabel("Speed:"), 0, 2);
        _cboSpeed.Items.AddRange(["1x", "2x", "5x", "10x"]);
        _cboSpeed.SelectedIndex = 0;
        _cboSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboSpeed.Dock = DockStyle.Fill;
        _cboSpeed.SelectedIndexChanged += CboSpeed_SelectedIndexChanged;
        layout.Controls.Add(_cboSpeed, 1, 2);

        layout.Controls.Add(MakeHeaderLabel("Replay Time:"), 0, 3);
        _lblReplayTime.Text = "0.0s";
        _lblReplayTime.AutoSize = true;
        _lblReplayTime.Dock = DockStyle.Fill;
        _lblReplayTime.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_lblReplayTime, 1, 3);

        page.Controls.Add(layout);
    }

    private void BuildConfigTab()
    {
        var page = new TabPage("Config");
        _tabs.TabPages.Add(page);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(DpiScale.Px(10)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(36)));

        _txtConfig.Multiline = true;
        _txtConfig.ReadOnly = true;
        _txtConfig.ScrollBars = ScrollBars.Both;
        _txtConfig.Dock = DockStyle.Fill;
        _txtConfig.Font = new Font("Consolas", 9f);
        _txtConfig.WordWrap = false;
        var configHost = new DarkScrollHost(_txtConfig) { Dock = DockStyle.Fill };
        layout.Controls.Add(configHost, 0, 0);
        layout.SetColumnSpan(configHost, 2);

        layout.Controls.Add(MakeHeaderLabel("Protocols:"), 0, 1);
        _lblProtocolStatus.Text = "---";
        _lblProtocolStatus.Dock = DockStyle.Fill;
        _lblProtocolStatus.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_lblProtocolStatus, 1, 1);

        layout.Controls.Add(MakeHeaderLabel("Devices:"), 0, 2);
        _lblDeviceCount.Text = "0";
        _lblDeviceCount.Dock = DockStyle.Fill;
        _lblDeviceCount.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_lblDeviceCount, 1, 2);

        var btnSetup = new Button
        {
            Text = "Prepare System",
            AutoSize = true,
            Margin = new Padding(DpiScale.Px(5)),
        };
        btnSetup.Click += BtnPrepareSystem_Click;
        layout.Controls.Add(btnSetup, 0, 3);

        var btnReconnect = new Button
        {
            Text = "Reconnect Ports",
            AutoSize = true,
            Margin = new Padding(DpiScale.Px(5)),
        };
        btnReconnect.Click += BtnReconnectPorts_Click;
        layout.Controls.Add(btnReconnect, 1, 3);

        page.Controls.Add(layout);
    }

    private void BuildLogTab()
    {
        var page = new TabPage("Log");
        _tabs.TabPages.Add(page);

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = DpiScale.Px(35),
            Padding = new Padding(DpiScale.Px(4)),
        };

        filterPanel.Controls.Add(new Label { Text = "Source:", AutoSize = true, Margin = new Padding(0, DpiScale.Px(6), DpiScale.Px(4), 0) });
        _cboLogSource.Width = DpiScale.Px(140);
        _cboLogSource.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboLogSource.Items.Add(LogSourceFilter.All);
        _cboLogSource.SelectedIndex = 0;
        filterPanel.Controls.Add(_cboLogSource);

        filterPanel.Controls.Add(new Label { Text = "Level:", AutoSize = true, Margin = new Padding(DpiScale.Px(8), DpiScale.Px(6), DpiScale.Px(4), 0) });
        _cboLogLevel.Width = DpiScale.Px(100);
        _cboLogLevel.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboLogLevel.Items.AddRange(["All", "Debug+", "Info+", "Warning+", "Error"]);
        _cboLogLevel.SelectedIndex = 2; // Info+
        filterPanel.Controls.Add(_cboLogLevel);

        _btnClearLog.Text = "Clear";
        _btnClearLog.Click += (_, _) => { EmulatorLog.Clear(); _logGrid.Rows.Clear(); _lastLogCount = 0; };
        filterPanel.Controls.Add(_btnClearLog);

        _chkAutoScroll.Text = "Auto-scroll";
        _chkAutoScroll.Checked = true;
        _chkAutoScroll.Margin = new Padding(DpiScale.Px(8), DpiScale.Px(4), 0, 0);
        filterPanel.Controls.Add(_chkAutoScroll);

        page.Controls.Add(filterPanel);

        _logGrid.Dock = DockStyle.Fill;
        _logGrid.ReadOnly = true;
        _logGrid.AllowUserToAddRows = false;
        _logGrid.AllowUserToDeleteRows = false;
        _logGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _logGrid.RowHeadersVisible = false;
        _logGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _logGrid.RowTemplate.Height = DpiScale.Px(20);
        _logGrid.Font = new Font("Consolas", 8.5f);
        if (!VacuumTheme.IsDark)
            _logGrid.BackgroundColor = Color.White;

        _logGrid.Columns.Add("Time", "Time");
        _logGrid.Columns.Add("Level", "Level");
        _logGrid.Columns.Add("Source", "Source");
        _logGrid.Columns.Add("Message", "Message");
        _logGrid.Columns["Time"]!.Width = DpiScale.Px(90);
        _logGrid.Columns["Level"]!.Width = DpiScale.Px(60);
        _logGrid.Columns["Source"]!.Width = DpiScale.Px(110);
        _logGrid.Columns["Message"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        var logHost = new DarkScrollHost(_logGrid) { Dock = DockStyle.Fill };
        page.Controls.Add(logHost);
        logHost.BringToFront();
    }

    private void RefreshLogSources(IReadOnlyList<LogEntry> entries)
    {
        var items = LogSourceFilter.Items(entries.Select(e => e.Source));
        if (items.SequenceEqual(_cboLogSource.Items.Cast<string>())) return;

        var selected = _cboLogSource.SelectedItem as string;
        _cboLogSource.Items.Clear();
        _cboLogSource.Items.AddRange([.. items]);
        _cboLogSource.SelectedItem = selected != null && items.Contains(selected) ? selected : LogSourceFilter.All;
    }

    private void UpdateLogGrid()
    {
        var entries = EmulatorLog.GetEntries();
        if (entries.Count == _lastLogCount) return;

        RefreshLogSources(entries);
        string sourceFilter = _cboLogSource.SelectedItem?.ToString() ?? LogSourceFilter.All;
        LogLevel minLevel = _cboLogLevel.SelectedIndex switch
        {
            0 => LogLevel.Trace,
            1 => LogLevel.Debug,
            2 => LogLevel.Info,
            3 => LogLevel.Warning,
            4 => LogLevel.Error,
            _ => LogLevel.Info,
        };

        var newEntries = entries.Skip(_lastLogCount);
        _lastLogCount = entries.Count;

        foreach (var entry in newEntries)
        {
            if (entry.Level < minLevel) continue;
            if (!LogSourceFilter.Matches(sourceFilter, entry.Source))
                continue;

            int rowIdx = _logGrid.Rows.Add(
                entry.Time.ToString("HH:mm:ss.fff"),
                entry.Level.ToString(),
                entry.Source,
                entry.Message);

            _logGrid.Rows[rowIdx].DefaultCellStyle.BackColor = entry.Level switch
            {
                LogLevel.Trace => VacuumTheme.IsDark ? Color.FromArgb(14, 24, 38) : Color.LightGray,
                LogLevel.Debug => VacuumTheme.IsDark ? Color.FromArgb(17, 29, 46) : Color.FromArgb(240, 240, 240),
                LogLevel.Warning => VacuumTheme.IsDark ? Color.FromArgb(42, 38, 18) : Color.LightYellow,
                LogLevel.Error => VacuumTheme.IsDark ? Color.FromArgb(50, 20, 25) : Color.FromArgb(255, 200, 200),
                _ => VacuumTheme.IsDark ? Color.FromArgb(22, 32, 50) : Color.White,
            };
        }

        while (_logGrid.Rows.Count > 5000)
            _logGrid.Rows.RemoveAt(0);

        if (_chkAutoScroll.Checked && _logGrid.Rows.Count > 0)
            _logGrid.FirstDisplayedScrollingRowIndex = _logGrid.Rows.Count - 1;
    }

    private void BuildPowerSuppliesTab()
    {
        var page = new TabPage("Power Sources");
        _tabs.TabPages.Add(page);

        _powerGrid.Dock = DockStyle.Fill;
        _powerGrid.ReadOnly = true;
        _powerGrid.AllowUserToAddRows = false;
        _powerGrid.AllowUserToDeleteRows = false;
        _powerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _powerGrid.RowHeadersVisible = false;
        _powerGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _powerGrid.RowTemplate.Height = DpiScale.Px(24);
        _powerGrid.Font = new Font("Consolas", 9f);
        if (!VacuumTheme.IsDark)
            _powerGrid.BackgroundColor = Color.White;

        _powerGrid.Columns.Add("Name", "Name");
        _powerGrid.Columns.Add("Port", "Port");
        _powerGrid.Columns.Add("Link", "Link");
        _powerGrid.Columns.Add("Type", "Type");
        _powerGrid.Columns.Add("Voltage", "Voltage");
        _powerGrid.Columns.Add("Current", "Current");
        _powerGrid.Columns.Add("Power", "Power");
        _powerGrid.Columns.Add("Output", "Output");

        _powerGrid.Columns["Name"]!.Width = DpiScale.Px(90);
        _powerGrid.Columns["Port"]!.Width = DpiScale.Px(70);
        _powerGrid.Columns["Link"]!.Width = DpiScale.Px(70);
        _powerGrid.Columns["Type"]!.Width = DpiScale.Px(100);
        _powerGrid.Columns["Voltage"]!.Width = DpiScale.Px(90);
        _powerGrid.Columns["Current"]!.Width = DpiScale.Px(90);
        _powerGrid.Columns["Power"]!.Width = DpiScale.Px(90);
        _powerGrid.Columns["Output"]!.Width = DpiScale.Px(70);

        _powerGrid.CellClick += PowerGrid_CellClick;

        page.Controls.Add(new DarkScrollHost(_powerGrid) { Dock = DockStyle.Fill });
    }

    private void PopulatePowerGrid()
    {
        if (_engine == null) return;

        foreach (var device in _engine.Devices)
        {
            if (device is not IPowerReadout readout) continue;

            string typeName = readout.TypeName;
            var server = _engine.Servers.FirstOrDefault(s => s.Name == device.Id);
            string port = server is VirtualSerialPortAdapter vsp ? vsp.HwPort : "-";
            string link = "-";

            int rowIdx = _powerGrid.Rows.Add(device.Id, port, link, typeName, "0", "0", "0", "OFF");
            _powerGrid.Rows[rowIdx].Cells["Link"].Style.ForeColor = StatusGray;
        }
    }

    private void PowerGrid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _engine == null) return;
        if (_powerGrid.Columns[e.ColumnIndex].Name != "Output") return;

        string deviceId = _powerGrid.Rows[e.RowIndex].Cells["Name"].Value?.ToString() ?? "";
        var device = _engine.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return;

        if (device is IPowerReadout { OutputOn: not null } readout)
            readout.ToggleOutput();
    }

    private void UpdatePowerGrid()
    {
        if (_engine == null) return;

        int row = 0;
        foreach (var device in _engine.Devices)
        {
            if (device is not IPowerReadout readout)
                continue;
            if (row >= _powerGrid.Rows.Count) break;

            var cells = _powerGrid.Rows[row].Cells;

            var server = _engine.Servers.FirstOrDefault(s => s.Name == device.Id);
            if (server is VirtualSerialPortAdapter vsp)
            {
                if (!vsp.IsRunning)
                {
                    cells["Link"].Value = "Port Down";
                    cells["Link"].Style.ForeColor = StatusRed;
                }
                else if (vsp.PeerDetected)
                {
                    cells["Link"].Value = "OK";
                    cells["Link"].Style.ForeColor = StatusGreen;
                }
                else
                {
                    cells["Link"].Value = "No Peer";
                    cells["Link"].Style.ForeColor = StatusYellow;
                }
            }
            else
            {
                cells["Link"].Value = "-";
                cells["Link"].Style.ForeColor = StatusGray;
            }

            cells["Voltage"].Value = readout.VoltageText;
            cells["Current"].Value = readout.CurrentText;
            cells["Power"].Value = readout.PowerText;
            cells["Output"].Value = readout.OutputText;
            cells["Output"].Style.ForeColor = readout.OutputOn switch
            {
                true => StatusGreen,
                false => StatusGray,
                null => StatusOrange,
            };

            row++;
        }
    }

    public void AddHelpMenuItem(ToolStripItem item)
    {
        var items = _helpMenu!.DropDownItems;
        items.Insert(items.IndexOf(_aboutMenuItem!), item);
    }

    private void FrmDashboard_Load(object? sender, EventArgs e)
    {
        foreach (var extension in _edition.DashboardExtensions)
            extension.OnLoad(this);

        var settingsPath = EmulatorSettings.SettingsPathFor(_edition.SettingsFolder);
        _controller = new ProjectLifecycleController(_settings, _edition.Registrations, settingsPath, AppContext.BaseDirectory);

        _controller.ProjectOpened += OnProjectOpened;
        _controller.ProjectOpenFailed += OnProjectOpenFailed;
        _controller.StateAutoloaded += path =>
            _lblStatusBar.Text = $"Restored state from {Path.GetFileName(path)}";
        _controller.StateAutoloadFailed += (path, ex) =>
        {
            _lblStatusBar.Text = $"Failed to restore last state: {ex.Message}";
            EmulatorLog.Log(LogLevel.Warning, "Dashboard", $"Autoload failed: {ex.Message}");
        };

        _loadLastOnStartupItem!.Checked = _settings.LoadLastStateOnStartup;
        RefreshRecentProjectsMenu();
        UpdateProjectMenuEnabled();

        if (!string.IsNullOrWhiteSpace(_cli?.Project))
            _controller.OpenProject(_cli.Project!, _cli.StateFile);
        else if (!_controller.TryOpenLastOrPrompt())
            OpenProjectDialog();

        _uiTimer.Start();
    }

    private int _tickCount;

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_engine == null) return;

        _tickCount++;
        var state = _engine.State;

        _chambersPanel.UpdateValues();
        _pumpsPanel.UpdateValues();
        _valvesPanel.UpdateValues();
        _mfcsPanel.UpdateValues();

        string mode = state.DataSource.Mode == DataSourceMode.LogReplay ? "Replay" : "Simulation";
        int s7Clients = _engine.Servers.OfType<S7ProtocolServer>().Sum(s => s.ActiveClientCount);
        string connStatus = s7Clients > 0 ? $"\u25cf Control connected ({s7Clients})" : "\u25cb Control not connected";
        _lblStatusBar.ForeColor = s7Clients > 0 ? StatusGreen : StatusGray;
        _lblStatusBar.Text = $"{connStatus} | Mode: {mode} | Tick: {_tickCount} | SimTime: {state.SimTime:F1}s";

        _lblReplayTime.Text = $"{state.SimTime:F1}s";

        UpdateLogGrid();
        UpdatePowerGrid();
    }

    private void PopulateConfigTab()
    {
        if (_config == null)
        {
            _txtConfig.Text = "(No config loaded)";
            return;
        }

        var lines = new List<string>
        {
            $"Tick Interval:  {_config.Engine.TickIntervalMs}ms",
            "",
            "Protocols:",
        };

        foreach (var proto in _config.Protocols)
        {
            string type = proto switch
            {
                S7ServerConfig s7 => $"  S7Server (plcId={s7.PlcId})",
                TcpSerialConfig tcp => $"  TcpSerial (deviceId={tcp.DeviceId})",
                SerialConfig ser => $"  Serial (deviceId={ser.DeviceId})",
                _ => $"  Unknown",
            };

            string endpoint = proto switch
            {
                SerialConfig ser => $"COM{DeriveHwPort(ser.PortName)} ({ser.BaudRate}, {ser.Framing})  [emulator: {ser.PortName}]",
                _ => $"{proto.BindAddress}:{proto.Port}",
            };

            lines.Add($"{type}  {endpoint}");
        }

        lines.Add("");
        lines.Add("Physics:");
        lines.Add($"  Noise StdDev: {_config.Physics.NoiseStddev}");
        lines.Add($"  Scroll Pump Speed: {_config.Physics.ScrollPumpSpeed} L/s");
        lines.Add($"  Turbo Pump Speed: {_config.Physics.TurboPumpSpeed} L/s");
        foreach (var ch in _config.Physics.Chambers)
        {
            lines.Add($"  Chamber '{ch.Id}': Vol={ch.Volume}L BasePressure={ch.BasePressure} LeakRate={ch.LeakRate}");
        }

        if (_engine != null)
        {
            lines.Add("");
            lines.Add($"Loaded Devices ({_engine.Devices.Count}):");
            foreach (var device in _engine.Devices)
                lines.Add($"  [{device.Category}] {device.Id} - {device.Description}");
        }

        _txtConfig.Text = string.Join(Environment.NewLine, lines);

        if (_engine != null)
        {
            var serverStatus = _engine.Servers
                .Select(s => $"{s.Name}: {(s.IsRunning ? "Running" : "Stopped")}")
                .ToList();
            _lblProtocolStatus.Text = serverStatus.Count > 0
                ? string.Join(", ", serverStatus)
                : "No servers";

            _lblDeviceCount.Text = _engine.Devices.Count.ToString();
        }
    }

    private void BtnPrepareSystem_Click(object? sender, EventArgs e)
    {
        if (_config == null)
        {
            ThemedMessageBox.Show(this, "No configuration loaded.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dlg = new FrmPrepareSystem(_config);
        dlg.ShowDialog(this);
    }

    private void BtnReconnectPorts_Click(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        _engine.RestartServers();
        PopulateConfigTab();
        ThemedMessageBox.Show(this, "All protocol servers restarted.",
            "Reconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string DeriveHwPort(string emulatorPort)
    {
        // "COM103" -> "3", "COM121" -> "21"
        var num = System.Text.RegularExpressions.Regex.Match(emulatorPort, @"\d+");
        if (num.Success && int.TryParse(num.Value, out int n) && n > 100)
            return (n - 100).ToString();
        return emulatorPort;
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Log Files (*.log)|*.log|All Files (*.*)|*.*",
            Title = "Select Log File",
        };

        if (dlg.ShowDialog() == DialogResult.OK)
        {
            try
            {
                _replaySource = new LogReplaySource();
                _replaySource.Load(dlg.FileName);
                _lblReplayFile.Text = Path.GetFileName(dlg.FileName);

                _engine?.SetDataSource(_replaySource);
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(this, $"Error loading log file:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnPlay_Click(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        _engine.Time.IsPaused = false;
    }

    private void BtnPause_Click(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        _engine.Time.IsPaused = true;
    }

    private void BtnStop_Click(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        _engine.Time.IsPaused = true;
        _engine.Time.Reset();
        _engine.Time.Start();
        _engine.Time.IsPaused = true;
    }

    private void CboSpeed_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_engine == null) return;
        _engine.Time.SpeedMultiplier = _cboSpeed.SelectedIndex switch
        {
            0 => 1.0f,
            1 => 2.0f,
            2 => 5.0f,
            3 => 10.0f,
            _ => 1.0f,
        };
    }

    private void FrmDashboard_FormClosing(object? sender, FormClosingEventArgs e)
    private void ApplyWindowPlacement()
    {
        if (_settings.HasWindowBounds)
        {
            var saved = new Rectangle(_settings.WindowX!.Value, _settings.WindowY!.Value,
                _settings.WindowWidth!.Value, _settings.WindowHeight!.Value);
            var areas = Screen.AllScreens.Select(s => s.WorkingArea);
            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Normal;
            DesktopBounds = EmulatorSettings.FitWindow(saved, areas, Screen.PrimaryScreen!.WorkingArea);
        }
        WindowState = (_settings.WindowMaximized ?? false) ? FormWindowState.Maximized : FormWindowState.Normal;
        _lastNonMinimizedState = WindowState;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (WindowState != FormWindowState.Minimized) _lastNonMinimizedState = WindowState;
    }

    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _settings.RememberWindow(
            WindowState, _lastNonMinimizedState,
            WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
        try { _settings.Save(EmulatorSettings.SettingsPathFor(_edition.SettingsFolder)); }
        catch { }
        _engine?.Dispose();
    }

    private void ShowHelp()
    {
        if (_helpForm == null || _helpForm.IsDisposed)
        {
            _helpForm = new HelpForm(_edition.ExtraHelpFolders);
            _helpForm.Show(this);
        }
        else
        {
            _helpForm.BringToFront();
            if (_helpForm.WindowState == FormWindowState.Minimized)
                _helpForm.WindowState = FormWindowState.Normal;
        }
    }

    private void ShowAbout()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        ThemedMessageBox.Show(this,
            $"{_edition.ProductName} v{versionStr}\n\n" +
            "Physics-based hardware simulator for\n" +
            "APS vacuum systems.\n\n" +
            ".NET 10 / WinForms",
            $"About {_edition.ProductName}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenProjectDialog()
    {
        if (_controller == null) return;

        if (_controller.Engine != null)
        {
            var ok = ThemedMessageBox.Show(this,
                "Switch project? The current simulation state will be discarded unless saved.",
                "Switch Project",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;
        }

        using var dlg = new FrmOpenProject(_controller);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.NewConfigsRoot != null) _controller.SetConfigsRoot(dlg.NewConfigsRoot);
        if (string.IsNullOrEmpty(dlg.SelectedProject)) return;

        var projectName = dlg.SelectedProject;
        var syncRequested = dlg.SyncControlRequested;
        RunProjectOpenWithBusyUi(projectName, () => _controller.OpenProject(projectName));

        // Sync Control's RootConfig only if (a) user opted in and (b) OpenProject succeeded.
        if (syncRequested && _controller.ActiveProjectName == projectName)
            SyncControlConfig(projectName);
    }

    /// <summary>
    /// Runs a (long, synchronous) project open with a wait cursor and a
    /// full-window "Opening project…" overlay. The open blocks the UI thread,
    /// so the overlay is force-painted before the work starts.
    /// </summary>
    private void RunProjectOpenWithBusyUi(string projectName, Action open)
    {
        ShowBusyOverlay($"Opening project “{projectName}”…");
        // UseWaitCursor alone takes effect only on the next WM_SETCURSOR,
        // which never arrives while the UI thread is blocked.
        Cursor.Current = Cursors.WaitCursor;
        UseWaitCursor = true;
        try
        {
            open();
        }
        finally
        {
            UseWaitCursor = false;
            Cursor.Current = Cursors.Default;
            if (_busyOverlay != null) _busyOverlay.Visible = false;
        }
    }

    private void ShowBusyOverlay(string message)
    {
        if (_busyOverlay == null)
        {
            _busyLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            };
            _busyOverlay = new Panel { Dock = DockStyle.Fill };
            _busyOverlay.Controls.Add(_busyLabel);
            Controls.Add(_busyOverlay);
        }

        bool dark = VacuumTheme.IsDark;
        _busyOverlay.BackColor = dark ? VacuumTheme.PanelBackground : SystemColors.Control;
        _busyLabel!.ForeColor = dark ? VacuumTheme.TextPrimary : SystemColors.ControlText;
        _busyLabel.Text = message;
        _busyOverlay.Visible = true;
        _busyOverlay.BringToFront();
        _busyOverlay.Refresh();
    }

    private void SyncControlConfig(string projectName)
    {
        if (_controller == null || string.IsNullOrEmpty(_controller.Settings.ConfigsRoot)) return;

        var result = ControlConfigSync.TrySync(
            emulatorBaseDir: AppDomain.CurrentDomain.BaseDirectory,
            overridePath: _controller.Settings.ControlRootConfigPath,
            configsRoot: _controller.Settings.ConfigsRoot,
            projectName: projectName,
            out var resolvedPath,
            out var err);

        switch (result)
        {
            case ControlSyncResult.Synced:
                // silent
                break;
            case ControlSyncResult.FileNotFound:
                ThemedMessageBox.Show(this,
                    $"Control's RootConfig.xml not found at:\n{resolvedPath}\n\n" +
                    "Set \"controlRootConfigPath\" in settings.json if Control is installed elsewhere.",
                    "Sync Control config",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;
            case ControlSyncResult.IOError:
                ThemedMessageBox.Show(this,
                    $"Could not update Control's RootConfig.xml:\n{err}\n\n" +
                    "If Control is running, close it and try again.",
                    "Sync Control config",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                break;
        }
    }

    private void OnProjectOpened(string projectName)
    {
        Text = $"{_edition.ProductName} \u2014 {projectName}";
        _chkSound.Checked = _controller?.Config?.Engine.Sound ?? true;
        SoundManager.Enabled = _chkSound.Checked;

        WireProtocolServersAndStart();
        RefreshRecentProjectsMenu();
        UpdateProjectMenuEnabled();

        PopulateConfigTab();
        _chambersPanel.AttachEngine(_engine!);
        _pumpsPanel.AttachEngine(_engine!);
        _valvesPanel.AttachEngine(_engine!);
        _mfcsPanel.AttachEngine(_engine!);
        CollapseIfEmpty();
        PopulatePowerGrid();
    }

    private void OnProjectOpenFailed(string projectName, Exception ex)
    {
        ThemedMessageBox.Show(this,
            $"Failed to open project '{projectName}':\n{ex.Message}",
            "Open Project",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        UpdateProjectMenuEnabled();
    }

    private void WireProtocolServersAndStart()
    {
        if (_controller?.Engine == null || _controller.Config == null) return;
        var engine = _controller.Engine;
        _engine = engine;
        _config = _controller.Config;

        foreach (var proto in _config.Protocols)
        {
            try
            {
                IProtocolServer server = proto switch
                {
                    S7ServerConfig s7 => new S7ProtocolServer(s7.BindAddress, s7.Port, $"S7-{s7.PlcId}", s7.PlcId),
                    TcpSerialConfig tcp => new TcpSerialProtocolServer(
                        tcp.DeviceId, tcp.BindAddress, tcp.Port,
                        Enum.TryParse<FramingMode>(tcp.Framing, true, out var fm) ? fm : FramingMode.CrLfTerminated),
                    SerialConfig ser => new VirtualSerialPortAdapter(
                        ser.DeviceId, ser.PortName, ser.BaudRate,
                        Enum.TryParse<FramingMode>(ser.Framing, true, out var sfm) ? sfm : FramingMode.CrLfTerminated),
                    _ => throw new NotSupportedException($"Unknown protocol type: {proto.GetType().Name}")
                };
                engine.AddServer(server);
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(this,
                    $"Could not create protocol server:\n{ex.Message}",
                    "Protocol Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        engine.RegisterDevicesWithServers();
        engine.Start();
    }

    private void RefreshRecentProjectsMenu()
    {
        if (_recentProjectsMenu == null || _controller == null) return;
        _recentProjectsMenu.DropDownItems.Clear();

        if (_controller.Settings.RecentProjects.Count == 0)
        {
            _recentProjectsMenu.Enabled = false;
            return;
        }

        _recentProjectsMenu.Enabled = true;
        foreach (var name in _controller.Settings.RecentProjects)
        {
            var item = new ToolStripMenuItem(name);
            string captured = name;
            item.Click += (_, _) =>
            {
                if (_controller.Engine != null)
                {
                    var ok = ThemedMessageBox.Show(this,
                        "Switch project? The current simulation state will be discarded unless saved.",
                        "Switch Project",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question);
                    if (ok != DialogResult.OK) return;
                }
                RunProjectOpenWithBusyUi(captured, () => _controller.OpenProject(captured));
            };
            _recentProjectsMenu.DropDownItems.Add(item);
        }
    }

    private void UpdateProjectMenuEnabled()
    {
        bool hasProject = _controller?.Engine != null;
        if (_saveStateItem != null) _saveStateItem.Enabled = hasProject;
        if (_loadStateItem != null) _loadStateItem.Enabled = hasProject;
        if (!hasProject) Text = $"{_edition.ProductName} \u2014 (no project)";
    }

    private static Label MakeHeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 9f),
        };
    }

    private void SaveState()
    {
        if (_engine == null || _controller?.ActiveProjectPath == null) return;

        string defaultDir = Path.Combine(_controller.ActiveProjectPath, "Snapshots");
        if (!Directory.Exists(defaultDir)) Directory.CreateDirectory(defaultDir);

        using var dlg = new SaveFileDialog
        {
            Title = "Save Emulator State",
            Filter = "Emulator State (*.emstate)|*.emstate",
            DefaultExt = "emstate",
            InitialDirectory = defaultDir,
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _engine.Pause();
            var snapshot = SnapshotService.Capture(_engine.State, _engine.Devices);
            SnapshotService.SaveToFile(snapshot, dlg.FileName);
            _controller.RememberStateFile(dlg.FileName);
            ThemedMessageBox.Show(this, $"State saved to:\n{dlg.FileName}", "Save State",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, $"Failed to save state:\n{ex.Message}", "Save State Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _engine.Resume();
        }
    }

    private void ResetSimulation()
    {
        if (_engine == null) return;

        var result = ThemedMessageBox.Show(this,
            "Reset all devices and chambers to fresh start?\nConnections will be preserved.",
            "Reset Simulation",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (result != DialogResult.OK) return;

        _engine.ResetSimulation();
        EmulatorLog.Log(LogLevel.Info, "Dashboard", "Simulation reset by user");
    }

    private void LoadState()
    {
        if (_engine == null || _controller?.ActiveProjectPath == null) return;

        string defaultDir = Path.Combine(_controller.ActiveProjectPath, "Snapshots");

        using var dlg = new OpenFileDialog
        {
            Title = "Load Emulator State",
            Filter = "Emulator State (*.emstate)|*.emstate",
            DefaultExt = "emstate",
            InitialDirectory = Directory.Exists(defaultDir) ? defaultDir : _controller.ActiveProjectPath,
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _engine.Pause();
            var snapshot = SnapshotService.LoadFromFile(dlg.FileName);
            SnapshotService.Restore(snapshot, _engine.State, _engine.Devices);
            _controller.RememberStateFile(dlg.FileName);
            ThemedMessageBox.Show(this, $"State loaded from:\n{dlg.FileName}", "Load State",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, $"Failed to load state:\n{ex.Message}", "Load State Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _engine.Resume();
        }
    }

}
