using System.Globalization;
using System.Linq;
using APS.Controls.Theme;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.GUIControls;
using Contracts = APS.ProcessPanel.Contracts;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Loaded via Panels.xml like any other IBaseCustomPanel. System-specific
/// implementations live in the GUI DLL.
/// </summary>
public class BaseProcessPanel : UserControl, IBaseCustomPanel, IProcessPanel
{
    // Monitor DPI factor for WinForms-control sizing (matches ClusterProcessPanel).
    // Paint methods derive their own factor from Graphics.DpiY instead.
    private float DpiF => DeviceDpi / 96f;

    // Pulled from VacuumTheme so Palette.xml applies.
    private static Color ColorBackground  => VacuumTheme.PanelBackground;
    private static Color ColorTextPrimary => VacuumTheme.TextPrimary;
    private static Color ColorTextDim     => VacuumTheme.TextDim;
    private static Color ColorBorder      => VacuumTheme.PanelBorder;
    // Header is a slightly lighter navy than PanelBackground — kept local as
    // it's the only per-panel decoration not currently worth a palette entry.
    private static readonly Color ColorHeaderBg = Color.FromArgb(22, 32, 50);

    private RecipeInfo? _currentRecipe;
    private ProcessStepInfo? _currentStep;
    private Contracts.DriveInfo[]? _currentDrives;
    private Contracts.ValveInfo[]? _currentValves;

    private readonly Panel _headerPanel;
    private readonly Panel _schematicPanel;
    private readonly Panel _leftPanel;
    private readonly Panel _rightPanel;
    private readonly Panel _bottomPanel;
    private readonly Panel _logPanel;
    private readonly RichTextBox _logBox;
    private readonly CurrentStepCard _stepCard;
    private readonly LoopProgressCard _loopCard;
    private readonly LoopProgressTracker _loopTracker = new();

    // _rowsById is keyed by every reading id a row consumes — its own and its
    // secondary trace's — so UpdateReadings routes both to the same control.
    // _rowsByDevice groups rows by ReadingSpec.DeviceId for PSU visibility.
    private readonly Dictionary<string, ReadingRowControl> _rowsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ReadingRowControl>> _rowsByDevice = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _readingIds = new();
    private FlowLayoutPanel? _leftStack;
    private FlowLayoutPanel? _rightStack;
    private FlowLayoutPanel? _centerStack;
    private readonly Panel? _centerPanel;

    private Label? _powerHeader;
    private readonly HashSet<string> _powerRowIds = new(StringComparer.Ordinal);

    private bool _logCollapsed = true;
    private Label _logHeaderLabel = null!;
    private const int LogCollapsedHeight = 24;
    private const int LogExpandedHeight  = 180;

    // StreamWriter is not thread-safe and EventLogBus.Entry fires on arbitrary
    // publisher threads. _logSync serialises every WriteLine with Init/Dispose
    // so concurrent publishers can't interleave the file and a publisher can't
    // WriteLine onto a writer the UI thread just disposed (ObjectDisposedException
    // on a background thread → the crash path).
    private readonly object _logSync = new();
    private StreamWriter? _logWriter;

    public string ID { get; set; } = "Process";
    public string Caption { get; set; } = "Process";
    public DeviceType DeviceType => DeviceType.System;
    public event EventHandler<CustomEventArgs>? SendCommandEvent;

    protected RecipeInfo? CurrentRecipe => _currentRecipe;
    protected ProcessStepInfo? CurrentStep => _currentStep;
    protected Contracts.DriveInfo[]? CurrentDrives => _currentDrives;
    protected Contracts.ValveInfo[]? CurrentValves => _currentValves;

    protected Panel HeaderPanel => _headerPanel;
    protected Panel SchematicPanel => _schematicPanel;
    protected Panel LeftPanel => _leftPanel;
    protected Panel RightPanel => _rightPanel;
    protected Panel BottomPanel => _bottomPanel;
    protected Panel LogPanel => _logPanel;

    /// <summary>
    /// Used by the host (FrmMain) to persist and restore the splitter position
    /// across sessions. Setting only takes effect if the left panel was actually created.
    /// </summary>
    public int CurrentLeftPanelWidth
    {
        get => _leftPanel.Width;
        set
        {
            if (LeftPanelWidth > 0 && value > 0)
                _leftPanel.Width = value;
        }
    }

    /// <summary>
    /// See CurrentLeftPanelWidth.
    /// </summary>
    public int CurrentRightPanelWidth
    {
        get => _rightPanel.Width;
        set
        {
            if (RightPanelWidth > 0 && value > 0)
                _rightPanel.Width = value;
        }
    }

    /// <summary>
    /// See CurrentLeftPanelWidth. Zero when the subclass did not opt in.
    /// </summary>
    public int CurrentCenterPanelWidth
    {
        get => _centerPanel?.Width ?? 0;
        set
        {
            if (_centerPanel != null && value > 0)
                _centerPanel.Width = value;
        }
    }

    // Subclasses return 0 to hide a region.
    // Convention: these return DEVICE pixels — overrides must scale their 96-DPI
    // base values by DpiF (DeviceDpi/96), as ClusterProcessPanel does.
    protected virtual int PanelHeaderHeight => (int)(72 * DpiF);
    protected virtual int LeftPanelWidth => (int)(220 * DpiF);
    protected virtual int RightPanelWidth => (int)(200 * DpiF);
    /// <summary>
    /// Width of the Process reading column that docks on the right edge of the
    /// centre host, between the schematic and the right splitter. Zero (the
    /// default) builds no column: <see cref="ReadingGroup.Process"/> rows then
    /// fall back to the left panel after Substrate. The schematic is square and
    /// height-bound, so on a wide screen this column takes width the schematic
    /// was not using.
    /// </summary>
    protected virtual int CenterPanelWidth => 0;
    protected virtual int BottomPanelHeight => (int)(40 * DpiF);
    protected virtual int LogPanelHeight => (int)(180 * DpiF);

    public BaseProcessPanel()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        BackColor = ColorBackground;
        ForeColor = ColorTextPrimary;

        _bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = BottomPanelHeight,
            BackColor = ColorBackground
        };
        if (BottomPanelHeight > 0) Controls.Add(_bottomPanel);

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(170, 190, 210),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Visible = false,
        };
        _logHeaderLabel = new Label
        {
            Text = "LOG ▸",
            Dock = DockStyle.Top,
            Height = (int)(LogCollapsedHeight * DpiF),
            Font = ScaledFont.Create(8.5f),
            ForeColor = ColorTextDim,
            BackColor = Color.FromArgb(18, 28, 45),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 10, 0),
            Cursor = Cursors.Hand,
        };
        _logHeaderLabel.Click += (_, _) => ToggleLogDrawer();

        _logPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = (int)(LogCollapsedHeight * DpiF),
            BackColor = Color.FromArgb(10, 18, 30)
        };
        _logPanel.Controls.Add(_logBox);
        _logPanel.Controls.Add(_logHeaderLabel);
        Controls.Add(_logPanel);

        InitLogFile();
        EventLogBus.Entry += OnBusEntryForBottomDrawer;

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = PanelHeaderHeight,
            BackColor = ColorHeaderBg
        };
        EnableDoubleBuffering(_headerPanel);
        _headerPanel.Paint += (_, e) => PaintHeader(e.Graphics, _headerPanel.ClientRectangle);
        if (PanelHeaderHeight > 0) Controls.Add(_headerPanel);

        // Left panel + draggable splitter. WinForms processes docked controls
        // in reverse z-order: the LAST-added Dock.Left wins the outermost
        // position. So we add the splitter FIRST and the panel SECOND, which
        // puts the panel on the outer edge and the splitter immediately
        // inboard of it (between the panel and the center fill).
        _leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = LeftPanelWidth,
            BackColor = ColorBackground
        };
        if (LeftPanelWidth > 0)
        {
            var leftSplitter = SideSplitterFactory.Create(DockStyle.Left, minPanelWidth: 160);
            Controls.Add(leftSplitter);
            Controls.Add(_leftPanel);
        }

        // Right panel + draggable splitter (same ordering — splitter first).
        _rightPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = RightPanelWidth,
            BackColor = ColorBackground
        };
        if (RightPanelWidth > 0)
        {
            var rightSplitter = SideSplitterFactory.Create(DockStyle.Right, minPanelWidth: 180);
            Controls.Add(rightSplitter);
            Controls.Add(_rightPanel);
        }

        _schematicPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ColorBackground
        };
        EnableDoubleBuffering(_schematicPanel);
        _schematicPanel.Paint += (_, e) => OnPaintSchematic(e.Graphics, _schematicPanel.ClientRectangle);

        _stepCard = new CurrentStepCard
        {
            Dock = DockStyle.Bottom,
            Height = (int)(64 * DpiF),
        };

        // Loop progress — hidden unless the running recipe is inside an outer
        // `loop` block. Added AFTER the step card so it docks below it: WinForms
        // gives the last-added Dock.Bottom child the outer edge (same rule the
        // left/right splitters rely on above).
        _loopCard = new LoopProgressCard
        {
            Dock = DockStyle.Bottom,
            Height = (int)(58 * DpiF),
        };

        // The schematic panel is the "Fill" container; the cards are its bottom dockers.
        // Dock order (WinForms lays out the LAST-added child first, outermost):
        // loop card and step card take the bottom across the full width, then
        // the Process column and its splitter take the right edge above them,
        // and the schematic fills what is left.
        var centerHost = new Panel { Dock = DockStyle.Fill, BackColor = ColorBackground };
        centerHost.Controls.Add(_schematicPanel);
        if (CenterPanelWidth > 0)
        {
            _centerPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = CenterPanelWidth,
                BackColor = ColorBackground,
            };
            var centerSplitter = SideSplitterFactory.Create(DockStyle.Right, minPanelWidth: 180);
            centerHost.Controls.Add(centerSplitter);
            centerHost.Controls.Add(_centerPanel);
        }
        centerHost.Controls.Add(_stepCard);
        centerHost.Controls.Add(_loopCard);
        Controls.Add(centerHost);
        Controls.SetChildIndex(centerHost, 0);

        PopulateLayout();

        BuildReadingRows();
    }

    // Panel.DoubleBuffered is protected — reflection is the standard way to set
    // it on a stock Panel without subclassing. Without it, owner-drawn panels
    // repainted on a timer flicker (visible erase-to-background before redraw).
    private static void EnableDoubleBuffering(Panel panel)
        => typeof(Panel).GetProperty("DoubleBuffered",
               System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
           .SetValue(panel, true);

    private void PaintHeader(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        OnPaintHeader(g, bounds);
    }

    protected virtual void OnPaintHeader(Graphics g, Rectangle bounds)
    {
        using Font nameFont  = ScaledFont.Create(11f, FontStyle.Bold);
        using Font stepFont  = ScaledFont.Create(9f, FontStyle.Regular);
        using Font stateFont = ScaledFont.Create(9f, FontStyle.Bold);
        using Font timeFont  = new("Consolas", 9f, FontStyle.Bold);
        using SolidBrush textBrush = new(ColorTextPrimary);
        using SolidBrush dimBrush  = new(ColorTextDim);

        // Scale pixel geometry by the monitor DPI factor (fonts are point-sized
        // and GDI+-scaled, so they need no adjustment here).
        float s = g.DpiY / 96f;

        if (_currentRecipe == null)
        {
            g.DrawString("No recipe loaded", stepFont, dimBrush, 12 * s, (bounds.Height - stepFont.Height) / 2f);
            return;
        }

        var r = _currentRecipe;

        Color stateColor = r.State switch
        {
            RecipeState.Running  => Color.FromArgb(80, 200, 120),
            RecipeState.Paused   => Color.FromArgb(255, 200, 50),
            RecipeState.Error    => Color.FromArgb(255, 80, 80),
            RecipeState.Aborting => Color.FromArgb(255, 120, 50),
            RecipeState.Complete => Color.Cyan,
            _                    => ColorTextDim
        };

        float inset  = 12 * s;
        float line1Y = 8 * s;
        string nameText = $"Recipe: {r.Name}";
        g.DrawString(nameText, nameFont, textBrush, inset, line1Y);

        string stateText = $"● {r.State.ToString().ToUpperInvariant()}";
        SizeF stateSize = g.MeasureString(stateText, stateFont);
        using (SolidBrush stateBrush = new(stateColor))
            g.DrawString(stateText, stateFont, stateBrush,
                bounds.Right - stateSize.Width - inset, 10 * s);

        // When a multi-stack `loop` is active, prefix with "Stack N/M" so the
        // operator can see deposition progress through a stacked recipe at
        // a glance (Delphi SCH_VAL_LPROG / LTOT parity).
        // line2Y keeps the legacy 32px baseline at 96 DPI but is floored by the
        // name font's measured height so the two lines never overlap as fonts
        // grow on high-DPI monitors.
        float line2Y = Math.Max(32 * s, line1Y + nameFont.GetHeight(g) + 2 * s);
        string stackPrefix = r.TotalStacks > 0
            ? $"Stack {r.CurrentStack}/{r.TotalStacks}  \u00b7  "
            : "";
        string stepText = $"{stackPrefix}Step {r.CurrentStep} / {r.TotalSteps} \u2014 {r.StepDescription}";
        g.DrawString(stepText, stepFont, dimBrush, inset, line2Y);

        string elapsed = FormatHms(r.Elapsed);
        string remaining = r.EstimatedRemaining.HasValue ? FormatHms(r.EstimatedRemaining.Value) : "--:--";
        string timeText = $"Elapsed ";
        string timeText2 = $"  \u00b7  Remaining ";
        SizeF elapsedSize = g.MeasureString(elapsed, timeFont);
        SizeF remainingSize = g.MeasureString(remaining, timeFont);
        SizeF label1Size = g.MeasureString(timeText, stepFont);
        SizeF label2Size = g.MeasureString(timeText2, stepFont);
        float totalW = label1Size.Width + elapsedSize.Width + label2Size.Width + remainingSize.Width;
        float tx = bounds.Right - totalW - inset;
        g.DrawString(timeText,  stepFont, dimBrush,  tx, line2Y + 1 * s); tx += label1Size.Width;
        g.DrawString(elapsed,   timeFont, textBrush, tx, line2Y);          tx += elapsedSize.Width;
        g.DrawString(timeText2, stepFont, dimBrush,  tx, line2Y + 1 * s); tx += label2Size.Width;
        g.DrawString(remaining, timeFont, textBrush, tx, line2Y);

        int barY = bounds.Height - (int)(10 * s);
        int barH = (int)(6 * s);
        var barBg = new Rectangle((int)inset, barY, bounds.Width - (int)(24 * s), barH);
        using (SolidBrush bgBrush = new(Color.FromArgb(34, 51, 76)))
            g.FillRectangle(bgBrush, barBg);

        if (r.TotalSteps > 0)
        {
            float progress = (float)r.CurrentStep / r.TotalSteps;
            int fillW = (int)(barBg.Width * progress);
            if (fillW > 0)
            {
                var fillRect = new Rectangle(barBg.Left, barBg.Top, fillW, barBg.Height);
                using SolidBrush fill = new(stateColor);
                g.FillRectangle(fill, fillRect);
            }

            using Pen tickPen = new(Color.FromArgb(10, 18, 30), 1f);
            for (int i = 1; i < r.TotalSteps; i++)
            {
                int tx2 = barBg.Left + (int)(barBg.Width * i / (float)r.TotalSteps);
                g.DrawLine(tickPen, tx2, barBg.Top, tx2, barBg.Bottom);
            }
        }

    }

    // internal (not private): shared with LoopProgressCard, which has the
    // identical TotalHours>=1 ? "HH:MM:SS" : "MM:SS" formatting need.
    internal static string FormatHms(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void Update(object[] args)
    {
        // Legacy bridge — the host should call IProcessPanel methods directly.
        // This exists for compatibility with the generic panel loading system.
    }

    public void UpdateRecipe(RecipeInfo info)
    {
        _currentRecipe = info;
        _stepCard.ApplyRecipeProgress(info.CurrentStep, info.TotalSteps);
        _loopCard.Apply(_loopTracker.Update(info));
        OnRecipeChanged(info);
        _headerPanel.Invalidate();
    }

    public void UpdateStep(ProcessStepInfo info)
    {
        _currentStep = info;
        _stepCard.ApplyStep(info);
        OnStepChanged(info);
        _schematicPanel.Invalidate();
    }

    public void UpdateDrives(Contracts.DriveInfo[] drives)
    {
        _currentDrives = drives;
        OnDrivesChanged(drives);
        _schematicPanel.Invalidate();
    }

    public void UpdateValves(Contracts.ValveInfo[] valves)
    {
        _currentValves = valves;
        OnValvesChanged(valves);
    }

    public void UpdateSources(IReadOnlyDictionary<int, Contracts.FlangeSourceInfo> flangeSources)
    {
        OnSourcesChanged(flangeSources);
    }

    /// <summary>Called once after construction. Add controls to Left/Right/Bottom panels.</summary>
    protected virtual void PopulateLayout() { }

    protected virtual void OnPaintSchematic(Graphics g, Rectangle bounds) { }

    /// <summary>Recipe state changed (new step, state transition, timing update).</summary>
    protected virtual void OnRecipeChanged(RecipeInfo info) { }

    protected virtual void OnStepChanged(ProcessStepInfo info) { }

    protected virtual void OnDrivesChanged(Contracts.DriveInfo[] drives) { }

    protected virtual void OnValvesChanged(Contracts.ValveInfo[] valves) { }

    /// <summary>Flange-to-source map available (pushed at startup and after each Sources.xml reload).</summary>
    protected virtual void OnSourcesChanged(IReadOnlyDictionary<int, Contracts.FlangeSourceInfo> flangeSources) { }

    protected void SendCommand(object[] data)
    {
        SendCommandEvent?.Invoke(this, new CustomEventArgs(data));
    }

    private void InitLogFile()
    {
        try
        {
            string logDir = APS.SystemControl.Core.CoreClass.AppPaths.LogsDir;
            Directory.CreateDirectory(logDir);
            // Invariant: RunArtifacts derives the launch date from this name, and the day
            // separators/entries below are invariant too — a Buddhist-calendar culture would
            // otherwise put the file 543 years from its own lines.
            var now = DateTime.Now;
            string logPath = Path.Combine(logDir,
                $"Process_{now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}.log");
            lock (_logSync)
            {
                _logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
                _logWriter.WriteLine($"--- Process log started {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ---");
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Legacy shim — bottom-drawer rendering and file logging are handled by
    /// the <see cref="EventLogBus"/> subscriber.
    /// </summary>
    public void Log(string source, string message)
        => EventLogBus.PublishClassified(source, message);

    /// <summary>
    /// Date of the last emitted day-separator. Control runs 24/7, so rather than
    /// stamping the full date on every line we emit one separator row when the
    /// day rolls over (or on the first line) and keep bare time on entries.
    /// Guarded by <see cref="_logSync"/>.
    /// </summary>
    private DateTime _lastDrawerDate = DateTime.MinValue;

    private void OnBusEntryForBottomDrawer(EventLogEntry entry)
    {
        string entryLine =
            $"{entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  [{entry.Source}]  {entry.Message}";

        string uiText;
        lock (_logSync)
        {
            if (entry.Timestamp.Date != _lastDrawerDate.Date)
            {
                _lastDrawerDate = entry.Timestamp.Date;
                string separator =
                    "------ " + entry.Timestamp.ToString("yyyy-MM-dd  dddd", CultureInfo.InvariantCulture) + " ------";
                _logWriter?.WriteLine(separator);
                _logWriter?.WriteLine(entryLine);
                uiText = separator + Environment.NewLine + entryLine;
            }
            else
            {
                _logWriter?.WriteLine(entryLine);
                uiText = entryLine;
            }
        }

        // Marshal via a control that actually HAS a handle: before this panel
        // is parented (construction window, ReloadGuiConfig teardown),
        // InvokeRequired is false on any thread and a direct call would create
        // the RichTextBox handle on a pumpless publisher thread. Drop the entry
        // when nothing can marshal yet, and always BeginInvoke so the
        // IsDisposed re-check runs on the UI thread — closing the disposal
        // TOCTOU that threw on the publisher thread.
        if (_logBox.IsHandleCreated && !_logBox.InvokeRequired)
        {
            AppendLogLine(uiText);
            return;
        }
        var marshalTarget = _logBox.IsHandleCreated ? (Control)_logBox : FindForm();
        if (marshalTarget is null || !marshalTarget.IsHandleCreated) return;
        try
        {
            marshalTarget.BeginInvoke(() =>
            {
                if (!_logBox.IsDisposed) AppendLogLine(uiText);
            });
        }
        catch (InvalidOperationException) { /* disposed between check and post */ }
    }

    private void AppendLogLine(string line)
    {
        _logBox.AppendText(line + Environment.NewLine);
        if (_logBox.TextLength > 200_000)
        {
            _logBox.SelectionStart = 0;
            _logBox.SelectionLength = _logBox.GetFirstCharIndexFromLine(100);
            _logBox.SelectedText = "";
        }
        if (_logCollapsed) UpdateCollapsedHeaderText();
    }

    private void ToggleLogDrawer()
    {
        _logCollapsed = !_logCollapsed;
        _logPanel.Height = _logCollapsed ? (int)(LogCollapsedHeight * DpiF) : (int)(LogExpandedHeight * DpiF);
        _logBox.Visible  = !_logCollapsed;
        _logHeaderLabel.Text = _logCollapsed ? "LOG ▸" : "LOG ▾";
        UpdateCollapsedHeaderText();
    }

    private void UpdateCollapsedHeaderText()
    {
        if (!_logCollapsed)
        {
            _logHeaderLabel.Text = "LOG ▾";
            return;
        }
        string mostRecent = GetLastLogLine();
        _logHeaderLabel.Text = string.IsNullOrEmpty(mostRecent)
            ? "LOG ▸"
            : $"LOG ▸  {mostRecent}";
    }

    private string GetLastLogLine()
    {
        string text = _logBox.Text;
        if (string.IsNullOrEmpty(text)) return string.Empty;
        int lastNewline = text.LastIndexOf('\n', text.Length - 2);
        if (lastNewline < 0) return text.TrimEnd('\r', '\n');
        return text.Substring(lastNewline + 1).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Returned in display order — the base class groups by Group when building rows.
    /// </summary>
    protected virtual IEnumerable<ReadingSpec> DefineReadings() => [];

    private void BuildReadingRows()
    {
        var specs = DefineReadings().ToList();
        if (specs.Count == 0)
        {
            // No readings — still let subclass add extras if it wants to build
            // its own stacks. Pass nulls so subclass knows nothing was created.
            PopulateExtraSections(null, null);
            return;
        }

        // Group specs by ReadingGroup. Left panel gets Pressure + MassFlow +
        // Substrate, in that order — pressure is the reading an operator checks
        // first, so it sits at the top of the left column above the gas flows
        // it is being pumped against. Right panel gets Power. Other falls into
        // Substrate.
        var grouped = specs
            .GroupBy(s => s.Group)
            .ToDictionary(g => g.Key, g => g.ToList());

        var leftPressure  = grouped.GetValueOrDefault(ReadingGroup.Pressure);
        var leftMass      = grouped.GetValueOrDefault(ReadingGroup.MassFlow);
        var leftSubstrate = Combine(grouped, ReadingGroup.Substrate, ReadingGroup.Other);
        var rightPower    = grouped.GetValueOrDefault(ReadingGroup.Power);
        var centerProcess = grouped.GetValueOrDefault(ReadingGroup.Process);

        // Process rows need the centre column; without one they join the
        // left panel so a subclass never silently loses readings.
        List<ReadingSpec>? leftProcess = null;
        if (_centerPanel is null && centerProcess is { Count: > 0 })
        {
            leftProcess = centerProcess;
            centerProcess = null;
        }

        bool leftHasContent  = (leftPressure?.Count ?? 0) + (leftMass?.Count ?? 0)
                             + (leftSubstrate?.Count ?? 0) + (leftProcess?.Count ?? 0) > 0;
        bool rightHasContent = (rightPower?.Count ?? 0) > 0;

        if (leftHasContent)
        {
            _leftStack = CreateStack(LeftPanel);
            AddSection(_leftStack, "Pressures / Temp", leftPressure);
            AddSection(_leftStack, "Mass Flows (sccm)", leftMass);
            AddSection(_leftStack, "Substrate",         leftSubstrate);
            AddSection(_leftStack, "Process",           leftProcess);
        }

        if (rightHasContent)
        {
            _rightStack = CreateStack(RightPanel);
            _powerHeader = AddSection(_rightStack, "Power Supplies", rightPower);
            foreach (var s in rightPower!) _powerRowIds.Add(s.Id);
        }

        if (centerProcess is { Count: > 0 } && _centerPanel != null)
        {
            _centerStack = CreateStack(_centerPanel);
            // When every spec names its own Section the sub-headers carry the
            // structure and a group header on top would only repeat them.
            string? header = centerProcess.All(s => s.Section != null) ? null : "Process";
            AddSection(_centerStack, header, centerProcess);
        }

        // Rows don't auto-fit in a FlowLayoutPanel — re-fit on SizeChanged (panel
        // or splitter resize) and on ControlAdded (lazily built extra sections
        // change the fixed height budget without resizing the stack).
        HookStackFit(_leftStack);
        HookStackFit(_rightStack);
        HookStackFit(_centerStack);

        PopulateExtraSections(_leftStack, _rightStack);
    }

    private static List<ReadingSpec>? Combine(
        Dictionary<ReadingGroup, List<ReadingSpec>> grouped,
        params ReadingGroup[] groups)
    {
        var result = new List<ReadingSpec>();
        foreach (var g in groups)
            if (grouped.TryGetValue(g, out var list))
                result.AddRange(list);
        return result.Count == 0 ? null : result;
    }

    private static FlowLayoutPanel CreateStack(Panel parent)
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.FromArgb(13, 27, 42),
            Padding = new Padding(4, 6, 4, 6),
        };
        parent.Controls.Add(stack);
        return stack;
    }

    /// <summary>
    /// A dim sub-header is inserted whenever a spec's
    /// <see cref="ReadingSpec.Section"/> differs from the previous one, so
    /// consecutive specs sharing a Section form one labelled block.
    /// </summary>
    private Label? AddSection(FlowLayoutPanel stack, string? header, List<ReadingSpec>? specs)
    {
        if (specs is null || specs.Count == 0) return null;

        Label? hdr = null;
        if (header != null)
        {
            int mTop = (int)((stack.Controls.Count == 0 ? 2 : 10) * DpiF);
            hdr = new Label
            {
                Text = header.ToUpperInvariant(),
                Font = ScaledFont.Create(8f),
                ForeColor = Color.FromArgb(119, 136, 153),
                AutoSize = true,
                Margin = new Padding((int)(4 * DpiF), mTop, 0, (int)(2 * DpiF)),
            };
            stack.Controls.Add(hdr);
        }

        string? section = null;
        foreach (var spec in specs)
        {
            if (spec.Section != null && spec.Section != section)
            {
                section = spec.Section;
                int mTop = (int)((stack.Controls.Count == 0 ? 2 : 6) * DpiF);
                stack.Controls.Add(new Label
                {
                    Text = section,
                    Font = ScaledFont.Create(8f),
                    ForeColor = Color.FromArgb(119, 136, 153),
                    AutoSize = true,
                    Margin = new Padding((int)(4 * DpiF), mTop, 0, (int)(2 * DpiF)),
                });
            }

            var row = new ReadingRowControl(spec)
            {
                Name = spec.Id,
                Margin = new Padding(0, 0, 0, 0),
                // Width is set via FitRowWidthsToStack on every SizeChanged — see hookup below.
                Width = Math.Max(0, stack.ClientSize.Width - stack.Padding.Horizontal - 2),
            };
            _rowsById[spec.Id] = row;
            _readingIds.Add(spec.Id);
            if (spec.Secondary is { } sec)
            {
                _rowsById[sec.Id] = row;
                _readingIds.Add(sec.Id);
            }
            if (!_rowsByDevice.TryGetValue(spec.DeviceId, out var list))
                _rowsByDevice[spec.DeviceId] = list = new List<ReadingRowControl>();
            list.Add(row);
            stack.Controls.Add(row);
        }

        return hdr;
    }

    // Reading-row heights stretch to fill the column (4K has lots of spare
    // vertical space — taller rows mean taller sparklines). 96-DPI bases,
    // scaled by DpiF at use; tune freely.
    // A row is a caption line (label + value) with a chart under it, so the
    // base has to cover both.
    private const int BaseReadingRowHeight = 40;   // matches ReadingRowControl's seed height
    private const int MaxReadingRowHeight  = 120;  // a nearly-empty column stops here

    private void HookStackFit(FlowLayoutPanel? stack)
    {
        if (stack is null) return;
        stack.SizeChanged  += (_, _) => FitRowsToStack(stack);
        stack.ControlAdded += (_, _) =>
        {
            FitRowsToStack(stack);
            // AutoSize children (LED rows) only get their real height on the
            // layout pass AFTER the add — queue one more fit behind it so the
            // fixed budget is computed from settled heights.
            if (stack.IsHandleCreated)
                stack.BeginInvoke(() => FitRowsToStack(stack));
        };
    }

    private void FitRowsToStack(FlowLayoutPanel stack)
    {
        int targetW = Math.Max(0, stack.ClientSize.Width - stack.Padding.Horizontal - 2);

        // Fixed height budget: everything that is not a reading row keeps its
        // natural size (headers, LED rows, drive pills); the leftover is spread
        // equally over the reading rows. Margins count on both kinds.
        int rowCount = 0;
        int fixedBudget = stack.Padding.Vertical;
        foreach (Control c in stack.Controls)
        {
            if (c is ReadingRowControl) { rowCount++; fixedBudget += c.Margin.Vertical; }
            else fixedBudget += c.Height + c.Margin.Vertical;
        }
        int rowH = ComputeReadingRowHeight(stack.ClientSize.Height, fixedBudget, rowCount,
            (int)(BaseReadingRowHeight * DpiF), (int)(MaxReadingRowHeight * DpiF));

        foreach (Control c in stack.Controls)
        {
            // Resize row-style children (ReadingRowControl and subclass-added
            // Panel rows like a subclass's Drives pill rows). Skip
            // auto-sized section headers (Label with AutoSize = true).
            if (c is Label { AutoSize: true }) continue;
            if (c.Width != targetW) c.Width = targetW;
            // Only-on-change assignment also keeps this re-entrancy safe: the
            // stack is Dock-filled, so child resizes can never feed back into
            // its own size.
            if (c is ReadingRowControl && c.Height != rowH) c.Height = rowH;
        }
    }

    /// <summary>Height for every reading row in a side-column stack: base plus
    /// an equal share of the column's leftover height, clamped to
    /// [<paramref name="baseRowHeight"/>, <paramref name="maxRowHeight"/>].
    /// All inputs are device pixels.</summary>
    internal static int ComputeReadingRowHeight(int availableHeight, int fixedBudget,
        int rowCount, int baseRowHeight, int maxRowHeight)
    {
        if (rowCount <= 0) return baseRowHeight;
        int leftover = availableHeight - fixedBudget - rowCount * baseRowHeight;
        int share = (int)Math.Floor(leftover / (double)rowCount);
        return Math.Clamp(baseRowHeight + share, baseRowHeight, maxRowHeight);
    }

    public virtual void UpdateReadings(Contracts.ProcessReading[] readings)
    {
        foreach (var reading in readings)
        {
            if (_rowsById.TryGetValue(reading.Id, out var row))
                row.ApplyReading(reading);
        }
    }

    public IReadOnlyCollection<string> ReadingIds => _readingIds;

    public void SetPowerSupplyVisibility(IReadOnlyDictionary<string, bool> visibleById)
    {
        // Keyed by hardware device (Sources.xml PSU id): a supply whose rows
        // publish under <id>_V / _W still hides by its plain id via
        // ReadingSpec.Device.
        foreach (var (id, visible) in visibleById)
            if (_rowsByDevice.TryGetValue(id, out var rows))
                foreach (var row in rows) row.Visible = visible;

        if (_powerHeader != null && _powerRowIds.Count > 0)
        {
            bool anyVisible = _powerRowIds.Any(
                id => _rowsById.TryGetValue(id, out var r) && r.Visible);
            _powerHeader.Visible = anyVisible;
        }
    }

    /// <summary>
    /// Called during construction, after <see cref="BuildReadingRows"/>.
    /// The stacks are null when there are no reading specs.
    /// </summary>
    protected virtual void PopulateExtraSections(
        FlowLayoutPanel? leftStack,
        FlowLayoutPanel? rightStack)
    {
    }

    protected void AddExtraSectionHeader(FlowLayoutPanel stack, string header)
    {
        int mTop = (int)((stack.Controls.Count == 0 ? 2 : 10) * DpiF);
        var hdr = new Label
        {
            Text = header.ToUpperInvariant(),
            Font = ScaledFont.Create(8f),
            ForeColor = Color.FromArgb(119, 136, 153),
            AutoSize = true,
            Margin = new Padding((int)(4 * DpiF), mTop, 0, (int)(2 * DpiF)),
        };
        stack.Controls.Add(hdr);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Unsubscribe first so no new publisher entry can arrive while we
            // tear the writer down, then dispose under the same lock WriteLine
            // uses so an in-flight publisher can't write to a disposed writer.
            EventLogBus.Entry -= OnBusEntryForBottomDrawer;
            lock (_logSync)
            {
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }
        base.Dispose(disposing);
    }
}
