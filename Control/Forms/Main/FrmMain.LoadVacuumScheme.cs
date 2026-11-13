using APS.Controls.Dialogs;
using APS.Controls.General;
using APS.Controls.Theme;
using APS.Controls.Vacuum;
using APS.Controls.XML.GUI;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Controls.Vacuum;
using APS.SystemControl.Hosting;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Cluster;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.GUIControls;
using APS.SystemControl.GUIControls.Process;
using APS.SystemControl.XML;

namespace APS.SystemControl;

public partial class FrmMain
{
    // Per-user persistent UI layout (splitter positions, main window placement).
    // Loaded in the FrmMain ctor, applied to the System tab right away and to the
    // Process panel when it resolves, saved in FrmMain_FormClosing.
    private UserLayout _userLayout = new();
    private FormWindowState _lastNonMinimizedState = FormWindowState.Maximized;

    private void ApplyMainWindowPlacement()
    {
        if (_userLayout.HasMainWindowBounds)
        {
            var saved = new Rectangle(_userLayout.MainWindowX, _userLayout.MainWindowY,
                _userLayout.MainWindowWidth, _userLayout.MainWindowHeight);
            var areas = Screen.AllScreens.Select(s => s.WorkingArea);
            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Normal;
            DesktopBounds = WindowPlacement.Fit(saved, areas, Screen.PrimaryScreen!.WorkingArea);
        }
        WindowState = (_userLayout.MainWindowMaximized ?? true) ? FormWindowState.Maximized : FormWindowState.Normal;
        _lastNonMinimizedState = WindowState;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (WindowState != FormWindowState.Minimized) _lastNonMinimizedState = WindowState;
    }

    private FloatingPanel? _systemEventsLog;
    private EventsLogControl? _systemEventsContent;
    private bool _systemEventsVisibleIntent = true;
    private bool _systemEventsLayoutApplied;
    private Control? _systemEventsChip;
    private Control? _schematicHud;
    private Label? _hudZoom;
    private Label? _hudFit;

    private const float HudZoomStep = 0.25f;

    private async void FrmMain_Load(object sender, EventArgs e)
    {
        Text = $"{APS.SystemControl.Hosting.ControlApp.Edition.ProductName}: {Path.GetFileName(config.ConfigDir)}";

        LoadVacuumComponentsXML(config.GUIVacuumConfigFile);
        LoadGraphs();
        ConfigureLogGrid();
        // StartAgentServer() must run after the Log-tab grid subscribes to EventLogBus.Entry
        // (just above), or its "Agent server listening on ..." line is silently dropped.
        // It has its own internal try/catch, so this is safe even though Load-handler
        // exceptions can be swallowed silently.
        InitControlMode();          // before StartAgentServer: the dispatcher closes over Core.ControlMode
        StartAgentServer();
        // Clear designer placeholder so Dock=Fill process panels work correctly
        tabProcess.Controls.Clear();
        LoadCustomPanelsXML(config.GUIPanelsConfigFile);
        ComposeHardwareMenus();
        // Status-strip right side. The hint label is flanked by two spring fillers so it
        // sits centered between the left-hand chamber state tiles and the right-hand account
        // indicators: the two springs split the leftover width equally, centering the hint
        // while pushing the quota/user labels to the far right.
        _lblHint = new ToolStripStatusLabel
        {
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = VacuumTheme.TextDim,
        };
        statusStrip1.Items.Add(new ToolStripStatusLabel { Spring = true }); // left spring → centers the hint
        statusStrip1.Items.Add(_lblHint);
        statusStrip1.Items.Add(new ToolStripStatusLabel { Spring = true }); // right spring → account block to the far right
        InitAgentStatusIndicator(); // heads the right-hand group, left of quota/user/version
        InitializeAccountsUi();   // adds the account indicators

        // Version at the FAR RIGHT (added last → rightmost, pushed there by the right
        // spring). Larger/bolder than the other status items so the build number reads
        // clearly both on-screen and in the dashboard status strip. Independent of the
        // accounts UI (which may early-return), since it is added here, not in it.
        statusStrip1.Items.Add(new ToolStripStatusLabel($"v{AppVersion.Full}")
        {
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 214, 229),
            Margin = new Padding(14, 0, 6, 0),
        });

        // HintManager: constructed after controls exist and DarkModeHelper.FixControls
        // has run (that ran in the FrmMain ctor before the channel pool was created).
        _hints = new HintManager(this, _lblHint);
        RegisterFrmMainHints();

        InitializeToolbarExtensions();
        RunModules(nameof(IControlModule.OnLoad), m => m.OnLoad(this));
        SetupSystemTabSplitters();
        ApplySystemTabLayout();
        InitPsuPanelVisibility();
        SetupSystemEventsOverlay();
        if (File.Exists(config.SourcesFile))
        {
            sourceManager.Load(config.SourcesFile);
            RebuildPsuMagnetronGlowMap();
        }

        // Hardware-load messages are queued onto the GUI channel during
        // CoreThread.LoadConfig (which ran in the ctor) and only reach the
        // bus after the form is loaded. The initial Startup -> Idle state
        // transition is also emitted by the first sensor poll ~200 ms
        // after CoreThread.Start(). Hold the boot gate closed until those
        // batches have drained, then publish "System is ready" so it
        // lands as the first operator-visible line.
        await Task.Delay(2000);
        if (IsDisposed) return;
        CompleteBoot();
        DisplayLogMessage("Core", "System is ready");
    }

    /// <summary>
    /// Called once after LoadCustomPanelsXML so any controls the panels
    /// loaded are already in place.
    /// </summary>
    private void SetupSystemTabSplitters()
    {
        // Both side panels are added by the designer with Dock = Left/Right
        // and a 500 px width. Just install the splitters; the factory takes
        // care of putting them inboard of the panels via z-order.
        SideSplitterFactory.Install(tabSystem, panelHwLeft, minPanelWidth: 200);
        SideSplitterFactory.Install(tabSystem, panelHwRight, minPanelWidth: 200);
    }

    private void SetupSystemEventsOverlay()
    {
        _systemEventsContent = new EventsLogControl();
        _systemEventsLog = new FloatingPanel
        {
            Caption = "Events",
            MinSize = new Size(DpiScale.Px(200), DpiScale.Px(100)),
            Bounds = new Rectangle(DpiScale.Px(12), DpiScale.Px(12), DpiScale.Px(360), DpiScale.Px(220)),
            Visible = true,
        };
        _systemEventsLog.ContentPanel.Controls.Add(new DarkScrollHost(_systemEventsContent) { Dock = DockStyle.Fill });
        vacuumSchematic.Controls.Add(_systemEventsLog);
        _systemEventsLog.UserCloseRequested += (_, _) =>
        {
            _systemEventsVisibleIntent = false;
            _systemEventsChip?.Invalidate();
        };
        _systemEventsLog.VisibilityChanged += (_, _) => _systemEventsChip?.Invalidate();
        _systemEventsLog.BringToFront();

        _systemEventsChip = CreateSystemEventsChip();
        vacuumSchematic.Controls.Add(_systemEventsChip);
        _systemEventsChip.BringToFront();

        _schematicHud = CreateSchematicViewHud(_systemEventsChip.Right + DpiScale.Px(8));
        vacuumSchematic.Controls.Add(_schematicHud);
        _schematicHud.BringToFront();
        vacuumSchematic.ZoomChanged += (_, _) => UpdateSchematicHud();
        UpdateSchematicHud();

        // vacuumSchematic may have other Dock=Fill controls added later;
        // keep the overlay, chip and view HUD on top whenever a sibling lands.
        vacuumSchematic.ControlAdded += (_, e) =>
        {
            if (e.Control != _systemEventsLog && e.Control != _systemEventsChip && e.Control != _schematicHud)
            {
                _systemEventsLog.BringToFront();
                _systemEventsChip?.BringToFront();
                _schematicHud?.BringToFront();
            }
        };
        // Defer the actual bounds apply to Shown — by FrmMain_Load,
        // vacuumSchematic's handle exists but its maximized-form client
        // rectangle hasn't necessarily settled. Clamping before that
        // would pull saved X/Y inward and the user's position would
        // drift left at every restart.
        this.Shown += (_, _) => ApplySystemEventsOverlayBounds();
        if (IsHandleCreated && Visible) ApplySystemEventsOverlayBounds();
    }

    private Control CreateSystemEventsChip()
    {
        var chip = new Panel
        {
            Size = new Size(DpiScale.Px(80), DpiScale.Px(22)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(45, 60, 85),
            Location = new Point(DpiScale.Px(12), DpiScale.Px(12)),
        };

        chip.Paint += (s, e) =>
        {
            var g = e.Graphics;
            bool on = _systemEventsVisibleIntent;
            Color dotColor    = on ? Color.FromArgb(0, 255, 255) : Color.FromArgb(140, 150, 170);
            Color borderColor = on ? Color.FromArgb(0, 200, 220) : Color.FromArgb(120, 140, 170);
            Color bgColor     = on ? Color.FromArgb(20, 60, 80)  : Color.FromArgb(45, 60, 85);

            using var bg = new SolidBrush(bgColor);
            g.FillRectangle(bg, 0, 0, chip.Width, chip.Height);
            using var border = new Pen(borderColor, 1f);
            g.DrawRectangle(border, 0, 0, chip.Width - 1, chip.Height - 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var dot = new SolidBrush(dotColor);
            g.FillEllipse(dot, DpiScale.Px(7), DpiScale.Px(8), DpiScale.Px(7), DpiScale.Px(7));
            using var font = APS.SystemControl.Controls.Theme.ScaledFont.Create(9f, FontStyle.Bold);
            using var text = new SolidBrush(APS.Controls.Theme.VacuumTheme.TextPrimary);
            g.DrawString("Events", font, text, DpiScale.Px(19), DpiScale.Px(3));
        };
        chip.Click += (_, _) => ToggleAllEventsOverlays();

        var toolTip = new ToolTip { InitialDelay = 400 };
        toolTip.SetToolTip(chip, "Show/hide events overlay");

        return chip;
    }

    private Control CreateSchematicViewHud(int left)
    {
        var font = APS.SystemControl.Controls.Theme.ScaledFont.Create(9f, FontStyle.Bold);
        var hud = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            BackColor = Color.FromArgb(45, 60, 85),
            Location = new Point(left, DpiScale.Px(12)),
            Height = DpiScale.Px(22),
        };
        hud.Paint += (_, e) =>
        {
            using var border = new Pen(Color.FromArgb(120, 140, 170), 1f);
            e.Graphics.DrawRectangle(border, 0, 0, hud.Width - 1, hud.Height - 1);
        };
        hud.Disposed += (_, _) => font.Dispose();

        int x = DpiScale.Px(4);
        Label Part(string text, string? widest = null, Action? click = null)
        {
            var size = TextRenderer.MeasureText(widest ?? text, font);
            var label = new Label
            {
                AutoSize = false,
                Text = text,
                Font = font,
                ForeColor = VacuumTheme.TextPrimary,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, DpiScale.Px(2)),
                Size = new Size(size.Width + (click != null ? DpiScale.Px(8) : 0), hud.Height - DpiScale.Px(4)),
            };
            if (click != null)
            {
                label.Cursor = Cursors.Hand;
                label.Click += (_, _) => click();
                label.MouseEnter += (_, _) => label.BackColor = Color.FromArgb(70, 90, 125);
                label.MouseLeave += (_, _) => label.BackColor = Color.Transparent;
                label.Paint += (_, e) =>
                {
                    using var pen = new Pen(Color.FromArgb(100, 120, 150), 1f);
                    e.Graphics.DrawRectangle(pen, 0, 0, label.Width - 1, label.Height - 1);
                };
            }
            hud.Controls.Add(label);
            x = label.Right + DpiScale.Px(2);
            return label;
        }

        Part("Zoom:");
        var minus = Part("\u2212", click: () => StepZoom(-HudZoomStep));
        _hudZoom = Part("100%", "250%", () => vacuumSchematic.ResetZoom());
        var plus = Part("+", click: () => StepZoom(HudZoomStep));
        _hudFit = Part("Fit", click: () => vacuumSchematic.ResetZoom());
        hud.Width = x + DpiScale.Px(2);

        _hints.Set(minus, "Zoom out|Zoom out from the centre of the schematic.");
        _hints.Set(plus, "Zoom in|Zoom in at the centre of the schematic (100–250%). " +
            "The mouse wheel zooms at the cursor; when zoomed, drag the background or middle-drag anywhere to pan.");
        _hints.Set(_hudZoom, "Zoom|Click to reset zoom to 100%.");
        _hints.Set(_hudFit, "Fit|Reset zoom and pan to show the whole schematic.");

        return hud;
    }

    private void StepZoom(float step) =>
        vacuumSchematic.SetZoom(
            vacuumSchematic.ZoomFactor + step,
            new Point(vacuumSchematic.ClientSize.Width / 2, vacuumSchematic.ClientSize.Height / 2));

    private void UpdateSchematicHud()
    {
        if (_hudZoom == null || _hudFit == null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _hudZoom.Text = ((int)Math.Round(vacuumSchematic.ZoomFactor * 100)).ToString(inv) + "%";
        _hudFit.ForeColor = vacuumSchematic.ZoomFactor > 1.001f ? VacuumTheme.TextPrimary : VacuumTheme.TextDim;
    }

    private void ApplySystemEventsOverlayBounds()
    {
        if (_systemEventsLayoutApplied || _systemEventsLog is null) return;
        _systemEventsLayoutApplied = true;

        var parent = vacuumSchematic.ClientRectangle;
        bool hasPersisted = _userLayout.SystemEventsLogX >= 0 || _userLayout.SystemEventsLogY >= 0;
        int w = _userLayout.SystemEventsLogWidth  > 0 ? _userLayout.SystemEventsLogWidth  : DpiScale.Px(360);
        int h = _userLayout.SystemEventsLogHeight > 0 ? _userLayout.SystemEventsLogHeight : DpiScale.Px(220);
        int margin = DpiScale.Px(12);
        int x = hasPersisted ? Math.Max(0, _userLayout.SystemEventsLogX) : parent.Width - w - margin;
        int y = hasPersisted ? Math.Max(0, _userLayout.SystemEventsLogY) : parent.Height - h - margin;

        _systemEventsLog.Bounds = FloatingPanelLayout.Clamp(
            new Rectangle(x, y, w, h), parent, _systemEventsLog.MinSize);
        _systemEventsVisibleIntent = _userLayout.SystemEventsLogVisible ?? true;
        _systemEventsLog.Visible = _systemEventsVisibleIntent;
        _systemEventsLog.BringToFront();
    }

    internal void ToggleAllEventsOverlays()
    {
        if (_systemEventsLog == null) return;
        _systemEventsVisibleIntent = !_systemEventsVisibleIntent;
        _systemEventsLog.Visible = _systemEventsVisibleIntent;
        _systemEventsChip?.Invalidate();
    }

    /// <summary>No-op when the user has never resized (saved widths are 0).</summary>
    private void ApplySystemTabLayout()
    {
        if (_userLayout.SystemTabLeftWidth > 0)
            panelHwLeft.Width = _userLayout.SystemTabLeftWidth;
        if (_userLayout.SystemTabRightWidth > 0)
            panelHwRight.Width = _userLayout.SystemTabRightWidth;
    }

    private void ApplyProcessPanelLayout()
    {
        if (_processPanel is GUIControls.Process.BaseProcessPanel bpp)
        {
            if (_userLayout.ProcessPanelLeftWidth > 0)
                bpp.CurrentLeftPanelWidth = _userLayout.ProcessPanelLeftWidth;
            if (_userLayout.ProcessPanelRightWidth > 0)
                bpp.CurrentRightPanelWidth = _userLayout.ProcessPanelRightWidth;
            if (_userLayout.ProcessPanelCenterWidth > 0)
                bpp.CurrentCenterPanelWidth = _userLayout.ProcessPanelCenterWidth;
        }
    }

    /// <summary>Called from FrmMain_FormClosing once the user confirms exit.</summary>
    private void SaveUserLayout()
    {
        _userLayout.SystemTabLeftWidth = panelHwLeft.Width;
        _userLayout.SystemTabRightWidth = panelHwRight.Width;
        if (_processPanel is GUIControls.Process.BaseProcessPanel bpp)
        {
            _userLayout.ProcessPanelLeftWidth = bpp.CurrentLeftPanelWidth;
            _userLayout.ProcessPanelRightWidth = bpp.CurrentRightPanelWidth;
            _userLayout.ProcessPanelCenterWidth = bpp.CurrentCenterPanelWidth;
        }
        if (_systemEventsLog is not null)
        {
            var r = _systemEventsLog.Bounds;
            _userLayout.SystemEventsLogX = r.X;
            _userLayout.SystemEventsLogY = r.Y;
            _userLayout.SystemEventsLogWidth = r.Width;
            _userLayout.SystemEventsLogHeight = r.Height;
            _userLayout.SystemEventsLogVisible = _systemEventsVisibleIntent;
        }
        _userLayout.RememberMainWindow(
            WindowState, _lastNonMinimizedState,
            WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
        _userLayout.Save();
    }

    private void LoadValves(XML_ValveControls xml_valves)
    {
        foreach (XML_ValveControl xml_valve in xml_valves.Items!)
        {
            ValveControl control = new(xml_valve.Id)
            {
                Direction = xml_valve.Direction!,
            };
            if (xml_valve.ScaleSet)
            { control.VScale = xml_valve.Scale; }

            control.DoubleClick += new EventHandler(SystemControl_DoubleClick);
            vacuumSchematic.AddComponent(control, xml_valve.Left, xml_valve.Top);
        }
    }

    private void LoadMFCs(XML_MFCControls xml_mfcs)
    {
        foreach (XML_MFCControl xml_mfc in xml_mfcs.Items!)
        {
            MFCControl control = new(xml_mfc.Id, xml_mfc.Gas)
            {
                Caption = xml_mfc.Description ?? "",
                Value = "0.00",
                SendHWMessage = (siD, value) => { SendFloatValueMessage(DeviceType.MFC, xml_mfc.Id, value); }
            };
            if (xml_mfc.ScaleSet)
            { control.VScale = xml_mfc.Scale; }
            vacuumSchematic.AddComponent(control, xml_mfc.Left, xml_mfc.Top);
        }
    }

    private void LoadSensors(XML_Sensors xml_sensors)
    {
        foreach (XML_Sensor xml_sensor in xml_sensors.Items!)
        {
            PanelStyle style = xml_sensor.Direction == "vert" ? PanelStyle.Vert : PanelStyle.Horiz;
            LEDPanel control = new(GUIConst.ControlName(DeviceType.Sensor, xml_sensor.Id), style)
            {
                Caption = xml_sensor.Description ?? "",
                Value = "0.00",
                Format = xml_sensor.Format ?? "F2",
                FontColor = Color.FromName(xml_sensor.Color ?? "Green"),
                Units = xml_sensor.Unit ?? ""
            };
            if (xml_sensor.ScaleSet)
            { control.VScale = xml_sensor.Scale; }

            vacuumSchematic.AddComponent(control, xml_sensor.Left, xml_sensor.Top);
        }
    }

    private void LoadLeds(XML_LedControls xml_leds)
    {
        foreach (XML_LedControl xml_led in xml_leds.Items!)
        {
            LedControl control = new(xml_led.Id!, APS.Controls.Core.DeviceType.System)
            {
                LedColor = Color.FromName(xml_led.Color ?? "Green"),
                Align = xml_led.Align ?? "left",
                VScale = xml_led.Scale,
                BGColor = Color.FromName(xml_led.BgColor ?? (IsDarkMode() ? "Black" : "White")),
                Connected = true
            };
            vacuumSchematic.AddComponent(control, xml_led.Left, xml_led.Top);
        }
    }

    private void LoadTPumps(XML_TPumpControls xml_pumps)
    {
        foreach (XML_TPumpControl xml_pump in xml_pumps.Items!)
        {
            TurboPumpControl control = new(xml_pump.Id)
            {
                BackColor = Color.FromName(xml_pump.BgColor ?? (IsDarkMode() ? "Black" : "White")),
            };
            if (!string.IsNullOrEmpty(xml_pump.Direction))
                control.Direction = xml_pump.Direction;
            if (xml_pump.ScaleSet)
            { control.VScale = xml_pump.Scale; }
            control.DoubleClick += new EventHandler(SystemControl_DoubleClick);
            vacuumSchematic.AddComponent(control, xml_pump.Left, xml_pump.Top);
        }
    }

    private void LoadPumps(XML_PumpControls xml_pumps)
    {
        foreach (XML_PumpControl xml_pump in xml_pumps.Items!)
        {
            PumpControl control = new(xml_pump.Id)
            {
                BackColor = Color.FromName(xml_pump.BgColor ?? (IsDarkMode() ? "Black" : "White")),
            };
            if (xml_pump.ScaleSet)
            { control.VScale = xml_pump.Scale; }
            control.DoubleClick += new EventHandler(SystemControl_DoubleClick);
            vacuumSchematic.AddComponent(control, xml_pump.Left, xml_pump.Top);
        }
    }

    private void LoadStages(XML_StageControls xml_stages)
    {
        foreach (XML_StageControl xml_stage in xml_stages.Items!)
        {
            string id = xml_stage.Id;
            StageControl control = new(id)
            {
                SendHWMessage = (_, value) => SendFloatValueMessage(DeviceType.Stage, id, value),
                SendStop = _ => SendStageStop(id),
                OpenSettings = _ => OpenStageSettings(id),
            };
            if (xml_stage.ScaleSet)
            { control.VScale = xml_stage.Scale; }
            vacuumSchematic.AddComponent(control, xml_stage.Left, xml_stage.Top);
            PushStagePresets(control, id);
        }
    }

    private BaseStage? GetStage(string id)
    {
        var core = systemThread?.Core;
        if (core == null) return null;
        if (core.hwElements.Item.TryGetValue(DeviceType.Stage, out var stageElems))
            foreach (var kv in stageElems)
                if (kv.Key == id && kv.Value is BaseStage st)
                    return st;
        return null;
    }

    private void PushStagePresets(StageControl control, string id)
    {
        var stage = GetStage(id);
        if (stage == null) return;
        control.SetPresets(stage.Presets
            .Select(p => new APS.Controls.Vacuum.StagePreset(p.Name, p.Position))
            .ToList());
    }

    /// <summary>Opens the extended-options popup for a stage, then re-pushes any edited presets.</summary>
    private void OpenStageSettings(string id)
    {
        var stage = GetStage(id);
        if (stage == null) return;
        using (var dlg = new frmStageSettings(stage,
            target => SendFloatValueMessage(DeviceType.Stage, id, target),
            () => SendStageStop(id)))
        {
            dlg.ShowDialog(this);
        }
        if (FindCachedControl(DeviceType.Stage, id) is StageControl widget)
            PushStagePresets(widget, id);
    }

    private void LoadGasInlets(XML_GasInletControls xml_gasInlets)
    {
        foreach (XML_GasInletControl xml_gasInlet in xml_gasInlets.Items!)
        {
            GasInletControl control = new(xml_gasInlet.Id!, xml_gasInlet.Gas ?? "Ar");
            if (!string.IsNullOrEmpty(xml_gasInlet.Direction))
                control.Direction = xml_gasInlet.Direction;
            if (xml_gasInlet.ScaleSet)
            { control.VScale = xml_gasInlet.Scale; }
            vacuumSchematic.AddComponent(control, xml_gasInlet.Left, xml_gasInlet.Top);
            vacuumSchematic.RegisterAlwaysFlowing(xml_gasInlet.Id!);
        }
    }

    private void LoadMagnetrons(XML_MagnetronControls xml_magnetrons)
    {
        foreach (XML_MagnetronControl xml_magnetron in xml_magnetrons.Items!)
        {
            MagnetronControl control = new(xml_magnetron.Id!, xml_magnetron.Gas ?? "Ar")
            {
                TargetLabel = xml_magnetron.TargetLabel ?? xml_magnetron.Id
            };
            if (xml_magnetron.ScaleSet)
            { control.VScale = xml_magnetron.Scale; }
            vacuumSchematic.AddComponent(control, xml_magnetron.Left, xml_magnetron.Top);
        }
    }

    private void LoadGateValves(XML_GateValveControls xml_gateValves)
    {
        foreach (XML_GateValveControl xml_gv in xml_gateValves.Items!)
        {
            GateValveControl control = new(xml_gv.Id!)
            {
                Direction = xml_gv.Direction!,
            };
            if (xml_gv.ScaleSet)
            { control.VScale = xml_gv.Scale; }

            control.DoubleClick += new EventHandler(SystemControl_DoubleClick);
            vacuumSchematic.AddComponent(control, xml_gv.Left, xml_gv.Top);
        }
    }

    private void LoadSPumps(XML_SPumpControls xml_pumps)
    {
        foreach (XML_SPumpControl xml_pump in xml_pumps.Items!)
        {
            ScrollPumpControl control = new(xml_pump.Id!)
            {
                BackColor = Color.FromName(xml_pump.BgColor ?? (IsDarkMode() ? "Black" : "White")),
            };
            if (!string.IsNullOrEmpty(xml_pump.Direction))
                control.Direction = xml_pump.Direction;
            if (xml_pump.ScaleSet)
            { control.VScale = xml_pump.Scale; }
            control.DoubleClick += new EventHandler(SystemControl_DoubleClick);
            vacuumSchematic.AddComponent(control, xml_pump.Left, xml_pump.Top);
        }
    }

    private void LoadTConnectors(XML_TConnectorControls xml_tconnectors)
    {
        foreach (XML_TConnectorControl xml_tc in xml_tconnectors.Items!)
        {
            TConnectorControl control = new(xml_tc.Id!)
            {
                Direction = xml_tc.Direction ?? "down",
            };
            if (xml_tc.ScaleSet)
            { control.VScale = xml_tc.Scale; }

            vacuumSchematic.AddComponent(control, xml_tc.Left, xml_tc.Top);
            vacuumSchematic.RegisterPassiveComponent(xml_tc.Id!);
        }
    }

    private void LoadAirInlets(XML_AirInletControls xml_airInlets)
    {
        foreach (XML_AirInletControl xml_ai in xml_airInlets.Items!)
        {
            AirInletControl control = new(xml_ai.Id!)
            {
                Direction = xml_ai.Direction ?? "right",
            };
            if (xml_ai.ScaleSet)
            { control.VScale = xml_ai.Scale; }

            vacuumSchematic.AddComponent(control, xml_ai.Left, xml_ai.Top);
            vacuumSchematic.RegisterPassiveComponent(xml_ai.Id!);
        }
    }

    private void LoadGasMixers(XML_GasMixerControls xml_gasMixers)
    {
        foreach (XML_GasMixerControl xml_gm in xml_gasMixers.Items!)
        {
            GasMixerControl control = new(xml_gm.Id!)
            {
                Direction = xml_gm.Direction ?? "hor",
                Inputs = xml_gm.Inputs,
                Outputs = xml_gm.Outputs,
            };
            if (xml_gm.ScaleSet)
            { control.VScale = xml_gm.Scale; }

            vacuumSchematic.AddComponent(control, xml_gm.Left, xml_gm.Top);
            vacuumSchematic.RegisterPassiveComponent(xml_gm.Id!);
            vacuumSchematic.RegisterMixerComponent(xml_gm.Id!);
        }
    }

    private void LoadThrottleValves(XML_ThrottleValveControls xml_throttleValves)
    {
        foreach (XML_ThrottleValveControl xml_tv in xml_throttleValves.Items!)
        {
            ThrottleValveControl control = new(xml_tv.Id!)
            {
                Direction = xml_tv.Direction!,
                Position = 100f, // Default: fully open (real system default)
                SendHWMessage = (id, action, pressure) =>
                {
                    object[] args = action switch
                    {
                        ThrottleAction.Set   => ["SetPressure", pressure!.Value],
                        ThrottleAction.Open  => ["Open"],
                        ThrottleAction.Close => ["Close"],
                        ThrottleAction.Stop  => ["Stop"],
                        _ => []
                    };
                    if (args.Length == 0) return;
                    SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Controller, id, args));
                },
            };
            if (xml_tv.ScaleSet)
            { control.VScale = xml_tv.Scale; }

            vacuumSchematic.AddComponent(control, xml_tv.Left, xml_tv.Top);
            // A throttle conducts flow when open but is not itself a flow source
            // (see RegisterThrottleComponent). Mark it before seeding its open state.
            vacuumSchematic.RegisterThrottleComponent(xml_tv.Id!);
            vacuumSchematic.SetComponentOpen(xml_tv.Id!, true);
        }
    }

    private readonly Dictionary<string, TransferModuleControl> transferModuleControls = new();

    private IClusterProcessPanel? _clusterPanel;
    // Static per-chamber sputter-source map (target material + shutter/supply
    // ids) from Sources.xml. Loaded once on first refresh — materials don't
    // change while the app runs.
    private IReadOnlyDictionary<string, ClusterSourceInfo[]>? _clusterSources;
    private bool _clusterPanelSearched;

    /// <summary>
    /// Resolves the cluster overview panel once (cached). Returns null on configs
    /// that don't load an <see cref="IClusterProcessPanel"/>, so
    /// the per-tick refresh early-returns at zero cost.
    /// </summary>
    private IClusterProcessPanel? FindClusterPanel()
    {
        if (_clusterPanelSearched) return _clusterPanel;
        _clusterPanelSearched = true;

        var tabProcess = FindControlByName("tabProcess");
        if (tabProcess == null) return null;

        foreach (Control ctrl in tabProcess.Controls)
        {
            if (ctrl is IClusterProcessPanel cp) { _clusterPanel = cp; return cp; }
            foreach (Control child in ctrl.Controls)
                if (child is IClusterProcessPanel cp2) { _clusterPanel = cp2; return cp2; }
        }
        return null;
    }

    private void LoadTransferModules(XML_TransferModuleControls xml_tms)
    {
        if (xml_tms.Items == null) return;

        foreach (var xtm in xml_tms.Items)
        {
            if (string.IsNullOrEmpty(xtm.Id)) continue;

            var widget = new TransferModuleControl(xtm.Id)
            {
                ChamberId = xtm.Chamber ?? "TM",
            };

            var robot = systemThread?.Core?.hwElements.Item.TryGetValue(
                            DeviceType.Robot, out var robotElems) == true
                        ? robotElems[xtm.Id] as BaseRobot
                        : null;

            if (robot != null)
            {
                widget.ConfiguredFaces = robot.Faces
                    .Select(kv => (kv.Key, kv.Value))
                    .ToList();

                // Post worker operations to the HW channel — never call BaseRobot
                // methods here: that did blocking S7-TCP I/O on the UI thread and
                // dropped failures silently (code audit 2026-08-29, C8).
                TransferWidgetWiring.Wire(widget, robot, msg => SendOperatorCommand(msg));
            }

            // AddComponent sets BaseScale (-> CalculateSize), positions the vessel centre
            // at the (left, top) percent coordinate via its anchor midpoint, and registers
            // it for canvas-resize repositioning -- same path as every other component.
            if (xtm.ScaleSet) widget.VScale = xtm.Scale;
            vacuumSchematic.AddComponent(widget, xtm.Left, xtm.Top);
            widget.BringToFront();
            transferModuleControls[xtm.Id] = widget;
        }

        // Start the per-tick robot-state refresh timer (200 ms — same cadence
        // as the HW poll loop so the widget never lags more than one cycle).
        if (transferModuleControls.Count > 0)
            StartRobotRefreshTimer();
    }

    private System.Windows.Forms.Timer? _robotRefreshTimer;

    private void StartRobotRefreshTimer()
    {
        _robotRefreshTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _robotRefreshTimer.Tick += (_, _) =>
        {
            // Best-effort cosmetic refresh. These do live PLC reads on the UI
            // thread; when the PLC is unreachable (e.g. Emulator not started)
            // they can throw PlcCommException. Swallow it so a 200 ms cosmetic
            // tick never escalates to the global crash handler — the next tick
            // retries once HW is back. The two refreshes are independent: a
            // failure in the transfer-module read must NOT starve the cluster
            // panel, which renders its chamber structure even while HW is
            // offline (its live readings degrade to null via HwRead.Safe).
            try { RefreshTransferModules(); } catch (PlcCommException) { /* HW offline — skip */ }
            try { RefreshClusterPanel(); }    catch (PlcCommException) { /* HW offline — skip */ }
        };
        _robotRefreshTimer.Start();

        this.FormClosing += (_, e) =>
        {
            // Skip teardown on a cancelled exit (operator answered No) — otherwise
            // all transfer-module/cluster widgets stop refreshing permanently.
            if (e.Cancel) return;
            _robotRefreshTimer?.Stop();
            _robotRefreshTimer?.Dispose();
            _robotRefreshTimer = null;
        };
    }

    /// <summary>
    /// Called from a 200 ms WinForms timer (UI thread), so no marshalling is needed.
    /// </summary>
    private void RefreshTransferModules()
    {
        var core = systemThread?.Core;
        if (core == null) return;

        if (!core.hwElements.Item.TryGetValue(DeviceType.Robot, out var robotElems))
            return;
        if (!core.hwElements.Item.TryGetValue(DeviceType.Sensor, out var sensorElems))
            sensorElems = null;
        if (!core.hwElements.Item.TryGetValue(DeviceType.Pump, out var pumpElems))
            pumpElems = null;
        if (!core.hwElements.Item.TryGetValue(DeviceType.Valve, out var valveElems))
            valveElems = null;

        foreach (var (id, widget) in transferModuleControls)
        {
            if (robotElems[id] is not BaseRobot robot) continue;

            // Cast via byte to bridge the two independent enum definitions
            // (Control's RobotState vs VacuumControls' mirror RobotState).
            widget.State       = (APS.Controls.Vacuum.RobotState)(byte)robot.State;
            widget.Phase       = (APS.Controls.Vacuum.RobotPhase)(byte)robot.Phase;
            widget.CurrentFace = robot.CurrentFace;
            widget.HasWafer    = robot.HasWafer;
            widget.ErrorCode   = (APS.Controls.Vacuum.RobotErrorCode)(byte)robot.ErrorCode;
            widget.AngleNorm   = robot.AngleNorm;
            widget.ExtendNorm  = robot.ExtendNorm;

            if (sensorElems != null)
            {
                foreach (var kvp in sensorElems)
                {
                    if (kvp.Value is BaseSensor sensor
                        && string.Equals(sensor.Chamber, widget.ChamberId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Cached value (poll-fed) — never a live PLC read on the UI thread.
                        widget.Pressure = sensor.LastValue;
                        break;
                    }
                }
            }

            bool turboOn = false, scrollOn = false;
            if (pumpElems != null)
            {
                foreach (var kvp in pumpElems)
                {
                    // Pump ID convention for Cluster_Demo: TM_TP (turbo) / TM_SP (scroll)
                    // Generalised: check if the pump Id starts with ChamberId + "_T" or "_S"
                    string chamber = widget.ChamberId;
                    if (kvp.Value is BasePump pump)
                    {
                        // Cached running flag (poll-fed) — no live PLC read here.
                        bool isOn = pump.LastRunning;
                        string pumpId = kvp.Key;
                        if (pumpId.StartsWith(chamber + "_T", StringComparison.OrdinalIgnoreCase))
                            turboOn = isOn;
                        else if (pumpId.StartsWith(chamber + "_S", StringComparison.OrdinalIgnoreCase))
                            scrollOn = isOn;
                    }
                }
            }
            widget.TurboOn  = turboOn;
            widget.ScrollOn = scrollOn;

            widget.Faces = robot.Faces
                .Select(kv =>
                {
                    string faceName = kv.Key;
                    int    faceIdx  = kv.Value;

                    bool slitOpen = false;
                    if (valveElems != null
                        && robot.FaceSlitMap.TryGetValue(faceName, out string? slitId)
                        && valveElems[slitId] is BaseValve slit)
                    {
                        slitOpen = slit.LastOpen;   // cached, no PLC I/O on UI thread
                    }

                    return new TransferModuleControl.TmFace(faceName, faceIdx, slitOpen);
                })
                .ToList();
        }
    }

    /// <summary>
    /// Builds a ClusterSnapshot from current per-chamber device state and pushes
    /// it to the cluster overview panel. Called from the 200 ms robot tick (UI thread).
    /// </summary>
    private void RefreshClusterPanel()
    {
        var panel = FindClusterPanel();
        if (panel == null) return;

        var core = systemThread?.Core;
        if (core == null) return;

        _clusterSources ??= string.IsNullOrEmpty(core.ConfigDir)
            ? new Dictionary<string, ClusterSourceInfo[]>()
            : APS.SystemControl.Core.Classes.SourcesMapLoader.LoadChamberSources(core.ConfigDir);

        core.hwElements.Item.TryGetValue(DeviceType.Sensor, out var sensorElems);
        core.hwElements.Item.TryGetValue(DeviceType.Valve,  out var valveElems);
        core.hwElements.Item.TryGetValue(DeviceType.Pump,   out var pumpElems);
        core.hwElements.Item.TryGetValue(DeviceType.Robot,  out var robotElems);
        core.hwElements.Item.TryGetValue(DeviceType.MFC,    out var mfcElems);
        core.hwElements.Item.TryGetValue(DeviceType.Power,  out var powerElems);
        core.hwElements.Item.TryGetValue(DeviceType.Stage,  out var stageElems);

        BaseRobot? robot = null;
        if (robotElems != null)
            foreach (var kv in robotElems)
                if (kv.Value is BaseRobot r) { robot = r; break; }

        // Pressure/temperature are matched by exact device key (each chamber has
        // both C<n>_pm and C<n>_tmp tagged with the same Chamber attribute, so a
        // chamber-only match would be ambiguous).
        // All readings below come from poll-fed caches (LastValue/LastFlow/
        // LastPosition/LastOpen/LastRunning) — pure in-memory, no live PLC reads
        // on the UI thread. The 200 ms background poll keeps them fresh, and
        // keeping hardware access on that single thread also avoids concurrent
        // Sharp7 socket reads. Power is already a cached property.
        double? SensorByKey(string key)
            => sensorElems != null && sensorElems[key] is BaseSensor s
                ? s.LastValue : (double?)null;

        double? PressureOf(string chamber)    => SensorByKey(chamber + "_pm");
        double? TemperatureOf(string chamber) => SensorByKey(chamber + "_tmp");

        // All MFCs for the chamber (Ar, N2, …), ordered by key so the primary
        // (shortest suffix, e.g. C<n>_Ar) sorts before secondary (C<n>_N2).
        ClusterReadingInfo[] FlowsOf(string chamber)
        {
            if (mfcElems == null) return System.Array.Empty<ClusterReadingInfo>();
            var list = new List<ClusterReadingInfo>();
            foreach (var kv in mfcElems)
                if (kv.Value is BaseMFC m &&
                    string.Equals(m.Chamber, chamber, StringComparison.OrdinalIgnoreCase))
                    list.Add(ClusterReadingInfo.For(kv.Key, chamber, m.LastFlow));
            return list.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // All power supplies for the chamber (RF, RF_B, …), ordered by key so the
        // primary (C<n>_RF) sorts before secondary (C<n>_RF_B). Power is a cached
        // property (poll updates it via ReadFeedback).
        ClusterReadingInfo[] PowersOf(string chamber)
        {
            if (powerElems == null) return System.Array.Empty<ClusterReadingInfo>();
            var list = new List<ClusterReadingInfo>();
            foreach (var kv in powerElems)
                if (kv.Key.StartsWith(chamber + "_", StringComparison.OrdinalIgnoreCase)
                    && kv.Value is BasePowerSupply p)
                    list.Add(ClusterReadingInfo.For(kv.Key, chamber, p.Power));
            return list.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        double? DriveOf(string chamber)
        {
            if (stageElems == null) return null;
            foreach (var kv in stageElems)
                if (kv.Key.StartsWith(chamber + "_", StringComparison.OrdinalIgnoreCase)
                    && kv.Value is BaseStage st)
                    return st.LastPosition;
            return null;
        }

        ClusterValveInfo[] ValvesOf(string chamber)
        {
            if (valveElems == null) return System.Array.Empty<ClusterValveInfo>();
            var list = new List<ClusterValveInfo>();
            foreach (var kv in valveElems)
                if (kv.Value is BaseValve v
                    && kv.Key.StartsWith(chamber + "_", StringComparison.OrdinalIgnoreCase))
                    list.Add(new ClusterValveInfo(kv.Key, kv.Key,
                        v.LastOpen));   // cached; SV_* excluded (different prefix)
            return list.ToArray();
        }

        ClusterSourceInfo[] SourcesOf(string chamber)
            => _clusterSources!.TryGetValue(chamber, out var s)
                ? s : System.Array.Empty<ClusterSourceInfo>();

        (bool turbo, bool scroll) PumpsOf(string chamber)
        {
            bool turbo = false, scroll = false;
            if (pumpElems != null)
                foreach (var kv in pumpElems)
                    if (kv.Value is BasePump pump)
                    {
                        // Assumes the Cluster_Demo pump naming convention <chamber>_TP (turbo)
                        // and <chamber>_SP (scroll); matched by the _T / _S letter after the underscore.
                        bool on = pump.LastRunning;   // cached, no PLC I/O
                        if (kv.Key.StartsWith(chamber + "_T", StringComparison.OrdinalIgnoreCase)) turbo = on;
                        else if (kv.Key.StartsWith(chamber + "_S", StringComparison.OrdinalIgnoreCase)) scroll = on;
                    }
            return (turbo, scroll);
        }

        bool SlitOpenOf(string chamber)
        {
            if (valveElems == null || robot == null) return false;
            if (!robot.FaceSlitMap.TryGetValue(chamber, out var slitId)) return false;
            return valveElems[slitId] is BaseValve v && v.LastOpen;   // cached, no PLC I/O
        }

        int? FaceAngleOf(string chamber)
            => robot != null && robot.FaceAngleMap.TryGetValue(chamber, out var a) ? a : (int?)null;

        SystemStateCode StateOf(string chamber)
            => (SystemStateCode)(byte)core.GetChamberState(chamber);

        ClusterRobotInfo robotInfo;
        if (robot != null)
        {
            var (tmTurbo, tmScroll) = PumpsOf("TM");
            robotInfo = new ClusterRobotInfo(
                (RobotStateCode)(byte)robot.State, robot.CurrentFace, robot.AngleNorm,
                robot.ExtendNorm, robot.HasWafer,
                robot.ErrorCode != APS.SystemControl.Core.HWClasses.Base.RobotErrorCode.Ok,
                PressureOf("TM"), tmTurbo, tmScroll);
        }
        else
        {
            robotInfo = new ClusterRobotInfo(RobotStateCode.Startup, 0, 0, 0, false, false, null, false, false);
        }

        string? skip = core.MainIsAutoFallback ? CoreConstants.DefaultChamberId : null;
        var snapshot = APS.SystemControl.Core.Cluster.ClusterSnapshotBuilder.Build(
            core.ChamberIds, skip, StateOf, PressureOf, SlitOpenOf, FaceAngleOf, robotInfo,
            temperatureOf: TemperatureOf,
            flowsOf: FlowsOf,
            powersOf: PowersOf,
            valvesOf: ValvesOf,
            drivePositionOf: DriveOf,
            pumpsOf: PumpsOf,
            sourcesOf: SourcesOf);

        panel.UpdateCluster(snapshot);
    }

    /// <summary>
    /// Registers hover hints on value-adding FrmMain-level controls.
    /// Toolbar ToolStripButtons already carry ToolTipText — leave them.
    /// Schematic widget hints are set on the schematic container; the
    /// per-device widgets live inside VacuumSchematic and are created
    /// dynamically from Controls.xml — they are not hinted individually here.
    /// </summary>
    private void RegisterFrmMainHints()
    {
        _hints.Set(tabControlMain,
            "Navigation tabs|Switch between System (vacuum schematic + HW panels), " +
            "Process (recipe progress + telemetry), Graphs (live sensor plots), " +
            "and Scheduler (automated job queue) views.");

        // Vacuum schematic hint intentionally not registered — the schematic
        // shows no hover hint (tooltip + status-bar push) per user request.

        _hints.Set(alarmBanner,
            "Active alarms|Displays unacknowledged system alarms. " +
            "Click the reset button to clear alarms once the condition is resolved.");

        _hints.Set(overrideBanner,
            "Safety override active|One or more alarm/interlock overrides are in effect. " +
            "Click to open the Overrides dialog (Service → Overrides) and review them. " +
            "Overrides bypass hardware safety guards — use only during maintenance.");
    }

    private void LoadVacuumComponentsXML(string fileName)
    {
        try
        {
            var guiconf = Serializer<XML_Controls>.Load(fileName);
            if (guiconf == null) return;
            if (guiconf.Vacuum == null) return;

            vacuumSchematic.LoadFromXml(guiconf.Schematic);
            vacuumSchematic.ResetZoom();

            if (guiconf.Vacuum.Valves != null) { LoadValves(guiconf.Vacuum.Valves); }

                if (guiconf.Vacuum.MFCs != null) { LoadMFCs(guiconf.Vacuum.MFCs); }

                if (guiconf.Vacuum.Sensors != null) { LoadSensors(guiconf.Vacuum.Sensors); }

                if (guiconf.Vacuum.Leds != null) { LoadLeds(guiconf.Vacuum.Leds); }

                if (guiconf.Vacuum.Pumps != null) { LoadPumps(guiconf.Vacuum.Pumps); }

                if (guiconf.Vacuum.TPumps != null) { LoadTPumps(guiconf.Vacuum.TPumps); }

                if (guiconf.Vacuum.Stages != null) { LoadStages(guiconf.Vacuum.Stages); }

                if (guiconf.Vacuum.GasInlets != null) { LoadGasInlets(guiconf.Vacuum.GasInlets); }

                if (guiconf.Vacuum.Magnetrons != null) { LoadMagnetrons(guiconf.Vacuum.Magnetrons); }

                if (guiconf.Vacuum.GateValves != null) { LoadGateValves(guiconf.Vacuum.GateValves); }

                if (guiconf.Vacuum.SPumps != null) { LoadSPumps(guiconf.Vacuum.SPumps); }

                if (guiconf.Vacuum.TConnectors != null) { LoadTConnectors(guiconf.Vacuum.TConnectors); }

                if (guiconf.Vacuum.AirInlets != null) { LoadAirInlets(guiconf.Vacuum.AirInlets); }

                if (guiconf.Vacuum.ThrottleValves != null) { LoadThrottleValves(guiconf.Vacuum.ThrottleValves); }

                if (guiconf.Vacuum.GasMixers != null) { LoadGasMixers(guiconf.Vacuum.GasMixers); }

                if (guiconf.Vacuum.TransferModules != null) { LoadTransferModules(guiconf.Vacuum.TransferModules); }

            // Gas inlets are always-on supply lines — glow their pipes at startup
            vacuumSchematic.ApplyAlwaysFlowing();

            DisplayLogMessage("Core", $"GUI Controls loaded from {fileName}");
        }
        catch (Exception ex)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            File.WriteAllText(logPath, $"{ex.Message}\n\n{ex.StackTrace}\n\nInner: {ex.InnerException?.Message}\n{ex.InnerException?.StackTrace}");
            ThemedMessageBox.Show(this, $"Error loading GUI components (see error.log):\n{ex.Message}\n\n{ex.StackTrace}");
        }
    }

}
