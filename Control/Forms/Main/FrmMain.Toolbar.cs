using APS.Controls.Theme;
using APS.Icons;
using APS.SystemControl.Accounts;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Forms;
using APS.SystemControl.Hosting;

namespace APS.SystemControl;

public partial class FrmMain : Form
{
    private ToolStripMenuItem? sourcesToolStripMenuItem;
    private ToolStripMenuItem? reloadSourcesToolStripMenuItem;
    private ToolStripMenuItem? graphLogToolStripMenuItem;
    private ToolStripMenuItem? editLayoutToolStripMenuItem;
    private ToolStripMenuItem? hideInactiveSuppliesToolStripMenuItem;
    private ToolStripMenuItem? autoAlignOnHideToolStripMenuItem;
    private ToolStripMenuItem? alignPanelsNowToolStripMenuItem;
    private bool _syncingPsuToggles;
    private ToolStripButton? btnSources;
    private ToolStripButton? btnGraphLog;
    private ToolStripButton? btnEditLayout;
    private bool _syncingEditLayout;

    // Operator hold affordances (see UpdateHeldUi in frmMain.cs).
    // Visible only while Core.IsHeldForOperator: a held run has parked itself and handed the
    // machine back, and the fault dialog is gone, so without these nothing on screen would say
    // the run is still open or offer the way back into it.
    private ToolStripButton? btnResume;
    private ToolStripStatusLabel? lblHeld;

    private readonly SourceManager sourceManager = new(6);

    private void InitializeToolbarExtensions()
    {
        // Menu icons at 16 logical px. MenuStrip does NOT DPI-scale ImageScalingSize, so the
        // helper gets the scaled size (32 px at 200 %). Drop-down items do not resolve ImageKey
        // through the MenuStrip, hence the detached Images[...] copies below.
        var menuIcons = IconLibrary.ConfigureToolStrip(menuStrip1, DpiScale.Px(16),
            IconName.Sources, IconName.GraphLog, IconName.EditLayout,
            IconName.New, IconName.OpenFile, IconName.Save, IconName.Exit,
            IconName.Run, IconName.Load, IconName.Download, IconName.Pause, IconName.Stop,
            IconName.Configure, IconName.ClearList, IconName.HWTest, IconName.Reload,
            IconName.FunctionTest, IconName.SerialConsole, IconName.UserManual, IconName.About,
            IconName.Snapshot, IconName.Override, IconName.Isolate, IconName.Target,
            IconName.Hide, IconName.AutoAlign, IconName.Grid);
        // Toolbar buttons use ImageKey against the single list the frmMain ctor gave toolStrip1.

        var toolsSeparator = new ToolStripSeparator();
        toolsToolStripMenuItem.DropDownItems.Add(toolsSeparator);

        sourcesToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "sourcesToolStripMenuItem",
            Text = "Sources",
            Image = menuIcons.Images["sources"],
            ToolTipText = "View and edit deposition sources"
        };
        sourcesToolStripMenuItem.Click += SourcesToolStripMenuItem_Click;
        toolsToolStripMenuItem.DropDownItems.Add(sourcesToolStripMenuItem);

        reloadSourcesToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "reloadSourcesToolStripMenuItem",
            Text = "Reload Sources",
            Image = menuIcons.Images["reload"],
            ToolTipText = "Re-read Sources.xml from disk (no app restart needed)"
        };
        reloadSourcesToolStripMenuItem.Click += (_, _) => ReloadSources();
        toolsToolStripMenuItem.DropDownItems.Add(reloadSourcesToolStripMenuItem);

        graphLogToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "graphLogToolStripMenuItem",
            Text = "Graph Log Viewer",
            Image = menuIcons.Images["graphlog"],
            ToolTipText = "Open recorded data log viewer"
        };
        graphLogToolStripMenuItem.Click += GraphLogToolStripMenuItem_Click;
        toolsToolStripMenuItem.DropDownItems.Add(graphLogToolStripMenuItem);

        // During a deposition, Sources stays openable (it opens read-only so the
        // operator can watch the live erosion read-outs); only Reload Sources —
        // which mutates the live config from disk — is grayed out.
        toolsToolStripMenuItem.DropDownOpening += (_, _) =>
        {
            bool active = systemThread?.Core?.IsDepositionActive ?? false;
            sourcesToolStripMenuItem!.Enabled = true;
            reloadSourcesToolStripMenuItem!.Enabled = !active;
        };

        editLayoutToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "editLayoutToolStripMenuItem",
            Text = "Edit Panel Layout",
            Image = menuIcons.Images["editlayout"],
            CheckOnClick = true,
            ToolTipText = "Toggle drag-and-drop panel repositioning"
        };
        editLayoutToolStripMenuItem.CheckedChanged += EditLayout_CheckedChanged;
        toolsToolStripMenuItem.DropDownItems.Add(editLayoutToolStripMenuItem);

        hideInactiveSuppliesToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "hideInactiveSuppliesToolStripMenuItem",
            Text = "Hide Inactive Supplies",
            Image = menuIcons.Images["hide"],
            CheckOnClick = true,
            Checked = true,
            ToolTipText = "Hide magnetron power supplies not wired to an active source"
        };
        hideInactiveSuppliesToolStripMenuItem.CheckedChanged += HideInactiveSupplies_CheckedChanged;
        toolsToolStripMenuItem.DropDownItems.Add(hideInactiveSuppliesToolStripMenuItem);

        autoAlignOnHideToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "autoAlignOnHideToolStripMenuItem",
            Text = "Auto-align on Hide",
            Image = menuIcons.Images["autoalign"],
            CheckOnClick = true,
            Checked = true,
            ToolTipText = "Close gaps by compacting panel columns after a supply is hidden"
        };
        autoAlignOnHideToolStripMenuItem.CheckedChanged += AutoAlignOnHide_CheckedChanged;
        toolsToolStripMenuItem.DropDownItems.Add(autoAlignOnHideToolStripMenuItem);

        alignPanelsNowToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "alignPanelsNowToolStripMenuItem",
            Text = "Align Panels Now",
            Image = menuIcons.Images["grid"],
            ToolTipText = "Compact System-tab panel columns now"
        };
        alignPanelsNowToolStripMenuItem.Click += (_, _) => _panelDragManager?.CompactColumns(persist: true);
        toolsToolStripMenuItem.DropDownItems.Add(alignPanelsNowToolStripMenuItem);

        var toolbarSeparator = new ToolStripSeparator();
        toolStrip1.Items.Add(toolbarSeparator);

        btnSources = new ToolStripButton
        {
            Name = "btnSources",
            Text = "Sources",
            ImageKey = IconName.Sources,
            ToolTipText = "View and edit deposition sources"
        };
        btnSources.Click += SourcesToolStripMenuItem_Click;
        toolStrip1.Items.Add(btnSources);

        btnGraphLog = new ToolStripButton
        {
            Name = "btnGraphLog",
            Text = "Graph Log",
            ImageKey = IconName.GraphLog,
            ToolTipText = "Open recorded data log viewer"
        };
        btnGraphLog.Click += GraphLogToolStripMenuItem_Click;
        toolStrip1.Items.Add(btnGraphLog);

        var toolbarSeparator2 = new ToolStripSeparator();
        toolStrip1.Items.Add(toolbarSeparator2);

        btnEditLayout = new ToolStripButton
        {
            Name = "btnEditLayout",
            Text = "Edit Layout",
            ImageKey = IconName.EditLayout,
            CheckOnClick = true,
            ToolTipText = "Toggle drag-and-drop panel repositioning"
        };
        btnEditLayout.CheckedChanged += EditLayout_CheckedChanged;
        toolStrip1.Items.Add(btnEditLayout);

        AssignMenuIcons(menuIcons);
        InitializeManualLoggingUi();
    }

    private void AssignMenuIcons(ImageList icons)
    {
        newRecepieToolStripMenuItem.Image = icons.Images["new"];
        openRecipieToolStripMenuItem.Image = icons.Images["openfile"];
        saveRecepieToolStripMenuItem.Image = icons.Images["save"];
        exitToolStripMenuItem.Image = icons.Images["exit"];

        runToolStripMenuItem.Image = icons.Images["run"];
        reRunFromSnapshotToolStripMenuItem.Image = icons.Images["snapshot"];

        overridesToolStripMenuItem.Image = icons.Images["override"];
        isolateToolStripMenuItem.Image = icons.Images["isolate"];
        settingsToolStripMenuItem.Image = icons.Images["configure"];
        hwDiagnosticsToolStripMenuItem.Image = icons.Images["hwtest"];
        reloadGuiConfigToolStripMenuItem.Image = icons.Images["reload"];

        serialConsoleToolStripMenuItem.Image = icons.Images["serialconsole"];

        userManualToolStripMenuItem.Image = icons.Images["usermanual"];
        aboutToolStripMenuItem.Image = icons.Images["about"];
    }

    private void SourcesToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        // During a deposition the form opens read-only: edits are locked but the
        // live read-outs (groove / utilization / burn-through ETA) stay visible.
        bool readOnly = systemThread?.Core?.IsDepositionActive ?? false;
        using var form = new frmSources(sourceManager, ControlApp.Edition.Find<ISourceMountExtension>(), systemThread?.Core, config.SourcesFile,
            onSourcesCommitted: ReloadSources, readOnly: readOnly);
        if (form.ShowDialog(this) == DialogResult.OK)
            ReloadSources();
    }

    /// <summary>
    /// Re-reads <c>Sources.xml</c> from disk into the running <see cref="SourceManager"/>
    /// so operators can edit the file externally (or copy a new revision from Builder)
    /// and pick it up without restarting the app. Also refreshes the Worker._sourceMap and
    /// pushes the new flange→material map to the live process panel. No-ops with a warning
    /// dialog if a recipe is running.
    /// </summary>
    private void ReloadSources()
    {
        if (systemThread?.Core?.IsDepositionActive == true)
        {
            ThemedMessageBox.Show(this,
                "Cannot reload sources while a deposition is in progress.",
                "Reload Sources", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            var map = systemThread?.Core?.ReloadSources(sourceManager, config.SourcesFile)
                      ?? new Dictionary<int, APS.ProcessPanel.Contracts.FlangeSourceInfo>();
            var pp = FindProcessPanel();
            if (pp != null && map.Count > 0)
                ExecuteOnUIThread(() => pp.UpdateSources(map));
            // Intentionally outside the pp/map guard above: ApplyPsuPanelVisibility re-reads
            // Sources.xml itself and must refresh the System-tab PSU panels (and the cleared-
            // sources case where map is empty) even when the process panel isn't present.
            ApplyPsuPanelVisibility();
            RunModules(nameof(IControlModule.OnSourcesReloaded), m => m.OnSourcesReloaded(this));
            RebuildPsuMagnetronGlowMap();
            DisplayLogMessage("Core", $"Sources reloaded from {config.SourcesFile}");
        }
        catch (Exception ex)
        {
            DisplayLogMessage("Core", $"Sources reload failed: {ex.Message}");
        }
    }

    private void ReloadGuiConfigToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        ReloadGuiConfig();
    }

    /// <summary>
    /// Rebuilds the XML-driven GUI (vacuum schematic + widgets from Controls.xml,
    /// custom panels from Panels.xml, and the Hardware menu) without restarting.
    /// Each subsystem is torn down first so nothing duplicates. Blocked during an
    /// active deposition, mirroring ReloadSources.
    /// </summary>
    private void ReloadGuiConfig()
    {
        if (systemThread?.Core?.IsDepositionActive == true)
        {
            ThemedMessageBox.Show(this,
                "Cannot reload GUI configuration while a deposition is in progress.",
                "Reload GUI Config", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            // Teardown: drop live controls AND every form-level handle into them.
            vacuumSchematic.ClearComponents();   // valves/MFCs/sensors/etc. (also detaches the events overlay)
            ClearDynamicPanels();                // custom panels + drag manager (now disposed)
            tabProcess.Controls.Clear();

            // Invalidate cached handles into the torn-down tree so live status
            // re-resolves to the rebuilt widgets instead of mutating disposed ghosts.
            controlCache.Clear();
            _valveCache.Clear();
            _driveCache.Clear();
            _powerLed = null;
            _onlineLed = null;
            lock (_processPanelLock)
            {
                _processPanel = null;
                _processPanelSearched = false;
            }
            _clusterPanel = null;
            _clusterPanelSearched = false;
            _telemetryTimer?.Stop();
            _telemetryTimer?.Dispose();
            _telemetryTimer = null;
            _telemetryCollector = null;

            // Rebuild in FrmMain_Load GUI order.
            LoadVacuumComponentsXML(config.GUIVacuumConfigFile);
            LoadCustomPanelsXML(config.GUIPanelsConfigFile);
            ComposeHardwareMenus();

            // Re-attach the System-events overlay on top of the rebuilt schematic
            // (ClearComponents only detached it; the controls persist as fields).
            if (_systemEventsLog != null) { vacuumSchematic.Controls.Add(_systemEventsLog); _systemEventsLog.BringToFront(); }
            if (_systemEventsChip != null) { vacuumSchematic.Controls.Add(_systemEventsChip); _systemEventsChip.BringToFront(); }

            // Splitters created once in FrmMain_Load are not torn down here, so
            // they are intentionally NOT re-created (SideSplitterFactory.Install
            // appends a new splitter each call — re-running it would duplicate them).
            ApplySystemTabLayout();
            ApplyPsuPanelVisibility();
            RunModules(nameof(IControlModule.OnSourcesReloaded), m => m.OnSourcesReloaded(this));

            DisplayLogMessage("GUI", "GUI configuration reloaded");
        }
        catch (Exception ex)
        {
            DisplayLogMessage("GUI", $"GUI reload failed: {ex.Message}");
        }
    }

    private void GraphLogToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        var form = new frmGraphLog();
        form.Show();
    }

    private void EditLayout_CheckedChanged(object? sender, EventArgs e)
    {
        if (_syncingEditLayout) return;
        _syncingEditLayout = true;

        bool isChecked = sender switch
        {
            ToolStripButton btn => btn.Checked,
            ToolStripMenuItem item => item.Checked,
            _ => false
        };

        if (btnEditLayout != null) btnEditLayout.Checked = isChecked;
        if (editLayoutToolStripMenuItem != null) editLayoutToolStripMenuItem.Checked = isChecked;

        if (isChecked)
            _panelDragManager?.EnterEditMode();
        else
            _panelDragManager?.ExitEditMode();

        _syncingEditLayout = false;
    }

    private void HideInactiveSupplies_CheckedChanged(object? sender, EventArgs e)
    {
        if (_syncingPsuToggles) return;
        _userLayout.HideInactiveSupplies = hideInactiveSuppliesToolStripMenuItem!.Checked;
        _userLayout.Save();
        ApplyPsuPanelVisibility();
    }

    private void AutoAlignOnHide_CheckedChanged(object? sender, EventArgs e)
    {
        if (_syncingPsuToggles) return;
        _userLayout.AutoAlignOnHide = autoAlignOnHideToolStripMenuItem!.Checked;
        _userLayout.Save();
        if (autoAlignOnHideToolStripMenuItem.Checked)
            _panelDragManager?.CompactColumns(persist: true);   // tidy immediately when enabled
    }

    /// <summary>
    /// Sync the two PSU toggles from persisted layout, then apply visibility once.
    /// Called from FrmMain_Load after the menu items and _userLayout both exist.
    /// Compaction is deferred to the form's Shown event (valid tab size).
    /// </summary>
    private void InitPsuPanelVisibility()
    {
        _syncingPsuToggles = true;
        if (hideInactiveSuppliesToolStripMenuItem != null)
            hideInactiveSuppliesToolStripMenuItem.Checked = _userLayout.HideInactiveSupplies;
        if (autoAlignOnHideToolStripMenuItem != null)
            autoAlignOnHideToolStripMenuItem.Checked = _userLayout.AutoAlignOnHide;
        _syncingPsuToggles = false;

        ApplyPsuPanelVisibility();                 // visibility now (compaction self-guards on size)
        Shown += PsuPanelVisibility_ShownOnce;     // first compaction once the tab has its real size
    }

    private void PsuPanelVisibility_ShownOnce(object? sender, EventArgs e)
    {
        Shown -= PsuPanelVisibility_ShownOnce;
        ApplyPsuPanelVisibility();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _panelDragManager != null && _panelDragManager.IsEditMode)
        {
            _panelDragManager.CancelEditMode();
            _syncingEditLayout = true;
            if (btnEditLayout != null) btnEditLayout.Checked = false;
            if (editLayoutToolStripMenuItem != null) editLayoutToolStripMenuItem.Checked = false;
            _syncingEditLayout = false;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
