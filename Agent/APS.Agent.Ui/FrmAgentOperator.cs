using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Journal;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Runtime;
using APS.Agent.Core.Skills;
using APS.Agent.Core.Strata;
using APS.Controls.Theme;

namespace APS.Agent.Ui;

public sealed class FrmAgentOperator : Form, IAgentSessionObserver
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly AgentUiSettings _settings;
    private readonly HttpClient _probeHttpClient;
    private readonly HttpClient _llmHttpClient;
    private readonly DarkToolTip _toolTip = new();
    private readonly Font _monoFont = new("Consolas", 9.5f);
    private readonly Font _boldFont = new("Segoe UI", 9f, FontStyle.Bold);

    private readonly Panel _headerPanel = new() { Dock = DockStyle.Top, Height = 74, Padding = new Padding(6, 5, 6, 5) };
    private readonly Button _btnModeCopilot = new() { Text = "COPILOT", Width = 96, Height = 28, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private readonly Button _btnModeAutonomous = new() { Text = "AUTONOMOUS", Width = 114, Height = 28, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private readonly DarkCheckBox _chkAllowDeviceCommand = new() { Text = "Direct Device Cmd", Margin = new Padding(8, 6, 6, 0) };
    private readonly Label _lblMachineBadge = CreateBadgeLabel("MCP OFFLINE | TIER: READ");
    private readonly Label _lblStrataIndicator = CreateBadgeLabel("STRATA STANDBY");
    private readonly Label _lblCloudIndicator = CreateBadgeLabel("CLOUD STANDBY");
    private readonly Label _lblElnIndicator = CreateBadgeLabel("ELN3 LOCAL");
    private readonly Button _btnConnectRefresh = new() { Text = "Connect / Probe", Width = 118, Height = 28 };

    private readonly Panel _watchBar = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 5, 6, 5) };
    private readonly Label _lblWatchState = new() { Text = "IDLE", Width = 88, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _lblWatchPeriod = new() { Text = "Period -/-", Width = 108, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _lblWatchDeposits = new() { Text = "Deposits: 0", Width = 102, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _lblWatchStep = new() { Text = "Step #0", Width = 88, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _pbarWatch = new() { Width = 150, Dock = DockStyle.Left, Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Value = 0 };
    private readonly Label _lblWatchTelemetry = new() { Text = "No active watch telemetry", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };

    private readonly DarkTabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _txtTranscript = new() { ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true, DetectUrls = false };
    private readonly DataGridView _gridTools = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.Fixed3D,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };

    private readonly TextBox _txtEndpoint = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _cmbProtocol = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtModel = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtApiKeyEnv = new() { Dock = DockStyle.Fill };
    private readonly DarkCheckBox _chkAllowExternal = new() { Text = "Allow External Cloud Endpoints" };
    private readonly TextBox _txtReasoningEffort = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtMcpBin = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtMcpConfig = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtSkillDir = new() { Dock = DockStyle.Fill };

    private readonly TextBox _txtElnEndpoint = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtElnApiKeyEnv = new() { Dock = DockStyle.Fill };
    private readonly DarkCheckBox _chkElnAllowExternal = new() { Text = "Allow External ELN3 Endpoint" };
    private readonly TextBox _txtElnMcpCommand = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtElnMcpArgs = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtElnMcpTool = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtElnExportDir = new() { Dock = DockStyle.Fill };
    private readonly DarkCheckBox _chkAutoSyncEln = new() { Text = "Auto-sync run artifacts to ELN3 on session completion" };
    private readonly Button _btnSyncElnNow = new() { Text = "Sync Latest Run to ELN3", Width = 180, Height = 28 };
    private readonly Label _lblElnSyncStatus = new() { Text = "ELN3 idle", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    private readonly Panel _bottomBar = new() { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6, 6, 6, 6) };
    private readonly TextBox _txtPrompt = new() { Dock = DockStyle.Fill, PlaceholderText = "Enter deposition goal, recipe validation request, or status query..." };
    private readonly Button _btnRunGoal = new() { Text = "RUN GOAL", Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private readonly Button _btnAbortRun = new() { Text = "ABORT RUN", Width = 104, Height = 30, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Enabled = false };
    private readonly Button _btnForceStop = new() { Text = "FORCE STOP", Width = 104, Height = 30, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Enabled = false };
    private readonly Button _btnSyncElnQuick = new() { Text = "SYNC ELN3", Width = 98, Height = 30 };

    private StdioMcpToolInvoker? _mcpInvoker;
    private string? _connectedMcpKey;
    private bool _isConnectingOrStarting;
    private readonly Dictionary<string, int> _toolRowMap = new(StringComparer.Ordinal);

    public OperatorSessionController Controller { get; } = new();

    public string MachineBadgeText => _lblMachineBadge.Text;
    public string StrataIndicatorText => _lblStrataIndicator.Text;
    public string CloudIndicatorText => _lblCloudIndicator.Text;
    public string ElnIndicatorText => _lblElnIndicator.Text;
    public string WatchPeriodText => _lblWatchPeriod.Text;
    public string WatchDepositsText => _lblWatchDeposits.Text;
    public string WatchTelemetryText => _lblWatchTelemetry.Text;

    public FrmAgentOperator() : this(AgentUiSettings.FromArgs([]))
    {
    }

    public FrmAgentOperator(AgentUiSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _probeHttpClient = new HttpClient(EndpointGuard.CreateNoRedirectHandler(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        _llmHttpClient = new HttpClient(EndpointGuard.CreateNoRedirectHandler(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        Text = "APS Autonomous Deposition Agent";
        MinimumSize = new Size(960, 620);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(3);

        BuildLayout();
        BindSettings();
        WireEvents();

        Controller.SessionTier = _settings.SessionTier;
        Controller.AllowDirectDeviceCommand = _settings.AllowDirectDeviceCommand;
        SetMode(_settings.InitialMode);
        ApplyIndicators(MachineIdentityBadge.Offline(_settings.SessionTier), BuildInitialIndicators(_settings));

        if (!string.IsNullOrWhiteSpace(_settings.StartupWarning))
            AppendLog("WARNING", _settings.StartupWarning);

        DarkModeHelper.FixControls(this);
        RefreshModeVisuals();
        RefreshActionButtons();
    }

    public void SetMode(AgentMode mode)
    {
        if (mode == AgentMode.Benchmark)
            throw new ArgumentException("UI only supports Copilot or Autonomous mode.", nameof(mode));

        Controller.Mode = mode;
        _chkAllowDeviceCommand.Enabled = !Controller.IsRunning && !_isConnectingOrStarting && mode == AgentMode.Autonomous;
        if (mode == AgentMode.Copilot)
            _chkAllowDeviceCommand.Checked = false;
        RefreshModeVisuals();
    }

    public void ApplyIndicators(MachineIdentityBadge badge, ConnectionIndicatorsSnapshot indicators)
    {
        ArgumentNullException.ThrowIfNull(badge);
        ArgumentNullException.ThrowIfNull(indicators);

        RunOnUi(() =>
        {
            _lblMachineBadge.Text = badge.BadgeText;
            _lblMachineBadge.ForeColor = !badge.Connected
                ? VacuumTheme.TextDim
                : badge.IsEmulator ? VacuumTheme.GlowGreen : VacuumTheme.GlowAmber;
            _toolTip.SetToolTip(_lblMachineBadge, badge.Tooltip);

            ApplyIndicatorLabel(_lblStrataIndicator, indicators.Strata);
            ApplyIndicatorLabel(_lblCloudIndicator, indicators.CloudLlm);
            ApplyIndicatorLabel(_lblElnIndicator, indicators.Eln3);
        });
    }

    public void ApplyWatchProgress(WatchProgressNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        RunOnUi(() =>
        {
            _lblWatchState.Text = notice.RecipeState.ToUpperInvariant();
            _lblWatchState.ForeColor = notice.RecipeState.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? VacuumTheme.GlowRed
                : notice.RecipeState.Equals("Paused", StringComparison.OrdinalIgnoreCase)
                    ? VacuumTheme.GlowAmber
                    : VacuumTheme.GlowGreen;

            _lblWatchPeriod.Text = notice.PeriodLabel;
            _lblWatchDeposits.Text = string.Create(CultureInfo.InvariantCulture, $"Deposits: {notice.ExecutedDepositsCount}");
            _lblWatchStep.Text = string.Create(CultureInfo.InvariantCulture, $"Step #{notice.StepSequence}");
            _lblWatchTelemetry.Text = string.IsNullOrWhiteSpace(notice.TelemetrySummary) ? notice.RawMessage : notice.TelemetrySummary;

            if (notice.TotalLoopIterations > 0)
            {
                int pct = Math.Clamp((int)Math.Round(notice.CurrentLoopIteration * 100.0 / notice.TotalLoopIterations), 0, 100);
                _pbarWatch.Value = pct;
            }
            else if (notice.TotalValue is double tot && tot > 0)
            {
                int pct = Math.Clamp((int)Math.Round(notice.ProgressValue * 100.0 / tot), 0, 100);
                _pbarWatch.Value = pct;
            }
        });
    }

    public void OnTurnStarted(int turnIndex) =>
        RunOnUi(() => AppendLog("SYSTEM", string.Create(CultureInfo.InvariantCulture, $"Turn {turnIndex + 1} started...")));

    public void OnAssistantResponse(int turnIndex, ProviderTurnResponse response) =>
        RunOnUi(() =>
        {
            if (!string.IsNullOrWhiteSpace(response.AssistantText))
                AppendLog("ASSISTANT", response.AssistantText);
            if (response.ToolCalls.Count > 0)
            {
                string names = string.Join(", ", response.ToolCalls.Select(tc => tc.Name));
                AppendLog("PLAN", $"Tool calls: {names}");
            }
        });

    public void OnToolCallStarted(string callId, string toolName, string argumentsJson) =>
        RunOnUi(() =>
        {
            int rowIdx = _gridTools.Rows.Add(
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                toolName,
                "Running",
                SummarizeText(argumentsJson, 90));
            _toolRowMap[callId] = rowIdx;
        });

    public void OnWatchProgress(WatchProgressNotice notice) => ApplyWatchProgress(notice);

    public void OnToolCallCompleted(string callId, string toolName, ToolCallReceipt receipt) =>
        RunOnUi(() =>
        {
            string status = receipt.IsError ? "Error" : "OK";
            string summary = SummarizeText(receipt.Text, 120);
            if (_toolRowMap.TryGetValue(callId, out int rowIdx) && rowIdx >= 0 && rowIdx < _gridTools.Rows.Count)
            {
                var row = _gridTools.Rows[rowIdx];
                row.Cells[2].Value = status;
                row.Cells[3].Value = summary;
            }
            else
            {
                _gridTools.Rows.Add(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), toolName, status, summary);
            }
            if (receipt.IsError)
                AppendLog("TOOL ERROR", $"{toolName}: {receipt.Text}");
        });

    public void OnSessionCompleted(SessionResult result) =>
        RunOnUi(() =>
        {
            AppendLog(
                "SESSION",
                string.Create(CultureInfo.InvariantCulture, $"Status={result.Status}, ToolCalls={result.ToolCallCount}, JournalSHA256={result.JournalSha256}"));
            _lblWatchState.Text = result.Status.ToUpperInvariant();
            _lblWatchState.ForeColor = result.Status == "completed" ? VacuumTheme.GlowGreen : VacuumTheme.GlowAmber;
        });

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Controller.ForceStop();
        var invoker = _mcpInvoker;
        _mcpInvoker = null;
        if (invoker is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await invoker.DisposeAsync().ConfigureAwait(false); }
                catch { }
            });
        }
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _monoFont.Dispose();
            _boldFont.Dispose();
            _probeHttpClient.Dispose();
            _llmHttpClient.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        _headerPanel.Paint += PaintFrameBorder;

        var topRow = new Panel { Dock = DockStyle.Top, Height = 32 };
        var leftFlow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        leftFlow.Controls.Add(_btnModeCopilot);
        leftFlow.Controls.Add(_btnModeAutonomous);
        leftFlow.Controls.Add(_chkAllowDeviceCommand);

        var rightFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        rightFlow.Controls.Add(_btnConnectRefresh);
        rightFlow.Controls.Add(_lblMachineBadge);

        topRow.Controls.Add(rightFlow);
        topRow.Controls.Add(leftFlow);

        var indicatorRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
        };
        indicatorRow.Controls.Add(_lblStrataIndicator);
        indicatorRow.Controls.Add(_lblCloudIndicator);
        indicatorRow.Controls.Add(_lblElnIndicator);

        _headerPanel.Controls.Add(indicatorRow);
        _headerPanel.Controls.Add(topRow);

        _watchBar.Paint += PaintFrameBorder;
        _lblWatchState.Font = _boldFont;
        _lblWatchPeriod.Font = _boldFont;
        _watchBar.Controls.Add(_lblWatchTelemetry);
        _watchBar.Controls.Add(_pbarWatch);
        _watchBar.Controls.Add(_lblWatchStep);
        _watchBar.Controls.Add(_lblWatchDeposits);
        _watchBar.Controls.Add(_lblWatchPeriod);
        _watchBar.Controls.Add(_lblWatchState);

        _txtTranscript.Font = _monoFont;
        var transcriptFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        transcriptFrame.Paint += PaintFrameBorder;
        transcriptFrame.Controls.Add(new DarkScrollHost(_txtTranscript) { Dock = DockStyle.Fill });

        _gridTools.Columns.Add("Time", "Time");
        _gridTools.Columns.Add("Tool", "Tool");
        _gridTools.Columns.Add("Status", "Status");
        _gridTools.Columns.Add("Summary", "Summary");
        _gridTools.Columns[0].FillWeight = 16;
        _gridTools.Columns[1].FillWeight = 24;
        _gridTools.Columns[2].FillWeight = 14;
        _gridTools.Columns[3].FillWeight = 46;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            Size = new Size(1000, 500),
        };
        split.Panel1.Padding = new Padding(3);
        split.Panel2.Padding = new Padding(3);
        split.Panel1.Controls.Add(transcriptFrame);
        split.Panel2.Controls.Add(_gridTools);

        var tabConsole = new TabPage("Operator Console") { Padding = new Padding(3) };
        tabConsole.Controls.Add(split);

        var tabConfig = new TabPage("Configuration & ELN3") { Padding = new Padding(6) };
        tabConfig.Controls.Add(BuildConfigPanel());

        _tabs.TabPages.Add(tabConsole);
        _tabs.TabPages.Add(tabConfig);

        _bottomBar.Paint += PaintFrameBorder;
        var actionFlow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        _btnRunGoal.BackColor = VacuumTheme.GlowGreen;
        _btnRunGoal.ForeColor = Color.White;
        _btnRunGoal.FlatAppearance.BorderColor = VacuumTheme.PanelBorder;
        _btnAbortRun.BackColor = VacuumTheme.PanelField;
        _btnAbortRun.ForeColor = VacuumTheme.GlowAmber;
        _btnAbortRun.FlatAppearance.BorderColor = VacuumTheme.GlowAmber;
        _btnForceStop.BackColor = VacuumTheme.PanelField;
        _btnForceStop.ForeColor = VacuumTheme.GlowRed;
        _btnForceStop.FlatAppearance.BorderColor = VacuumTheme.GlowRed;
        actionFlow.Controls.Add(_btnRunGoal);
        actionFlow.Controls.Add(_btnAbortRun);
        actionFlow.Controls.Add(_btnForceStop);
        actionFlow.Controls.Add(_btnSyncElnQuick);

        _bottomBar.Controls.Add(_txtPrompt);
        _bottomBar.Controls.Add(actionFlow);

        Controls.Add(_tabs);
        Controls.Add(CreateSpacer(DockStyle.Bottom));
        Controls.Add(_bottomBar);
        Controls.Add(CreateSpacer(DockStyle.Top));
        Controls.Add(_watchBar);
        Controls.Add(CreateSpacer(DockStyle.Top));
        Controls.Add(_headerPanel);

        Load += (_, _) =>
        {
            if (split.ClientSize.Width >= 420)
            {
                split.Panel1MinSize = 180;
                split.Panel2MinSize = 160;
                int target = (int)(split.ClientSize.Width * 0.62);
                int max = split.ClientSize.Width - split.Panel2MinSize;
                if (target >= split.Panel1MinSize && target <= max)
                    split.SplitterDistance = target;
            }
        };
    }

    private Control BuildConfigPanel()
    {
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        frame.Paint += PaintFrameBorder;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _cmbProtocol.Items.AddRange(["chat_completions", "openai_responses", "anthropic"]);

        AddFormRow(table, 0, "LLM Endpoint URI:", _txtEndpoint);
        AddFormRow(table, 1, "Provider Protocol:", _cmbProtocol);
        AddFormRow(table, 2, "Model ID (optional local):", _txtModel);
        AddFormRow(table, 3, "API Key Env Var:", _txtApiKeyEnv);
        AddFormRow(table, 4, "LLM Air-Gap Policy:", _chkAllowExternal);
        AddFormRow(table, 5, "Reasoning Effort:", _txtReasoningEffort);
        AddFormRow(table, 6, "APS.Mcp Executable:", _txtMcpBin);
        AddFormRow(table, 7, "APS.Mcp Config Path:", _txtMcpConfig);
        AddFormRow(table, 8, "Operator Skill Dir:", _txtSkillDir);
        AddFormRow(table, 9, "ELN3 HTTP Endpoint:", _txtElnEndpoint);
        AddFormRow(table, 10, "ELN3 API Key Env Var:", _txtElnApiKeyEnv);
        AddFormRow(table, 11, "ELN3 Air-Gap Policy:", _chkElnAllowExternal);
        AddFormRow(table, 12, "ELN3 MCP Command:", _txtElnMcpCommand);
        AddFormRow(table, 13, "ELN3 MCP Args:", _txtElnMcpArgs);
        AddFormRow(table, 14, "ELN3 MCP Tool Name:", _txtElnMcpTool);
        AddFormRow(table, 15, "ELN3 Local Export Dir:", _txtElnExportDir);
        AddFormRow(table, 16, "Auto-Sync Policy:", _chkAutoSyncEln);

        var elnRow = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4, 4, 4, 0) };
        _btnSyncElnNow.Dock = DockStyle.Left;
        elnRow.Controls.Add(_lblElnSyncStatus);
        elnRow.Controls.Add(_btnSyncElnNow);
        _lblElnSyncStatus.Padding = new Padding(10, 0, 0, 0);

        var scroll = new DarkScrollPanel { Dock = DockStyle.Fill };
        scroll.BeginUpdate();
        scroll.Controls.Add(elnRow);
        scroll.Controls.Add(table);
        scroll.EndUpdate();

        frame.Controls.Add(scroll);
        return frame;
    }

    private void BindSettings()
    {
        _txtEndpoint.Text = _settings.Endpoint.ToString();
        _cmbProtocol.SelectedIndex = _settings.Protocol switch
        {
            ProviderProtocol.OpenAiResponses => 1,
            ProviderProtocol.Anthropic => 2,
            _ => 0,
        };
        _txtModel.Text = _settings.ModelId;
        _txtApiKeyEnv.Text = _settings.ApiKeyEnvVar;
        _chkAllowExternal.Checked = _settings.AllowExternalEndpoints;
        _txtReasoningEffort.Text = _settings.ReasoningEffort;
        _txtMcpBin.Text = _settings.McpBin;
        _txtMcpConfig.Text = _settings.McpConfigPath;
        _txtSkillDir.Text = _settings.SkillDir;
        _chkAllowDeviceCommand.Checked = _settings.AllowDirectDeviceCommand;

        _txtElnEndpoint.Text = _settings.ElnEndpointUri?.ToString() ?? "";
        _txtElnApiKeyEnv.Text = _settings.ElnApiKeyEnvVar;
        _chkElnAllowExternal.Checked = _settings.ElnAllowExternal;
        _txtElnMcpCommand.Text = _settings.ElnMcpCommand ?? "";
        _txtElnMcpArgs.Text = _settings.ElnMcpArgs is null ? "" : string.Join(" ", _settings.ElnMcpArgs);
        _txtElnMcpTool.Text = _settings.ElnMcpToolName;
        _txtElnExportDir.Text = _settings.ElnLocalExportDir;
    }

    private void WireEvents()
    {
        _btnModeCopilot.Click += (_, _) => SetMode(AgentMode.Copilot);
        _btnModeAutonomous.Click += (_, _) => SetMode(AgentMode.Autonomous);
        _chkAllowDeviceCommand.CheckedChanged += (_, _) =>
            Controller.AllowDirectDeviceCommand = _chkAllowDeviceCommand.Checked;
        _btnConnectRefresh.Click += async (_, _) => await RefreshConnectionsAsync().ConfigureAwait(true);
        _btnRunGoal.Click += async (_, _) => await RunPromptAsync().ConfigureAwait(true);
        _btnAbortRun.Click += async (_, _) => await AbortRecipeOutOfBandAsync().ConfigureAwait(true);
        _btnForceStop.Click += (_, _) => ForceStopRun();
        _btnSyncElnQuick.Click += async (_, _) => await SyncToElnAsync().ConfigureAwait(true);
        _btnSyncElnNow.Click += async (_, _) => await SyncToElnAsync().ConfigureAwait(true);
        _txtPrompt.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await RunPromptAsync().ConfigureAwait(true);
            }
        };
    }

    private async Task RefreshConnectionsAsync()
    {
        if (_isConnectingOrStarting || Controller.IsRunning)
            return;

        _isConnectingOrStarting = true;
        RefreshActionButtons(busyOverride: true);
        try
        {
            var cfg = ReadCurrentSettings();
            var indicators = await AgentConnectionMonitor.EvaluateAsync(
                cfg.Endpoint,
                cfg.AllowExternalEndpoints,
                cfg.ApiKeyEnvVar,
                cfg.ToElnConfig(),
                _probeHttpClient).ConfigureAwait(true);

            MachineIdentityBadge badge;
            try
            {
                var invoker = await EnsureMcpConnectedAsync(cfg).ConfigureAwait(true);
                badge = await Controller.RefreshMachineBadgeAsync(invoker).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                badge = MachineIdentityBadge.Offline(Controller.SessionTier, ex.Message);
                AppendLog("MCP", $"Connection probe failed: {ex.Message}");
            }

            ApplyIndicators(badge, indicators);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", ex.Message);
        }
        finally
        {
            _isConnectingOrStarting = false;
            RefreshActionButtons();
        }
    }

    private async Task RunPromptAsync()
    {
        string prompt = _txtPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || _isConnectingOrStarting || Controller.IsRunning)
            return;

        _isConnectingOrStarting = true;
        RefreshActionButtons(busyOverride: true);
        try
        {
            var cfg = ReadCurrentSettings();
            EndpointGuard.Validate(cfg.Endpoint, cfg.AllowExternalEndpoints);

            string? resolvedModel = string.IsNullOrWhiteSpace(cfg.ModelId) ? null : cfg.ModelId.Trim();
            int? strataNCtx = null;
            if (EndpointGuard.IsLoopbackHost(cfg.Endpoint.Host))
            {
                try
                {
                    var info = await StrataSupervisor.ProbeRunningAsync(cfg.Endpoint, _probeHttpClient).ConfigureAwait(true);
                    resolvedModel ??= info.ModelId;
                    if (info.NCtx > 0) strataNCtx = info.NCtx;
                }
                catch when (!string.IsNullOrWhiteSpace(resolvedModel))
                {
                }
            }
            if (string.IsNullOrWhiteSpace(resolvedModel))
                throw new InvalidOperationException("Model ID is required for non-Strata endpoints.");

            var invoker = await EnsureMcpConnectedAsync(cfg).ConfigureAwait(true);
            var badge = await Controller.RefreshMachineBadgeAsync(invoker).ConfigureAwait(true);
            var indicators = await AgentConnectionMonitor.EvaluateAsync(
                cfg.Endpoint,
                cfg.AllowExternalEndpoints,
                cfg.ApiKeyEnvVar,
                cfg.ToElnConfig(),
                _probeHttpClient).ConfigureAwait(true);
            ApplyIndicators(badge, indicators);

            SkillBundle skill = SkillBundleLoader.Load(string.IsNullOrWhiteSpace(cfg.SkillDir) ? null : cfg.SkillDir);
            var providerCfg = new ProviderConfig(
                cfg.Protocol,
                cfg.Endpoint,
                resolvedModel,
                string.IsNullOrWhiteSpace(cfg.ApiKeyEnvVar) ? null : cfg.ApiKeyEnvVar.Trim(),
                cfg.AllowExternalEndpoints,
                MaxOutputTokens: 8192,
                ReasoningEffort: string.IsNullOrWhiteSpace(cfg.ReasoningEffort) ? null : cfg.ReasoningEffort.Trim());
            IModelProvider provider = ModelProviderFactory.Create(providerCfg, _llmHttpClient);

            string effectiveTier = Controller.Mode == AgentMode.Copilot ? "Read" : Controller.SessionTier;
            bool allowDeviceCmd = Controller.Mode == AgentMode.Autonomous && Controller.AllowDirectDeviceCommand;
            var sessionOpts = new SessionOptions(
                Controller.Mode,
                cfg.Protocol,
                skill.CombinedPromptText,
                SessionTier: effectiveTier,
                AllowDirectDeviceCommand: allowDeviceCmd,
                RequireValidatedUpload: Controller.Mode == AgentMode.Autonomous,
                MaxTurns: cfg.MaxTurns,
                ContextBudgetTokens: cfg.ContextBudgetTokens,
                StrataNCtx: strataNCtx,
                StripHistoricalThinking: true,
                SkillDigest: skill.SkillDigestSha256,
                SkillReadTool: new SandboxedSkillReadTool(skill.RootDirectory));

            string runDir = Path.Combine(Path.GetTempPath(), "aps-agent-runs");
            Directory.CreateDirectory(runDir);
            string journalPath = Path.Combine(runDir, $"ui-{DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)}.jsonl");

            _toolRowMap.Clear();
            ResetWatchBarForNewRun();
            AppendLog("USER", prompt);
            _txtPrompt.Clear();

            _isConnectingOrStarting = false;
            var sw = Stopwatch.StartNew();
            Task<SessionResult> runTask = Controller.RunGoalAsync(
                prompt,
                provider,
                invoker,
                sessionOpts,
                journalPath,
                this);
            RefreshActionButtons(runningOverride: true);
            SessionResult res = await runTask.ConfigureAwait(true);
            sw.Stop();

            var exposed = ToolPolicy.FilterDeclarations(Controller.Mode, invoker.Declarations, effectiveTier, allowDeviceCmd, null);
            string schemaDigest = ValidatedUploadGate.ComputeDigest(
                JsonSerializer.Serialize(exposed.OrderBy(d => d.Name, StringComparer.Ordinal).Select(d => new { d.Name, d.Description, d.Parameters })));
            AuditJournalWriter.WriteSidecar(
                Path.ChangeExtension(journalPath, ".sidecar.json"),
                new AuditSidecar(
                    Path.GetFileNameWithoutExtension(journalPath),
                    "operator-ui",
                    resolvedModel,
                    string.IsNullOrWhiteSpace(cfg.ReasoningEffort) ? "none" : cfg.ReasoningEffort.Trim(),
                    res.Status,
                    res.FinalAnswer,
                    sw.Elapsed.TotalSeconds,
                    res.TotalCostUsd,
                    res.ToolCallCount,
                    "",
                    schemaDigest,
                    skill.SkillDigestSha256,
                    Path.GetFileName(journalPath),
                    res.JournalSha256,
                    res.UsageRecords,
                    ErrorCategory: res.ErrorCategory));

            if (_chkAutoSyncEln.Checked)
                await SyncToElnAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _lblWatchState.Text = "CANCELLED";
            _lblWatchState.ForeColor = VacuumTheme.GlowRed;
            AppendLog("SYSTEM", "Run cancelled by operator (FORCE STOP).");
        }
        catch (Exception ex)
        {
            _lblWatchState.Text = "ERROR";
            _lblWatchState.ForeColor = VacuumTheme.GlowRed;
            AppendLog("ERROR", ex.Message);
        }
        finally
        {
            _isConnectingOrStarting = false;
            RefreshActionButtons();
        }
    }

    private async Task AbortRecipeOutOfBandAsync()
    {
        if (_mcpInvoker is null)
            return;

        bool retryAllowed = false;
        try
        {
            _btnAbortRun.Enabled = false;
            AppendLog("OPERATOR", "Sending out-of-band abort_recipe...");
            ToolCallReceipt receipt = await Controller.RequestAbortRecipeAsync(_mcpInvoker).ConfigureAwait(true);
            AppendLog("ABORT", receipt.IsError ? $"Error: {receipt.Text}" : receipt.Text);
            if (receipt.IsError)
                retryAllowed = true;
        }
        catch (Exception ex)
        {
            retryAllowed = true;
            AppendLog("ERROR", $"Abort failed: {ex.Message}");
        }
        finally
        {
            if (retryAllowed && Controller.IsRunning && _mcpInvoker is not null)
                _btnAbortRun.Enabled = true;
        }
    }

    private void ForceStopRun()
    {
        AppendLog("OPERATOR", "Force stopping LLM loop...");
        Controller.ForceStop();
    }

    private async Task SyncToElnAsync()
    {
        if (_isConnectingOrStarting || Controller.IsRunning)
            return;

        try
        {
            var cfg = ReadCurrentSettings();
            var elnClient = new ElnSyncClient(cfg.ToElnConfig());
            string workDir = Path.Combine(Path.GetTempPath(), "aps-agent-runs", "eln-staging");
            ElnSyncResult res = await Controller.SyncLatestRunToElnAsync(elnClient, workDir).ConfigureAwait(true);
            _lblElnSyncStatus.Text = $"Synced ({res.Mode}): {res.OutputLocation}";
            AppendLog("ELN3", _lblElnSyncStatus.Text);
        }
        catch (Exception ex)
        {
            _lblElnSyncStatus.Text = $"ELN3 error: {ex.Message}";
            AppendLog("ERROR", _lblElnSyncStatus.Text);
        }
    }

    private async Task<StdioMcpToolInvoker> EnsureMcpConnectedAsync(AgentUiSettings cfg)
    {
        string key = $"{cfg.McpBin}|{cfg.McpConfigPath}";
        if (_mcpInvoker is not null && string.Equals(_connectedMcpKey, key, StringComparison.Ordinal))
            return _mcpInvoker;

        if (_mcpInvoker is not null)
        {
            var old = _mcpInvoker;
            _mcpInvoker = null;
            _connectedMcpKey = null;
            await old.DisposeAsync().ConfigureAwait(true);
        }

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.McpConfigPath))
        {
            args.Add("--config");
            args.Add(Path.GetFullPath(cfg.McpConfigPath));
        }

        string telemetryDir = Path.Combine(Path.GetTempPath(), "aps-agent-runs", "telemetry");
        Directory.CreateDirectory(telemetryDir);
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["APS_MCP_TELEMETRY_DIR"] = telemetryDir,
        };
        string? wd = Path.GetDirectoryName(cfg.McpBin);
        StdioMcpToolInvoker connected = await StdioMcpToolInvoker.ConnectAsync(
            cfg.McpBin,
            args,
            env,
            string.IsNullOrWhiteSpace(wd) ? null : wd).ConfigureAwait(true);

        if (IsDisposed || Disposing)
        {
            await connected.DisposeAsync().ConfigureAwait(false);
            throw new OperationCanceledException();
        }

        _mcpInvoker = connected;
        _connectedMcpKey = key;
        return connected;
    }

    private AgentUiSettings ReadCurrentSettings()
    {
        if (!AgentUiSettings.TryParseHttpUri(_txtEndpoint.Text, out var endpoint))
            throw new InvalidOperationException("Endpoint URI must be an absolute HTTP/HTTPS URL.");

        Uri? elnUri = null;
        if (!string.IsNullOrWhiteSpace(_txtElnEndpoint.Text))
        {
            if (!AgentUiSettings.TryParseHttpUri(_txtElnEndpoint.Text, out elnUri))
                throw new InvalidOperationException("ELN3 endpoint URI must be an absolute HTTP/HTTPS URL.");
        }

        ProviderProtocol protocol = _cmbProtocol.SelectedIndex switch
        {
            1 => ProviderProtocol.OpenAiResponses,
            2 => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.ChatCompletions,
        };

        return new AgentUiSettings
        {
            InitialMode = Controller.Mode,
            Protocol = protocol,
            Endpoint = endpoint!,
            ModelId = _txtModel.Text.Trim(),
            ApiKeyEnvVar = _txtApiKeyEnv.Text.Trim(),
            AllowExternalEndpoints = _chkAllowExternal.Checked,
            McpBin = string.IsNullOrWhiteSpace(_txtMcpBin.Text) ? "APS.Mcp.exe" : _txtMcpBin.Text.Trim(),
            McpConfigPath = _txtMcpConfig.Text.Trim(),
            SkillDir = _txtSkillDir.Text.Trim(),
            SessionTier = Controller.SessionTier,
            AllowDirectDeviceCommand = _chkAllowDeviceCommand.Checked,
            ReasoningEffort = _txtReasoningEffort.Text.Trim(),
            MaxTurns = _settings.MaxTurns,
            ContextBudgetTokens = _settings.ContextBudgetTokens,
            ElnEndpointUri = elnUri,
            ElnApiKeyEnvVar = _txtElnApiKeyEnv.Text.Trim(),
            ElnMcpCommand = string.IsNullOrWhiteSpace(_txtElnMcpCommand.Text) ? null : _txtElnMcpCommand.Text.Trim(),
            ElnMcpArgs = AgentUiSettings.SplitSpaceTokens(_txtElnMcpArgs.Text),
            ElnMcpToolName = string.IsNullOrWhiteSpace(_txtElnMcpTool.Text) ? "upload_run_bundle" : _txtElnMcpTool.Text.Trim(),
            ElnLocalExportDir = string.IsNullOrWhiteSpace(_txtElnExportDir.Text) ? _settings.ElnLocalExportDir : _txtElnExportDir.Text.Trim(),
            ElnAllowExternal = _chkElnAllowExternal.Checked,
        };
    }

    private void ResetWatchBarForNewRun()
    {
        _lblWatchState.Text = "RUNNING";
        _lblWatchState.ForeColor = VacuumTheme.GlowGreen;
        _lblWatchPeriod.Text = "Period -/-";
        _lblWatchDeposits.Text = "Deposits: 0";
        _lblWatchStep.Text = "Step #0";
        _pbarWatch.Value = 0;
        _lblWatchTelemetry.Text = "Starting session...";
    }

    private static ConnectionIndicatorsSnapshot BuildInitialIndicators(AgentUiSettings settings)
    {
        bool isLoopback = EndpointGuard.IsLoopbackHost(settings.Endpoint.Host);
        IndicatorStatus strata = isLoopback
            ? new IndicatorStatus(IndicatorState.Off, "STRATA STANDBY", "Click Connect / Probe to check Strata")
            : new IndicatorStatus(IndicatorState.Off, "STRATA STANDBY", "External endpoint selected");
        IndicatorStatus cloud = isLoopback
            ? new IndicatorStatus(IndicatorState.Off, "CLOUD STANDBY", "Local loopback endpoint active")
            : !settings.AllowExternalEndpoints
                ? new IndicatorStatus(IndicatorState.Error, "CLOUD LOCKED", $"External host '{settings.Endpoint.Host}' blocked by air-gap lock")
                : new IndicatorStatus(IndicatorState.Warning, "CLOUD STANDBY", "Click Connect / Probe to verify API key");

        IndicatorStatus eln;
        if (!string.IsNullOrWhiteSpace(settings.ElnMcpCommand))
            eln = new IndicatorStatus(IndicatorState.Ready, "ELN3 MCP", settings.ElnMcpCommand);
        else if (settings.ElnEndpointUri is not null)
            eln = !EndpointGuard.IsLoopbackHost(settings.ElnEndpointUri.Host) && !settings.ElnAllowExternal
                ? new IndicatorStatus(IndicatorState.Error, "ELN3 LOCKED", $"External host '{settings.ElnEndpointUri.Host}' blocked by air-gap lock")
                : new IndicatorStatus(IndicatorState.Ready, "ELN3 HTTP", settings.ElnEndpointUri.ToString());
        else
            eln = new IndicatorStatus(IndicatorState.Warning, "ELN3 LOCAL", settings.ElnLocalExportDir);

        return new ConnectionIndicatorsSnapshot(strata, cloud, eln);
    }

    private void RefreshModeVisuals()
    {
        bool copilot = Controller.Mode == AgentMode.Copilot;
        _btnModeCopilot.BackColor = copilot ? VacuumTheme.GlowGreen : VacuumTheme.PanelBackground;
        _btnModeCopilot.ForeColor = copilot ? Color.White : VacuumTheme.TextDim;
        _btnModeCopilot.FlatAppearance.BorderColor = copilot ? VacuumTheme.GlowGreen : VacuumTheme.PanelBorder;

        _btnModeAutonomous.BackColor = !copilot ? VacuumTheme.GlowAmber : VacuumTheme.PanelBackground;
        _btnModeAutonomous.ForeColor = !copilot ? Color.Black : VacuumTheme.TextDim;
        _btnModeAutonomous.FlatAppearance.BorderColor = !copilot ? VacuumTheme.GlowAmber : VacuumTheme.PanelBorder;
    }

    private void RefreshActionButtons(bool? runningOverride = null, bool busyOverride = false)
    {
        bool running = runningOverride ?? Controller.IsRunning;
        bool busy = running || busyOverride || _isConnectingOrStarting;
        _btnRunGoal.Enabled = !busy;
        _btnConnectRefresh.Enabled = !busy;
        _btnSyncElnQuick.Enabled = !busy;
        _btnSyncElnNow.Enabled = !busy;
        _btnAbortRun.Enabled = running && _mcpInvoker is not null;
        _btnForceStop.Enabled = running;
        _btnModeCopilot.Enabled = !busy;
        _btnModeAutonomous.Enabled = !busy;
        _chkAllowDeviceCommand.Enabled = !busy && Controller.Mode == AgentMode.Autonomous;
    }

    private void ApplyIndicatorLabel(Label label, IndicatorStatus status)
    {
        label.Text = status.Label;
        label.ForeColor = status.State switch
        {
            IndicatorState.Ready => VacuumTheme.GlowGreen,
            IndicatorState.Warning => VacuumTheme.GlowAmber,
            IndicatorState.Error => VacuumTheme.GlowRed,
            _ => VacuumTheme.TextDim,
        };
        _toolTip.SetToolTip(label, status.Detail);
    }

    private void AppendLog(string tag, string message)
    {
        string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _txtTranscript.AppendText($"[{stamp}] [{tag}] {message}\n");
        _txtTranscript.SelectionStart = _txtTranscript.TextLength;
        _txtTranscript.ScrollToCaret();
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
            return;
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            if (!IsHandleCreated)
                return;
            try
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed && !Disposing)
                        action();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
        else
        {
            action();
        }
    }

    private static Label CreateBadgeLabel(string text)
    {
        var lbl = new Label
        {
            Text = text,
            AutoSize = true,
            BackColor = VacuumTheme.PanelField,
            ForeColor = VacuumTheme.TextDim,
            BorderStyle = BorderStyle.None,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(4, 2, 0, 2),
        };
        lbl.Paint += PaintBadgeBorder;
        return lbl;
    }

    private static void PaintBadgeBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Control c) return;
        using var pen = new Pen(VacuumTheme.PanelBorder);
        e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
    }

    private static Panel CreateSpacer(DockStyle dock) => new() { Dock = dock, Height = 3 };

    private static void PaintFrameBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Control c) return;
        using var pen = new Pen(SystemColors.ControlDark);
        e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
    }

    private static void AddFormRow(TableLayoutPanel table, int row, string caption, Control editor)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 8, 4),
        };
        editor.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(lbl, 0, row);
        table.Controls.Add(editor, 1, row);
    }

    private static string SummarizeText(string text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        string flat = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return flat.Length <= maxLen ? flat : flat[..maxLen] + "...";
    }
}
