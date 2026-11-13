using System.ComponentModel;
using System;
using System.Runtime.InteropServices;
using APS.Controls.General;
using APS.Controls.Theme;
using APS.Controls.Vacuum;
using APS.Icons;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Hosting;
using APS.SystemControl.Forms;

namespace APS.SystemControl;

public partial class FrmMain : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    public bool _disposed = false;

    private ToolStripStatusLabel _lblHint = null!;
    private HintManager _hints = null!;

    private readonly ChannelPool? pool;
    private readonly LogThread? logThread;
    private readonly CoreThread? systemThread;
    private APS.SystemControl.Core.Remote.Agent.AgentServer? _agent;
    private APS.SystemControl.Core.Remote.Agent.EventLogRingBuffer? _agentLog;
    /// <summary>Why the agent server isn't running, for the status-strip tooltip. Null while it is.</summary>
    private string? _agentOffReason = "Not started.";
    private string _agentEndpoint = "";
    private string recipeFileName = "";
    // Operator-edited parameter values captured in the recipe selector. Applied
    // per-run (the .rcpx template on disk is never changed). Empty for legacy
    // .recipe files that have no parameter form.
    private Dictionary<string, string> recipeParameters = new(StringComparer.OrdinalIgnoreCase);
    // Path of the .depo.json for the in-flight run, set at run start, finalized at run end.
    private string? _currentSnapshotPath;
    // Re-run lineage: the source snapshot's fingerprint when started from a past snapshot.
    private string? _pendingBasedOn;
    private readonly AlarmBannerPanel alarmBanner = new();

    /// <summary>
    /// Orange banner docked above alarmBanner; visible whenever any runtime
    /// alarm/interlock override is active. Clicking reopens frmOverrides.
    /// </summary>
    private readonly Label overrideBanner = new()
    {
        Dock = DockStyle.Bottom,
        Height = 24,
        BackColor = Color.FromArgb(210, 130, 0),
        ForeColor = Color.White,
        Font = ScaledFont.Create(9f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        Cursor = Cursors.Hand,
        Visible = false,
    };

    /// <summary>
    /// Transient orange toast shown at the top-right of frmMain for ~3 seconds
    /// whenever an interlock bypass lets an HW operation through. Triggered by
    /// MsgType.OverrideFlash on the GUI channel.
    /// </summary>
    private readonly Label overrideToast = new()
    {
        BackColor = Color.FromArgb(210, 130, 0),
        ForeColor = Color.White,
        Font = ScaledFont.Create(9f, FontStyle.Bold),
        Padding = new Padding(8, 4, 8, 4),
        AutoSize = true,
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly System.Windows.Forms.Timer overrideToastTimer = new() { Interval = 3000 };

    /// <summary>
    /// Non-modal error toast for MsgType.Error / MsgType.Interlock from the core
    /// (rejected manual commands, hardware faults, interlock denials). Not modal: on the
    /// real machine a dialog parked over the schematic mid-recipe hides the screen exactly when
    /// the operator needs it. A newer message replaces the text and restarts the timer;
    /// every message is also published to the EventLogBus so it stays in the Events
    /// overlay / Log grid.
    /// </summary>
    private readonly Label errorToast = new()
    {
        BackColor = Color.FromArgb(170, 30, 30),
        ForeColor = Color.White,
        Font = ScaledFont.Create(9f, FontStyle.Bold),
        Padding = new Padding(8, 4, 8, 4),
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        Visible = false,
        BorderStyle = BorderStyle.FixedSingle,
        Cursor = Cursors.Hand,
    };
    private readonly System.Windows.Forms.Timer errorToastTimer = new() { Interval = 8000 };
    private readonly BindingList<LogEvent> events = [];
    frmEditor editorForm = new frmEditor();

    private readonly Config config = new();

    private DateTime _runStartedUtc;

    public void RunThreads(Action<string, int>? progress = null)
    {
        if (systemThread != null && logThread != null)
        {
            if (systemThread.LoadConfig(config, progress))
            {
                progress?.Invoke("Starting threads...", 90);
                logThread.Start();
                systemThread.Start();
            }
            else throw new Exception("Error loading hardware configuration.\n" + systemThread.LastErrorMessage);
        }
    }

    /// <summary>One line to the EventLogBus (Events overlay / Log grid) AND to bin\Logs\events_*.log
    /// via the LOG channel — the bus alone is an in-memory/UI sink with no file backing.</summary>
    internal void PublishEvent(string source, string msg, APS.SystemControl.Core.Logging.LogEventLevel level)
    {
        APS.SystemControl.Core.Logging.EventLogBus.Publish(source, msg, level);
        pool?.WriteLOG(new ValMsg<string>(MsgType.Log, DeviceType.System, source, msg));
    }

    private void StartAgentServer()
    {
        try
        {
            if (pool is null || systemThread?.Core is null)
            {
                _agentOffReason = "Core is not initialised.";
                return;
            }
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var cfg = APS.SystemControl.Core.Remote.Agent.AgentServerConfig.Load(
                Path.Combine(baseDir, "AgentServer.xml"));
            if (!cfg.Enabled || cfg.Keys.Count == 0)          // feature off
            {
                _agentOffReason = cfg.Enabled
                    ? "No API keys configured in AgentServer.xml."
                    : "Disabled in AgentServer.xml.";
                return;
            }

            if (!APS.SystemControl.Hosting.ControlApp.Edition.Features.Has(APS.SystemControl.Hosting.EditionFeatureNames.AgentMcp))
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Agent server disabled: license lacks 'agent-mcp'.");
                _agentOffReason = "License does not include the 'agent-mcp' feature.";
                return;
            }

            _agentLog = new APS.SystemControl.Core.Remote.Agent.EventLogRingBuffer();
            var sys = new APS.SystemControl.Core.Remote.Agent.CoreAgentSystemAccess(
                systemThread, pool, _agentLog);

            var core = systemThread.Core;
            var dispatcher = new APS.SystemControl.Core.Remote.Agent.AgentDispatcher(sys,
                msg => PublishEvent("Agent", msg, APS.SystemControl.Core.Logging.LogEventLevel.Operator),
                cls => core.ControlMode.Authorize(CommandOrigin.Host, cls))
                { Capabilities = APS.SystemControl.Core.Remote.Agent.AgentCapabilities.For(APS.SystemControl.Hosting.ControlApp.Edition) };
            var cert = APS.SystemControl.Core.Remote.Agent.AgentCertificate.LoadOrCreate(
                Path.Combine(baseDir, "AgentServer.pfx"));
            _agent = new APS.SystemControl.Core.Remote.Agent.AgentServer(cfg, dispatcher, cert,
                APS.SystemControl.Hosting.ControlApp.Edition.Find<APS.SystemControl.Core.Remote.Agent.IAgentStreamExtension>());
            _agent.Start();
            _agentEndpoint = $"{cfg.Bind}:{_agent.BoundPort}";
            _agentOffReason = null;
            APS.SystemControl.Core.Logging.EventLogBus.Publish("Agent",
                $"Agent server listening on {_agentEndpoint} (cert {cert.Thumbprint})",
                APS.SystemControl.Core.Logging.LogEventLevel.Detail);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Agent server failed to start: {ex.Message}");
            // Start() may have bound the listener before throwing (or thrown from the
            // accept-loop kickoff) — release it rather than leaving a half-open socket.
            try { _agent?.Stop(); } catch { }
            _agent = null;
            _agentOffReason = $"Failed to start: {ex.Message}";
        }
        // The indicator is built later in FrmMain_Load (it has to follow the status-strip
        // springs), so this is a no-op on the startup path and only matters if the server
        // is ever restarted while the form is up.
        RefreshAgentStatus();
    }

    public FrmMain(Action<string, int>? progress = null)
    {
        this.AutoScaleMode = AutoScaleMode.Font;
        try
        {
            progress?.Invoke("Initializing UI...", 10);
            InitializeComponent();
            _userLayout = UserLayout.Load();
            ApplyMainWindowPlacement();
            APS.SystemControl.Core.UIScaling.ApplyToForm(this);

            // Main navigation tabs: larger header text and a taller strip than the
            // DarkTabControl default so the primary System/Process/... targets are
            // prominent and hard to misclick, especially at high DPI. Use HeaderFont,
            // NOT Font: TabControl.Font is ambient and cascades into every TabPage's
            // children — that would oversize the HW-panel buttons/checkboxes/radios
            // which inherit their font.
            tabControlMain.HeaderFont = new Font("Segoe UI", 11f);
            tabControlMain.Padding = new Point(DpiScale.Px(22), DpiScale.Px(6));
            tabControlMain.ItemSize = new Size(0, DpiScale.Px(40));

            // Replace resource-based toolbar icons with line-art style at 24 logical px.
            // ONE ImageList for the whole strip — the buttons added later in
            // FrmMain.Toolbar.cs (InitializeToolbarExtensions) reference it by ImageKey.
            // ToolStrip does NOT scale ImageScalingSize under SystemAware DPI, so the
            // helper gets the DPI-scaled size (36 / 48 px on a 4K 150-200 % monitor).
            IconLibrary.ConfigureToolStrip(toolStrip1, DpiScale.Px(24),
                IconName.SelectRecipe, IconName.Edit, IconName.Run, IconName.Stop,
                IconName.Sources, IconName.GraphLog, IconName.EditLayout);
            toolStrip1.AutoSize = true;
            // Designer buttons carry a resource Image, which always wins over ImageKey.
            btnSelect.Image = null;
            btnSelect.ImageKey = IconName.SelectRecipe;
            btnSelect.Click += ButtonLoadRecipe_Click;
            btnEdit.Image = null;
            btnEdit.ImageKey = IconName.Edit;
            btnStart.Image = null;
            btnStart.ImageKey = IconName.Run;
            btnStart.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            btnStart.Text = "Start";
            toolStripButton1.Image = null;
            toolStripButton1.ImageKey = IconName.Stop;
            toolStripButton1.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            toolStripButton1.Text = "Abort";
            toolStripButton1.Click += AbortToolStripButton_Click;

            // Resume — the way out of a hold. Hidden unless Core.IsHeldForOperator; sits
            // immediately right of Abort, which is deliberately left exactly as it is:
            // AbortToolStripButton_Click already ends a held run through AbortRecipe (it
            // cancels the run token, and the held decision-wait observes it through its
            // linked CTS and unwinds as operator-stop).
            btnResume = new ToolStripButton
            {
                Name = "btnResume",
                ImageKey = IconName.Run,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Text = "Resume",
                ToolTipText = "Resume the held run — re-runs the failed step",
                Visible = false,             // only while held
            };
            btnResume.Click += (_, _) => systemThread?.ResolveFault(FaultDecision.Retry);
            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(toolStripButton1) + 1, btnResume);

            // HELD banner, leftmost on the status strip (inserted at 0, ahead of the chamber
            // tiles InitStateIndicator adds below). Amber — the same warning ink the Processing
            // chamber dot and the Paused recipe badge use — so "the machine is yours" reads at
            // a glance from across the room.
            lblHeld = new ToolStripStatusLabel
            {
                Name = "lblHeld",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 183, 77),
                Margin = new Padding(6, 0, 6, 0),
                Visible = false,
            };
            statusStrip1.Items.Insert(0, lblHeld);

            // The hold flag is set on the RECIPE thread just after the dialog's Hold click
            // resolves, so "FaultPauseEnd" can reach the UI before it is set — the banner must
            // not be driven off that message alone. This 1 s timer reconciles it from the
            // cached flag (never a live PLC read, per the house rule for UI timers).
            // The timer is STARTED by InitStateIndicator further down, which bails out when
            // Core is null — harmless here, since a null Core can never hold a run either.
            _stateElapsedTimer.Tick += (_, _) => UpdateHeldUi();

            // Load icon from file (all sizes) and force taskbar update via Win32
            var iconPath = Path.Combine(AppContext.BaseDirectory, "DepositionControl2_Icon.ico");
            if (File.Exists(iconPath))
            {
                var icon = new Icon(iconPath);
                Icon = icon;   // the Form holds this reference; not eligible for finalization
                // Root the WM_SETICON icons for the form's lifetime: passing a raw
                // HICON to WM_SETICON does not transfer ownership, so if these Icon
                // objects were collected their finalizers would DestroyIcon the
                // handles the window still uses (blank/garbled icon mid-session).
                // Disposed in OnFormClosed. (audit U-3)
                _iconSmall = new Icon(icon, 16, 16);
                _iconLarge = new Icon(icon, 48, 48);
                SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, _iconSmall.Handle);
                SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG, _iconLarge.Handle);
            }

            // Override banner (docked bottom; stacks ABOVE alarmBanner because
            // DockStyle.Bottom controls stack in add-order).
            overrideBanner.Click += (_, _) => OverridesToolStripMenuItem_Click(null, EventArgs.Empty);
            this.Controls.Add(overrideBanner);

            overrideToast.Parent = this;
            overrideToast.BringToFront();
            overrideToastTimer.Tick += (_, _) => { overrideToast.Visible = false; overrideToastTimer.Stop(); };

            errorToast.Parent = this;
            errorToast.BringToFront();
            errorToast.Click += (_, _) => { errorToast.Visible = false; errorToastTimer.Stop(); };
            errorToastTimer.Tick += (_, _) => { errorToast.Visible = false; errorToastTimer.Stop(); };

            alarmBanner.AlarmsReset += () =>
            {
                systemThread?.Core?.ActiveAlarms.Clear();
                pool?.GUIWriter?.TryWrite(new ValMsg<string>(MsgType.Alarm, DeviceType.System, "alarm_reset", ""));
            };
            this.Controls.Add(alarmBanner);

            tabControlMain.ApplyPageTheme();
            DarkModeHelper.FixControls(this);

            progress?.Invoke("Setting up channels...", 20);
            pool = new();
            if (pool.GUIReader != null && pool.GUIWriter != null)
            {
                // StartListening is async (it yields at the channel await), so a
                // dedicated LongRunning thread would be abandoned at the first
                // await. Use Task.Run and observe the inner Task instead of
                // discarding a Task<Task>: per-callback exceptions are already
                // isolated inside StartListening, but guard the enumeration. (audit U-2)
                _ = Task.Run(async () =>
                {
                    try { await pool.GUIReader.StartListening(GUIMessageDispatcher); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError($"GUI channel listener terminated: {ex}");
                    }
                });

                logThread = new(pool);
                systemThread = new(pool);
            }
            else
                throw new ArgumentNullException(nameof(pool.GUIReader), "Pool cannot be null");
            RunThreads(progress);
            // StartAgentServer() is deferred to FrmMain_Load, after
            // ConfigureLogGrid(): EventLogBus.Entry is a plain event with no replay buffer and
            // its Log-tab grid subscriber attaches only in Load, so status lines published from
            // this ctor would be silently dropped. StartAgentServer() wraps its whole body in
            // its own try/catch, and nothing else reads _agent/_agentLog before Load runs.
            progress?.Invoke("Ready", 100);
            InitStateIndicator();

            // LoadConfig/InitializeOverrides is synchronous inside RunThreads, so by here
            // Core.Overrides is assigned.
            if (systemThread?.Core?.Overrides is { } reg)
            {
                reg.OverridesChanged += (_, _) => UpdateOverrideBanner();
                reg.SuppressedAlarmRefreshed += (_, ev) =>
                {
                    // Event fires from the Core poll thread — marshal to UI. Drop late
                    // events during teardown (BeginInvoke on a disposed form throws).
                    if (IsDisposed || Disposing) return;
                    if (InvokeRequired) BeginInvoke(() => alarmBanner.RefreshSuppressedAlarm(ev.Id, ev.Message));
                    else alarmBanner.RefreshSuppressedAlarm(ev.Id, ev.Message);
                };
                UpdateOverrideBanner();
            }

            // Load sources FIRST: modules read sourceManager.MagnetronInstances in OnCoreReady.
            // FrmMain_Load loads sources too, but it runs AFTER this ctor.
            if (System.IO.File.Exists(config.SourcesFile))
                sourceManager.Load(config.SourcesFile);
            RunModules(nameof(IControlModule.OnCoreReady), m => m.OnCoreReady(this));
        }
        catch (Exception ex)
        {
            // The global CrashReporter hook writes crash.log and shows the structured dialog.
            ThemedMessageBox.Show(this, $"{ex.Message}\n\nSee crash.log for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
        this.FormClosing += FrmMain_FormClosing;
    }

    private Control? FindControlByName(string controlName)
    {
        Control[] controls = this.Controls.Find(controlName, true);
        return controls.Length > 0 ? controls[0] : null;
    }

    // WM_SETICON icons kept rooted for the form's lifetime (audit U-3).
    private Icon? _iconSmall;
    private Icon? _iconLarge;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        APS.SystemControl.Core.Logging.EventLogBus.Entry -= OnBusEntryForGrid;
        _iconSmall?.Dispose();
        _iconLarge?.Dispose();
        base.OnFormClosed(e);
    }

    public void DisplayAlarm(ThreadMsg msg)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => DisplayAlarm(msg)));
            return;
        }

        if (msg.Device == "alarm_reset")
            return;

        alarmBanner.AddAlarm(((ValMsg<string>)msg).Value);
    }

    /// <summary>
    /// Asynchronous error surface for core-originated Error / Interlock messages:
    /// a red toast that fades on its own (or on click) plus an operator-level
    /// Events line. No modal — the run keeps going and nothing has to be clicked.
    /// </summary>
    private void ShowErrorMessage(string message, string source)
    {
        PublishEvent(source, message, APS.SystemControl.Core.Logging.LogEventLevel.Operator);
        ExecuteOnUIThread(() => ShowErrorToast(message));
    }

    private void ShowErrorToast(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => ShowErrorToast(text)); return; }
        errorToast.Text = $"✖ {text}";
        int top = overrideToast.Visible ? overrideToast.Bottom + 8 : 48;
        errorToast.Location = new Point(ClientSize.Width - errorToast.Width - 16, top);
        errorToast.Visible = true;
        errorToast.BringToFront();
        errorToastTimer.Stop();
        errorToastTimer.Start();
    }

    private void GUIMessageDispatcher(ThreadMsg msg)
    {
        // Handle per-chamber SystemState change notifications. SetChamberState
        // emits Device == "SystemState" for the default chamber and
        // "SystemState:{id}" for all others.
        if (msg.Target == DeviceType.System
            && msg is ParamMsg stateMsg
            && msg.Device is string dev
            && (dev == "SystemState" || dev.StartsWith("SystemState:", StringComparison.Ordinal))
            && stateMsg.Args.Length >= 3)
        {
            var chamberId = dev == "SystemState"
                ? CoreConstants.DefaultChamberId
                : dev.Substring("SystemState:".Length);

            var newState = (SystemState)Convert.ToInt32(stateMsg.Args[0]);
            bool isUserRecipe = Convert.ToBoolean(stateMsg.Args[2]);

            if (newState == SystemState.Processing && isUserRecipe)
                InvokeAction(() =>
                {
                    if (tabProcess.Controls.Count > 0)
                        tabControlMain.SelectedTab = tabProcess;
                });

            InvokeAction(() => UpdateChamberStateLabel(chamberId, newState));
            if (chamberId == CoreConstants.DefaultChamberId)
                InvokeAction(() => UpdateEmbeddedRecipeButtons(newState));   // notIn="…" gating
            InvokeAction(OnRunStateChangedForQuota);   // pause/resume metering around runs
            InvokeAction(() =>
            {
                var running = systemThread?.Core?.IsRecipeRunning == true;
                RunModules(nameof(IControlModule.OnRunStateChanged), m => m.OnRunStateChanged(this, running));
            });

            msg.Dispose();
            return;
        }

        switch (msg.Type)
        {
            case MsgType.Error:
                ShowErrorMessage(((ValMsg<string>)msg).Value, "Error");
                break;
            case MsgType.Interlock:
                ShowErrorMessage(((ValMsg<string>)msg).Value, "Interlock");
                break;
            case MsgType.Alarm:
                DisplayAlarm(msg);
                break;
            case MsgType.OverrideFlash:
                if (msg is ValMsg<string> flash) ShowOverrideFlash(flash.Value);
                break;
            case MsgType.Log:
                InvokeAction(() => DisplayLogMessage(msg));
                break;
            case MsgType.Progress:
                HandleRecipeProgress(msg);
                break;
            case MsgType.Command:
                HandleCommand(msg);
                break;
            default:
                UpdateControl(msg);
                break;
        }

        // Update system LEDs from Status messages (any device connected = POWER on)
        if (msg.Type == MsgType.Status)
            UpdateSystemLeds(msg);

        msg.Dispose();
    }

    private void HandleCommand(ThreadMsg msg)
    {
        if (msg.Device == "Message" && msg is ValMsg<string> valMsg)
        {
            // Capture the value before posting: GUIMessageDispatcher disposes msg
            // after this returns. Post non-blocking so the GUI-channel consumer is
            // not parked inside the modal loop; AcknowledgeMessage() runs on the UI
            // thread right after the operator dismisses the dialog (the recipe stays
            // paused until then), so live updates keep flowing while it is open.
            string text = valMsg.Value;
            ExecuteOnUIThread(() =>
            {
                frmMessage.ShowMessage(this, text);
                systemThread?.AcknowledgeMessage();
            });
        }
        else if (msg.Device == "FaultPause" && msg is ValMsg<APS.SystemControl.Core.FaultPauseInfo> fp)
        {
            // Modeless: the recipe is held by the engine's decision TCS, not by a modal loop,
            // so telemetry and the process panel keep updating while the operator looks.
            var info = fp.Value;
            ExecuteOnUIThread(() =>
            {
                frmFaultPause.Present(this, info, d => systemThread?.ResolveFault(d));
                UpdateHeldUi();
            });
        }
        else if (msg.Device == "FaultPauseEnd")
        {
            // A held pause sends this TWICE — once when the hold begins (closing the dialog for
            // good) and again when the hold finally ends. Dismiss is a no-op the second time:
            // it clears the static _current before closing, and FormClosed clears it too.
            ExecuteOnUIThread(() => { frmFaultPause.Dismiss(); UpdateHeldUi(); });
        }
    }

    /// <summary>
    /// True while the held affordances are on screen. Only used to spot the EDGE, so the
    /// process-panel header is rewritten once per transition rather than every timer tick.
    /// </summary>
    private bool _heldUiShown;

    private const string HeldStepPrefix = "HELD — manual control unlocked  ·  ";

    /// <summary>
    /// Shows/hides the held affordances from <c>Core.IsHeldForOperator</c>. Call on the UI
    /// thread whenever the hold state may have changed: the pause dialog opening or closing,
    /// the recipe-progress message, and the 1 s state timer (which is what actually catches
    /// the hold, since the flag is set on the recipe thread AFTER "FaultPauseEnd" is posted).
    /// <para>Reads <c>IsHeldForOperator</c>, not <c>IsManualControlGated</c>: the latter is the
    /// gate the command paths check, and is false for an idle machine too.</para>
    /// </summary>
    private void UpdateHeldUi()
    {
        bool held = systemThread?.Core?.IsHeldForOperator ?? false;
        if (btnResume is not null) btnResume.Visible = held;
        if (lblHeld is not null)
        {
            lblHeld.Visible = held;
            lblHeld.Text = held ? "HELD — manual control unlocked" : "";
        }

        if (held == _heldUiShown) return;
        _heldUiShown = held;
        ShowHeldOnProcessPanel(held);
    }

    /// <summary>
    /// Second place the hold shows: the process panel's header line. Nothing on the wire says
    /// "held" — <c>RecipeState</c> has no such member and a held pause deliberately sends no
    /// further progress frame (that would restart the agent's step clock, which is the only
    /// signal of how long a tool has been parked). So this re-pushes the LAST CACHED
    /// <c>RecipeInfo</c> with the header's step text prefixed. No new wire message, no contract
    /// change: purely a repaint of what the panel was already showing.
    /// </summary>
    private void ShowHeldOnProcessPanel(bool held)
    {
        if (_lastRecipeInfo is not { } info) return;
        string desc = info.StepDescription;
        if (desc.StartsWith(HeldStepPrefix, StringComparison.Ordinal))
            desc = desc[HeldStepPrefix.Length..];
        if (held) desc = HeldStepPrefix + desc;
        if (desc == info.StepDescription) return;
        _lastRecipeInfo = info with { StepDescription = desc };
        FindProcessPanel()?.UpdateRecipe(_lastRecipeInfo);
    }

    private int _lastStepNumber = -1;
    private DateTime _stepStartTimeUtc = DateTime.UtcNow;
    // Cached for the telemetry timer to refresh step-elapsed during long dwells
    // (deposit, warmup, pwait) — without this, ElapsedSeconds is only recomputed
    // on step boundaries and the card shows 00:00 for the entire step.
    private APS.ProcessPanel.Contracts.RecipeInfo? _lastRecipeInfo;
    // Bounded progress window reported by the Worker (e.g. Deposit's shutter
    // dwell). Zero means "unknown / no bounded window active" and the card
    // falls back to an unbounded "NN:NN elapsed" display.
    private double _stepTotalSeconds;
    private string? _stepDescriptionOverride;

    private void HandleRecipeProgress(ThreadMsg msg)
    {
        // DepositStart — Worker signals the deposit dwell is about to start on a flange.
        // Push the flange+duration directly to the panel now: the next RecipeProgress
        // frame is emitted only at the next step boundary (after the deposit ends),
        // which is too late. Panel's OnStepChanged is additive — subsequent telemetry
        // ticks with empty Parameters won't clear the stored value.
        //   Args: [flangeNumber:int, durationSeconds:double]
        if (msg is ParamMsg dp && msg.Device == "DepositStart" && dp.Args.Length >= 2)
        {
            int flange = Convert.ToInt32(dp.Args[0]);
            double seconds = Convert.ToDouble(dp.Args[1]);
            var parameters = new Dictionary<string, APS.ProcessPanel.Contracts.ProcessParameter>
            {
                [APS.ProcessPanel.Contracts.ProcessParameterKeys.DepositFlange] =
                    new APS.ProcessPanel.Contracts.ProcessParameter(
                        APS.ProcessPanel.Contracts.ProcessParameterKeys.DepositFlange, flange, ""),
                [APS.ProcessPanel.Contracts.ProcessParameterKeys.DepositSeconds] =
                    new APS.ProcessPanel.Contracts.ProcessParameter(
                        APS.ProcessPanel.Contracts.ProcessParameterKeys.DepositSeconds, seconds, "sec"),
            };
            var depositStepInfo = new APS.ProcessPanel.Contracts.ProcessStepInfo(
                StepType: _lastRecipeInfo?.StepDescription ?? "deposit",
                Material: null,
                ElapsedSeconds: 0,
                TotalSeconds: seconds,
                Parameters: parameters);
            var depPp = FindProcessPanel();
            if (depPp != null)
                ExecuteOnUIThread(() => depPp.UpdateStep(depositStepInfo));
            return;
        }

        // StepProgress — Worker signals a bounded dwell window
        //   Args: [description:string, totalSeconds:double]
        if (msg is ParamMsg sp && msg.Device == "StepProgress" && sp.Args.Length >= 2)
        {
            _stepStartTimeUtc = DateTime.UtcNow;
            _stepDescriptionOverride = (string)sp.Args[0];
            _stepTotalSeconds = Convert.ToDouble(sp.Args[1]);
            return;
        }

        if (msg is not ParamMsg paramMsg || paramMsg.Args.Length < 7) return;

        var info = new APS.ProcessPanel.Contracts.RecipeInfo(
            Name: (string)paramMsg.Args[0],
            CurrentStep: Convert.ToInt32(paramMsg.Args[1]),
            TotalSteps: Convert.ToInt32(paramMsg.Args[2]),
            StepDescription: (string)paramMsg.Args[3],
            State: (APS.ProcessPanel.Contracts.RecipeState)Convert.ToInt32(paramMsg.Args[4]),
            Elapsed: TimeSpan.FromSeconds(Convert.ToDouble(paramMsg.Args[5])),
            EstimatedRemaining: Convert.ToDouble(paramMsg.Args[6]) >= 0
                ? TimeSpan.FromSeconds(Convert.ToDouble(paramMsg.Args[6]))
                : null,
            CurrentStack: paramMsg.Args.Length > 7 ? Convert.ToInt32(paramMsg.Args[7]) : 0,
            TotalStacks:  paramMsg.Args.Length > 8 ? Convert.ToInt32(paramMsg.Args[8]) : 0
        );

        // Track step transitions so we can report ElapsedSeconds for the current step.
        // The Worker only publishes recipe-level timing via ParamMsg, so step elapsed
        // is derived from a wall-clock timestamp recorded on each step boundary.
        // TotalSeconds stays 0 (unknown until the Worker exposes per-step totals).
        if (info.CurrentStep != _lastStepNumber)
        {
            _lastStepNumber = info.CurrentStep;
            _stepStartTimeUtc = DateTime.UtcNow;
            _stepTotalSeconds = 0;
            _stepDescriptionOverride = null;
        }
        var prevState = _lastRecipeInfo?.State;
        _lastRecipeInfo = info;

        // Finalize the deposition snapshot on the first active→terminal transition.
        // Active = Running/Paused/Aborting; terminal = Complete/Error/Idle.
        // _currentSnapshotPath is cleared afterwards so a second terminal message cannot
        // double-finalize.
        bool wasActive = prevState is APS.ProcessPanel.Contracts.RecipeState.Running
                                   or APS.ProcessPanel.Contracts.RecipeState.Paused
                                   or APS.ProcessPanel.Contracts.RecipeState.Aborting;
        bool nowTerminal = info.State is APS.ProcessPanel.Contracts.RecipeState.Complete
                                      or APS.ProcessPanel.Contracts.RecipeState.Error
                                      or APS.ProcessPanel.Contracts.RecipeState.Idle;
        if (wasActive && nowTerminal)
        {
            string result = info.State switch
            {
                APS.ProcessPanel.Contracts.RecipeState.Complete => "completed",
                APS.ProcessPanel.Contracts.RecipeState.Error    => "failed",
                _                                               => "aborted",
            };
            var startedUtc = _runStartedUtc;
            var endedUtc = DateTime.UtcNow;
            // Marshal the _currentSnapshotPath read+clear onto the UI thread so it is
            // serialized with AuthorizeAndRecord, which sets it on the UI thread. The
            // outcome's Means + BasePressure are filled Core-side in FinalizeDepositionSnapshot
            // from the TSV row values, so this only supplies the run's result and window.
            ExecuteOnUIThread(() =>
            {
                if (_currentSnapshotPath is { } path)
                {
                    var outcome = new APS.SystemControl.Core.Evidence.DepositionOutcome
                    {
                        Result = result,
                        Window = new APS.SystemControl.Core.Evidence.DepositionWindow
                        {
                            StartedUtc = startedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                            EndedUtc   = endedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                        },
                    };
                    systemThread?.Core?.FinalizeDepositionSnapshot(path, outcome);
                    _currentSnapshotPath = null;
                }
            });
        }

        double stepElapsed = (DateTime.UtcNow - _stepStartTimeUtc).TotalSeconds;

        var stepInfo = new APS.ProcessPanel.Contracts.ProcessStepInfo(
            StepType: info.StepDescription,
            Material: null,  // Worker does not publish material yet — Card hides the material box.
            ElapsedSeconds: stepElapsed,
            TotalSeconds: 0,  // unknown — Card progress bar stays empty.
            Parameters: new Dictionary<string, APS.ProcessPanel.Contracts.ProcessParameter>()
        );

        var pp = FindProcessPanel();
        if (pp != null)
        {
            ExecuteOnUIThread(() =>
            {
                pp.UpdateRecipe(info);
                pp.UpdateStep(stepInfo);
                // A fresh frame has just overwritten the header, so re-assert the hold marker
                // (or drop it) from the flag rather than leaving whatever this frame carried.
                _heldUiShown = false;
                UpdateHeldUi();
            });
        }
        else
        {
            ExecuteOnUIThread(UpdateHeldUi);
        }
    }

    private void UpdateControl(ThreadMsg msg)
    {
        switch (msg.Target)
        {
            case DeviceType.Valve:
                FindValveControls(msg);
                break;
            case DeviceType.MFC:
                FindMFC(msg);
                break;
            case DeviceType.Power:
                FindPower(msg);
                break;
            case DeviceType.Sensor:
                FindSensor(msg);
                break;
            case DeviceType.Pump:
                FindPump(msg);
                break;
            case DeviceType.Stage:
                FindStage(msg);
                break;
            case DeviceType.Controller:
                FindController(msg);
                break;
            case DeviceType.Drive:
                FindDrive(msg);
                break;
            default:
                break;
        }
    }

    internal void InvokeAction(Action action)
    {
        // A Core/session event can fire during form teardown; invoking on a disposed
        // form throws ObjectDisposedException from a background thread. Drop late events.
        if (IsDisposed || Disposing) return;
        if (this.InvokeRequired)
            this.Invoke(action);
        else
            action();
    }


    private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
    {
        using FrmAbout formAbout = new();
        formAbout.ShowDialog();
    }

    /// <summary>Bounded wait (s) for a recipe to reach a safe state on exit.</summary>
    private const int RecipeAbortWaitSeconds = 10;

    private void FrmMain_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var result = frmMessage.Confirm(this, "Do you really want to exit the application?", "Exit Application");
        if (result == DialogResult.No)
        {
            e.Cancel = true;
            return;
        }

        // If a recipe is still running, abort it and let it reach a safe state over
        // LIVE transports BEFORE we disconnect, so its FAILURE_PROTOCOL does not run over
        // already-closed links. (audit M-6) This UI-thread wait is deadlock-safe because
        // recipe→UI messages post non-blocking (see H-1).
        var core = systemThread?.Core;
        if (core?.IsRecipeRunning == true)
        {
            if (frmMessage.Confirm(this, "A recipe is still running. Abort it and exit?",
                    "Recipe Running") == DialogResult.No)
            {
                e.Cancel = true;
                return;
            }
            core.AbortRecipe();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(RecipeAbortWaitSeconds);
            while (core.IsRecipeRunning && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(50);
        }

        SaveUserLayout();

        // Stop the CoreThread and its background poller/watchdog/dispatcher FIRST so
        // nothing is reading or writing the transports, THEN release them.
        RunModules(nameof(IControlModule.OnClosing), m => m.OnClosing(this));
        // Unsubscribe before Stop(): Stop() raises SessionsChanged one last time, and the
        // handler would post to a form that is already tearing down.
        try { if (_agent is not null) _agent.SessionsChanged -= OnAgentSessionsChanged; } catch { }
        try { _agent?.Stop(); } catch { }
        try { _agentLog?.Dispose(); } catch { }
        try { systemThread?.Stop(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"CoreThread.Stop on exit failed: {ex.Message}");
        }

        // Release COM ports / TCP sockets / PLC links deterministically so the OS
        // reclaims them now instead of at process teardown. Never throws.
        try { systemThread?.Core?.DisconnectAll(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"DisconnectAll on exit failed: {ex.Message}");
        }

        RunModules(nameof(IControlModule.OnShutdown), m => m.OnShutdown(this));
    }

    private void SystemControl_DoubleClick(object? sender, EventArgs e)
    {
        switch (sender)
        {
            case ValveControl valveControl:
                if (valveControl.ID != null)
                {
                    bool target = !valveControl.Open;
                    object[] args = [DeviceType.Valve, valveControl.ID, target];
                    SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Valve", args));
                    valveControl.BeginPending(target);
                }
                break;
            case PumpControl pumpControl:
                if (pumpControl.ID != null)
                {
                    bool target = !pumpControl.Power;
                    object[] args = [DeviceType.Pump, pumpControl.ID, target];
                    SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Pump", args));
                    pumpControl.BeginPending(target);
                }
                break;
            case GateValveControl gateValveControl:
                if (gateValveControl.ID != null)
                {
                    bool target = !gateValveControl.Open;
                    object[] args = [DeviceType.Valve, gateValveControl.ID, target];
                    SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Valve", args));
                    gateValveControl.BeginPending(target);
                }
                break;
            case TurboPumpControl turboPumpControl:
                if (turboPumpControl.ID != null)
                {
                    bool target = !turboPumpControl.IsRunning;
                    object[] args = [DeviceType.Pump, turboPumpControl.ID, target];
                    SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Pump", args));
                    turboPumpControl.BeginPending(target);
                }
                break;
            case ScrollPumpControl scrollPumpControl:
                if (scrollPumpControl.ID != null)
                {
                    bool target = !scrollPumpControl.Power;
                    object[] args = [DeviceType.Pump, scrollPumpControl.ID, target];
                    SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Pump", args));
                    scrollPumpControl.BeginPending(target);
                }
                break;
            default: break;
        }
    }

    private void SendFloatValueMessage(DeviceType target, string id, float value)
    {
        object[] args = [0, id, value];
        switch (target)
        {
            case DeviceType.MFC:
                args[0] = DeviceType.MFC;
                SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "MFC", args));
                break;
            case DeviceType.Stage:
                args[0] = DeviceType.Stage;
                SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Stage", args));
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Halts a stage now via the "StageStop" worker command (pulses the driver
    /// stop bit + aborts the in-flight arrival-wait). Wired to the stage
    /// widget's Stop button and the stage popup.
    /// </summary>
    private void SendStageStop(string id)
    {
        object[] args = [DeviceType.Stage, id];
        SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "StageStop", args), CommandClass.Safety);
    }

    private static string? SelectFile(string ext)
    {
        using OpenFileDialog openFileDialog = new();
        openFileDialog.Filter = $"{ext} files (*.{ext})|*.{ext}|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 1;
        openFileDialog.RestoreDirectory = true;

        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            return openFileDialog.FileName;
        }
        return null;
    }

    private void ButtonLoadRecipe_Click(object? sender, EventArgs e)
    {
        string recipeDir = config.RecipesDir;
        if (!Directory.Exists(recipeDir))
        {
            using OpenFileDialog dlg = new();
            dlg.Filter = "Recipe files (*.rcpx)|*.rcpx|Legacy recipes (*.recipe)|*.recipe|All files (*.*)|*.*";
            dlg.FilterIndex = 1;
            dlg.RestoreDirectory = true;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                recipeFileName = dlg.FileName;
                recipeParameters.Clear(); // legacy .recipe files have no parameter form
                lblRecepieFileName.Text = Path.GetFileNameWithoutExtension(recipeFileName);
            }
            return;
        }

        try
        {
            using var selector = new frmRecipeSelector();
            selector.SetDirectory(recipeDir);
            if (selector.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(selector.SelectedFilePath))
            {
                recipeFileName = selector.SelectedFilePath;
                // Capture the operator's edited parameter values so the run uses
                // them instead of the file defaults (per-run override).
                recipeParameters = new Dictionary<string, string>(selector.Parameters, StringComparer.OrdinalIgnoreCase);
                lblRecepieFileName.Text = Path.GetFileNameWithoutExtension(recipeFileName);

                var info = new APS.ProcessPanel.Contracts.RecipeInfo(
                    Name: Path.GetFileNameWithoutExtension(recipeFileName),
                    CurrentStep: 0,
                    TotalSteps: 0,
                    StepDescription: "Ready",
                    State: APS.ProcessPanel.Contracts.RecipeState.Idle,
                    Elapsed: TimeSpan.Zero,
                    EstimatedRemaining: null
                );
                var pp = FindProcessPanel();
                if (pp != null)
                    ExecuteOnUIThread(() => pp.UpdateRecipe(info));
            }
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, $"Error opening recipe selector:\n{ex.Message}\n\n{ex.StackTrace}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private APS.SystemControl.Core.Evidence.DepositionConditions BuildConditions()
    {
        var (name, format, content) =
            APS.SystemControl.Core.Evidence.EffectiveRecipe.Resolve(recipeFileName, recipeParameters);

        // Sources: magnetron placements from SourceManager + mounted targets from one DB query.
        var placements = (sourceManager?.MagnetronInstances ?? new()).Select(inst =>
        {
            var flange = sourceManager!.Sources.Values.FirstOrDefault(s => s.MagnetronInstanceId == inst.Id);
            return new APS.SystemControl.Core.Evidence.MagnetronPlacement(
                inst.Id, inst.ModelId, inst.Format, flange?.FlangeNumber, inst.ElnLink,
                flange?.ZeroDistanceMm, flange?.LiftPositionMm);
        }).ToList();

        var mounted = ControlApp.Edition.Find<IMountedTargetSource>()?.GetMountedTargets()
            ?? new Dictionary<Guid, APS.SystemControl.Core.Evidence.MountedTarget>();

        var sources = APS.SystemControl.Core.Evidence.MountedSourcesReader.Build(placements, mounted);

        // Attach the connected power supply (+ its ELN link) to each source, keyed by the
        // flange's configured powerSupplyId. Only supplies actually connected at run start are recorded.
        var psuCore = systemThread?.Core;
        if (psuCore is not null && sourceManager is not null)
        {
            foreach (var src in sources)
            {
                if (src.Flange is not int fl) continue;
                string? psuId = sourceManager.GetSource(fl)?.PowerSupplyId;
                if (string.IsNullOrEmpty(psuId)) continue;
                if (psuCore.FindElement(APS.Controls.Core.DeviceType.Power, psuId)
                        is APS.SystemControl.BaseHWClasses.BasePowerSupply psu && psu.Connected)
                    src.PowerSupply = new APS.SystemControl.Core.Evidence.DepositionPsu
                    {
                        Id = psu.Params.ID,
                        Model = psu.GetType().Name,
                        Description = string.IsNullOrWhiteSpace(psu.Params.Description) ? null : psu.Params.Description,
                        Responding = psu.IsResponding,
                        ElnLink = sourceManager.GetPowerSourceElnLink(psuId),
                    };
            }
        }

        var session = APS.SystemControl.Accounts.CurrentUser.Session;
        var signature = new APS.SystemControl.Core.Evidence.DepositionSignature
        {
            UserId = session?.UserId.ToString(),
            Name = session?.DisplayName ?? APS.SystemControl.Accounts.CurrentUser.Name,
            Role = session?.Roles?.FirstOrDefault(),
            SignedAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            Intent = session is null ? "unauthenticated" : "authorized",
        };

        return new APS.SystemControl.Core.Evidence.DepositionConditions
        {
            Specimen = null, // set by the dialog
            Signature = signature,
            Software = new()
            {
                ["control"] = typeof(FrmMain).Assembly.GetName().Version?.ToString() ?? "?",
                ["worker"]  = systemThread?.Core?.WorkerName ?? "?",
            },
            PlcSerials = ControlApp.Edition.Find<IPlcSerialSource>()?.GetPlcSerials().ToArray() ?? [],
            Sources = sources,
            Recipe = new APS.SystemControl.Core.Evidence.DepositionRecipe
            {
                Name = name, Format = format, Content = content
            },
        };
    }

    /// <summary>Show the Authorize &amp; run dialog; on OK stamp the specimen + write the snapshot.
    /// Returns false if the operator cancels (caller aborts the run).</summary>
    private bool AuthorizeAndRecord(string? basedOn)
    {
        var conditions = BuildConditions();
        using var dlg = new Forms.Evidence.AuthorizeRunDialog(conditions);
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        conditions.Specimen = dlg.Specimen;

        // Push the operator-confirmed lift back into the live SourceManager (so the
        // panel/next run start from it) and best-effort persist to Sources.xml — but
        // only when a lift actually changed, to avoid rewriting the config file (and
        // re-serializing the whole in-memory SourceManager) on every authorized run.
        // The signed snapshot already carries the confirmed value regardless of
        // whether the disk save succeeds or even runs.
        bool liftChanged = false;
        if (sourceManager is not null)
        {
            foreach (var s in conditions.Sources)
                if (s.Flange is int fl && sourceManager.GetSource(fl) is { } src && src.LiftPositionMm != s.LiftMm)
                {
                    src.LiftPositionMm = s.LiftMm;
                    liftChanged = true;
                }

            if (liftChanged && !string.IsNullOrEmpty(config.SourcesFile))
            {
                try { sourceManager.Save(config.SourcesFile); }
                catch { /* persistence is best-effort; the run snapshot still has the confirmed lift */ }
            }
        }

        _currentSnapshotPath = systemThread?.Core?.CreateDepositionSnapshot(conditions, basedOn);
        _runStartedUtc = DateTime.UtcNow;
        return true;
    }

    private void RunToolStripMenuItem_Click(object sender, EventArgs e)
    {
        // Defense in depth: the Run controls are also disabled for users without the
        // permission, but guard here too (this handler is shared and reachable via F5).
        if (!APS.SystemControl.Accounts.CurrentUser.Can(APS.Accounts.Domain.Permission.RunRecipe))
        {
            ThemedMessageBox.Show(this, "You are not authorized to run recipes.",
                "Not authorized", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrEmpty(recipeFileName))
        {
            ThemedMessageBox.Show(this, "No recipe selected. Please load a recipe first.",
                "No Recipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Time-quota gate: block a NEW run when the operator is out of time. An in-progress
        // run is never interrupted (metering is paused for its duration), so this only fires
        // before a run starts.
        if (APS.SystemControl.Accounts.CurrentUser.Sessions?.CanStartRun() == false)
        {
            ThemedMessageBox.Show(this, "Your time quota is exhausted. A new run cannot be started.",
                "No time remaining", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Safety gate: if any alarms/interlocks are currently overridden, force
        // the operator to acknowledge before starting a recipe. Bypassed safety
        // during deposition can damage hardware or contaminate the chamber.
        // Policy is in Core.Classes.RecipeRunGate so it can be unit-tested.
        int activeOverrides = systemThread?.Core?.Overrides?.ActiveCount ?? 0;
        if (APS.SystemControl.Core.Classes.RecipeRunGate.ShouldWarn(activeOverrides))
        {
            var choice = ThemedMessageBox.Show(
                this,
                APS.SystemControl.Core.Classes.RecipeRunGate.BuildWarningMessage(activeOverrides),
                "Active safety overrides",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes) return;
        }

        // Per-run signed deposition snapshot: the dialog reviews conditions, captures the
        // specimen, and L1-signs as the logged-in operator. The pending re-run lineage is
        // consumed take-and-clear at this gate: leaving it set across a cancelled/blocked
        // re-run stamps the stale fingerprint into the NEXT unrelated run's signed .depo.json
        // as false lineage (audit 2026-08-29, C32). A cancelled re-run is re-initiated from
        // the snapshot, which re-arms it.
        string? basedOn = _pendingBasedOn;
        _pendingBasedOn = null;
        if (!AuthorizeAndRecord(basedOn))
            return; // operator cancelled at the authorize gate

        // Pause quota metering for the run (operators are never billed for — or logged out
        // mid — a deposition) and stamp the verified operator on the run in the audit chain.
        APS.SystemControl.Accounts.CurrentUser.Sessions?.PauseQuota();
        Accounts.AccountsAuditBus.Write(new Core.Classes.AuditEvent
        {
            Event = "recipe.run.start",
            User = Accounts.CurrentUser.Name,
            Message = $"recipe={recipeFileName}",
        });

        // Carry both the recipe path and a SNAPSHOT of the edited parameters. The
        // copy isolates the in-flight run from a later recipe load mutating the field.
        SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Execute",
            [recipeFileName, new Dictionary<string, string>(recipeParameters, StringComparer.OrdinalIgnoreCase)]));
    }

    private void AbortToolStripButton_Click(object? sender, EventArgs e)
    {
        if (pool is null) return;
        SendOperatorCommand(new ValMsg<string>(MsgType.Command, DeviceType.Worker, "Abort", ""), CommandClass.Safety);
    }

    /// <summary>
    /// Open a past .depo.json, extract its embedded effective recipe to a temp file,
    /// show an advisory drift-check dialog, then hand off to the normal Run path.
    /// </summary>
    private void ReRunFromSnapshot_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Re-run from snapshot",
            Filter = "Deposition snapshots (*.depo.json)|*.depo.json",
            InitialDirectory = config.DataDir,
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var snap = APS.SystemControl.Core.Evidence.DepositionSnapshotReader.Load(ofd.FileName);

        // Capture existing state so we can restore it if the user cancels.
        var savedFileName = recipeFileName;
        var savedParams   = new Dictionary<string, string>(recipeParameters, StringComparer.OrdinalIgnoreCase);
        var savedLabel    = lblRecepieFileName.Text;

        // Load the embedded effective recipe as the program to run (params already baked).
        recipeFileName = APS.SystemControl.Core.Evidence.DepositionSnapshotReader.ExtractRecipeToTemp(snap);
        recipeParameters.Clear();
        lblRecepieFileName.Text = snap.Conditions.Recipe.Name + " (re-run)";

        // Drift-check captured vs current conditions — advisory only.
        var current = BuildConditions();
        var report = APS.SystemControl.Core.Evidence.DriftCheck.Compare(snap.Conditions, current);
        using (var dlg = new Forms.Evidence.DriftCheckDialog(report))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                recipeFileName          = savedFileName;
                recipeParameters        = savedParams;
                lblRecepieFileName.Text = savedLabel;
                return;
            }
        }

        // Link the new run's snapshot to the source, then go through the normal Run path.
        _pendingBasedOn = snap.Conditions.Fingerprint;
        RunToolStripMenuItem_Click(sender, e);
    }

    private void SerialConsoleToolStripMenuItem_Click(object sender, EventArgs e)
    {
        SerialPortConsoleForm consoleForm = new();
        consoleForm.Show();
    }

    private Forms.frmOverrides? _overridesForm;
    private Forms.FrmHwDiagnostics? _hwDiagForm;

    private void UpdateOverrideBanner()
    {
        if (IsDisposed || Disposing) return;   // late OverridesChanged during teardown
        if (InvokeRequired) { BeginInvoke(UpdateOverrideBanner); return; }
        var reg = systemThread?.Core?.Overrides;
        if (reg == null || reg.ActiveCount == 0)
        {
            overrideBanner.Visible = false;
            return;
        }
        overrideBanner.Text = $"⚠  {reg.ActiveCount} override(s) active — click to review";
        overrideBanner.Visible = true;
    }

    public void ShowOverrideFlash(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => ShowOverrideFlash(text)); return; }
        overrideToast.Text = $"⚠ OVERRIDE: {text}";
        overrideToast.Location = new Point(ClientSize.Width - overrideToast.Width - 16, 48);
        overrideToast.Visible = true;
        overrideToast.BringToFront();
        overrideToastTimer.Stop();
        overrideToastTimer.Start();
    }

    private void OverridesToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        // Defense in depth: the menu item is also disabled for users without the
        // permission, but guard here too in case it is reached via a shortcut.
        if (!APS.SystemControl.Accounts.CurrentUser.Can(APS.Accounts.Domain.Permission.SafetyOverride))
        {
            ThemedMessageBox.Show(this, "You are not authorized to manage safety overrides.",
                "Not authorized", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (systemThread?.Core?.Overrides == null)
        {
            ThemedMessageBox.Show(this, "Core is still initializing — try again in a moment.",
                "Not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_overridesForm != null && !_overridesForm.IsDisposed)
        {
            _overridesForm.Activate();
            return;
        }

        string path = Path.Combine(config.ConfigDir ?? ".", "RuntimeOverrides.xml");
        _overridesForm = new Forms.frmOverrides(systemThread.Core, path);
        _overridesForm.FormClosed += (_, _) => _overridesForm = null;
        _overridesForm.Show(this);
    }

    private void HwDiagnosticsToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        if (systemThread?.Core == null)
        {
            ThemedMessageBox.Show(this, "Core is still initializing — try again in a moment.",
                "Not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_hwDiagForm != null && !_hwDiagForm.IsDisposed) { _hwDiagForm.Activate(); return; }
        _hwDiagForm = new Forms.FrmHwDiagnostics(systemThread.Core);
        _hwDiagForm.FormClosed += (_, _) => _hwDiagForm = null;
        _hwDiagForm.Show(this);
    }

    private void IsolateToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        var result = ThemedMessageBox.Show(
            this,
            "Isolate System will close ALL valves and zero ALL MFCs.\n\n" +
            "This stops any active deposition and isolates the chamber.\n\nProceed?",
            "Confirm Isolate", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Worker, "Isolate", []), CommandClass.Safety);
    }

    private void SettingsToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        using var dlg = new Forms.frmAppSettings(config.Snapshot(), (logUser, logEmbedded) =>
        {
            var liveCore = systemThread?.Core;
            if (liveCore is not null)
            {
                liveCore.LogUserRecipes = logUser;
                liveCore.LogEmbeddedRecipes = logEmbedded;
            }
        });
        dlg.ShowDialog(this);
        // No action on close — frmAppSettings handles save + restart prompt internally.
    }

    private void btnEdit_Click(object sender, EventArgs e)
    {
        // Nothing selected — RichTextBox.LoadFile("") throws ArgumentException.
        if (string.IsNullOrEmpty(recipeFileName))
            return;
        // The editor is modeless and its Save button Close()s (disposes) it, so
        // reusing the field instance is use-after-dispose on the next Edit.
        // Recreate if disposed (mirrors the frmFindReplace M-14 pattern). (audit L-1)
        if (editorForm.IsDisposed)
            editorForm = new frmEditor();
        editorForm.LoadText(recipeFileName);
        editorForm.Show();
    }

    private sealed class ChamberTile
    {
        public required string ChamberId;
        public required ToolStripStatusLabel Name;
        public required ToolStripStatusLabel Dot;
        public required ToolStripStatusLabel State;
        public required ToolStripStatusLabel Elapsed;
        public DateTime ChangedAt;
        public SystemState Displayed;
        public bool DotBright;
    }

    private readonly Dictionary<string, ChamberTile> _tiles = new();
    private readonly System.Windows.Forms.Timer _stateElapsedTimer = new() { Interval = 1000 };

    private void InitStateIndicator()
    {
        // Build the chamber list: take Core's list, hide the orphan "Main" tile
        // when Alarms.xml declared other chambers but no Main of its own.
        var core = systemThread?.Core;
        if (core is null) return;

        var ids = new List<string>(core.ChamberIds);
        if (core.MainIsAutoFallback)
            ids.Remove(CoreConstants.DefaultChamberId);

        // Order: Main first if present, then alphabetical.
        ids.Sort((a, b) =>
        {
            if (a == CoreConstants.DefaultChamberId && b != CoreConstants.DefaultChamberId) return -1;
            if (b == CoreConstants.DefaultChamberId && a != CoreConstants.DefaultChamberId) return 1;
            return string.CompareOrdinal(a, b);
        });

        bool firstTile = true;
        foreach (var id in ids)
        {
            if (!firstTile)
                statusStrip1.Items.Add(new ToolStripSeparator());
            firstTile = false;

            var initial = core.GetChamberState(id);

            var name = new ToolStripStatusLabel(id)
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(119, 136, 153),
            };
            var dot = new ToolStripStatusLabel("●")
            {
                Font = new Font("Segoe UI", 10F),
                ForeColor = StateColor(initial),
            };
            var stateLbl = new ToolStripStatusLabel(initial.ToString().ToUpperInvariant())
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 214, 229),
            };
            var elapsed = new ToolStripStatusLabel("0m 00s")
            {
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(119, 136, 153),
            };

            statusStrip1.Items.Add(name);
            statusStrip1.Items.Add(dot);
            statusStrip1.Items.Add(stateLbl);
            statusStrip1.Items.Add(elapsed);

            _tiles[id] = new ChamberTile
            {
                ChamberId = id,
                Name = name,
                Dot = dot,
                State = stateLbl,
                Elapsed = elapsed,
                ChangedAt = DateTime.UtcNow,
                Displayed = initial,
                DotBright = true,
            };
        }

        _stateElapsedTimer.Tick += (_, _) =>
        {
            foreach (var t in _tiles.Values)
            {
                var elapsed = DateTime.UtcNow - t.ChangedAt;
                t.Elapsed.Text = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m"
                    : $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";

                if (t.Displayed == SystemState.Processing)
                {
                    t.DotBright = !t.DotBright;
                    t.Dot.ForeColor = t.DotBright
                        ? Color.FromArgb(255, 183, 77)
                        : Color.FromArgb(100, 72, 30);
                }
            }
        };
        components ??= new System.ComponentModel.Container();
        components.Add(_stateElapsedTimer);
        _stateElapsedTimer.Start();
    }

    private static Color StateColor(SystemState state) => state switch
    {
        SystemState.Startup     => Color.FromArgb(119, 136, 153),
        SystemState.Idle        => Color.FromArgb(72, 169, 254),
        SystemState.Maintenance => Color.FromArgb(178, 145, 47),  // vented, pumps running, service mode
        SystemState.Processing  => Color.FromArgb(255, 183, 77),
        SystemState.Stopped     => Color.FromArgb(239, 83, 80),
        _                       => Color.FromArgb(200, 214, 229),
    };

    private void UpdateChamberStateLabel(string chamberId, SystemState state)
    {
        if (!_tiles.TryGetValue(chamberId, out var t)) return; // suppressed orphan, etc.
        t.Displayed = state;
        t.ChangedAt = DateTime.UtcNow;
        t.DotBright = true;

        t.Dot.ForeColor = StateColor(state);
        t.State.Text = state.ToString().ToUpperInvariant();
        t.Elapsed.Text = "0m 00s";
    }

    // The app is always dark (forced in Program.Main). Kept for the vacuum-scheme
    // background fallbacks that still branch on dark/light.
    private static bool IsDarkMode() => APS.Controls.Theme.VacuumTheme.IsDark;
}
