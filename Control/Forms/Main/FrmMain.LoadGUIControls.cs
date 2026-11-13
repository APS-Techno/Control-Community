using System.Reflection;
using APS.Controls.General;
using APS.Controls.Theme;
using APS.Controls.XML.GUI;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Core;
using APS.SystemControl.Forms;
using APS.SystemControl.GUIControls.General;
using APS.SystemControl.XML;
using APS.SystemControl.XML.GUI;

namespace APS.SystemControl;

public partial class FrmMain
{
    private PanelDragManager? _panelDragManager;

    // Panels added at runtime by LoadPanel, paired with the parent they were
    // added to, so ReloadGuiConfig can remove exactly these (not the static
    // designer controls / side panels) before rebuilding.
    private readonly List<(Control Parent, Control Panel)> _dynamicPanels = [];

    private void LoadPanel(XML_Panel xml_panel, PanelDragManager dragManager)
    {
        // Feature gate: loading a non-stock panel DLL requires the custom-process-panel flag.
        // The generic panel library and any library the edition bundles are always allowed.
        if (!string.Equals(xml_panel.Lib, "APS.Controls.Generic.dll", StringComparison.OrdinalIgnoreCase)
            && !APS.SystemControl.Hosting.ControlApp.Edition.Features.IsBundledLibrary(xml_panel.Lib)
            && !APS.SystemControl.Hosting.ControlApp.Edition.Features.EnsureOrShow(APS.SystemControl.Hosting.EditionFeatureNames.CustomProcessPanel, this))
        {
            return;
        }

        Assembly externalControlsLib = Assembly.LoadFrom(Path.Combine(config.GUIDllBasePath, xml_panel.Lib));
        Type externalPanelType = externalControlsLib.GetType(xml_panel.Type);
        if (externalPanelType != null)
        {
            object extrenalPanelInstance = Activator.CreateInstance(externalPanelType);
            if (extrenalPanelInstance is Control externalPanel)
            {
                (externalPanel as IBaseCustomPanel)!.ID = xml_panel.ID!;
                (externalPanel as IBaseCustomPanel)!.Caption = xml_panel.Caption ?? "";

                // Recipe-driven panels build their UI from the live chamber + embedded-recipe
                // set. Core is fully loaded by now (RunThreads ran in the ctor, before
                // FrmMain_Load -> LoadCustomPanelsXML).
                if (externalPanel is IRecipeOpsPanel rop && systemThread?.Core is { } core)
                {
                    var chambers = core.ChamberIds
                        .Where(id => !(core.MainIsAutoFallback && id == CoreConstants.DefaultChamberId))
                        .ToArray();
                    rop.ConfigureOps(chambers, core.embRecipes?.GetKnownNames() ?? Enumerable.Empty<string>());
                }

                // Hardware-aware panels (e.g. StepperDrivePanel's startup drive-position
                // check) get a read-only registry view onto the connected devices.
                if (externalPanel is IHardwareAwarePanel hwAware && systemThread?.Core is { } hwCore)
                {
                    hwAware.OnHardwareAvailable(
                        new APS.SystemControl.Core.Menus.Services.HardwareRegistryAdapter(hwCore));
                }

                Control? parent = FindControlByName(xml_panel.Parent);

                if (parent != null)
                {
                    parent.Controls.Add(externalPanel);
                    parent.Controls.SetChildIndex(externalPanel, 0);
                    _dynamicPanels.Add((parent, externalPanel));
                    DarkModeHelper.FixControls(externalPanel);

                    // Per-panel background from config. Applied AFTER FixControls
                    // (which only recolors panels still at SystemColors.Control, so
                    // it never touches this) — ambient color inheritance cascades
                    // the new BackColor to the body TableLayoutPanel + transparent
                    // labels, while interactive widgets keep their own colors.
                    if (!string.IsNullOrWhiteSpace(xml_panel.Color)
                        && TryParseHtmlColor(xml_panel.Color, out var panelBg))
                    {
                        // Capture the theme background the body blended into BEFORE
                        // recoloring, so we can re-point opaque children that painted
                        // themselves with it (see RetintBlendedChildren).
                        Color blended = externalPanel.BackColor;
                        externalPanel.BackColor = panelBg;
                        RetintBlendedChildren(externalPanel, blended, panelBg);
                    }

                    // Optional per-panel chrome overrides. Header/border live on
                    // the ScalablePanel base; button-face is applied here (the
                    // base can't see LedButton across the dependency boundary).
                    if (externalPanel is ScalablePanel sp)
                    {
                        if (!string.IsNullOrWhiteSpace(xml_panel.HeaderColor)
                            && TryParseHtmlColor(xml_panel.HeaderColor, out var headerColor))
                            sp.HeaderColor = headerColor;

                        if (!string.IsNullOrWhiteSpace(xml_panel.BorderColor)
                            && TryParseHtmlColor(xml_panel.BorderColor, out var borderColor))
                            sp.BorderColor = borderColor;
                    }

                    if (!string.IsNullOrWhiteSpace(xml_panel.ButtonColor)
                        && TryParseHtmlColor(xml_panel.ButtonColor, out var buttonColor))
                        ApplyButtonFaceColor(externalPanel, buttonColor);

                    // Per-system drive-icon sweep speeds (seconds per adjacent
                    // flange): animFlangeSeconds seeds both drives, then the
                    // per-drive elements override — the motors run at different
                    // velocities.
                    if (externalPanel is IAnimatedSchematicPanel animPanel)
                    {
                        if (xml_panel.AnimFlangeSeconds > 0)
                        {
                            animPanel.HolderFlangeSeconds = xml_panel.AnimFlangeSeconds;
                            animPanel.ShutterFlangeSeconds = xml_panel.AnimFlangeSeconds;
                        }
                        if (xml_panel.AnimHolderSeconds > 0)
                            animPanel.HolderFlangeSeconds = xml_panel.AnimHolderSeconds;
                        if (xml_panel.AnimShutterSeconds > 0)
                            animPanel.ShutterFlangeSeconds = xml_panel.AnimShutterSeconds;
                    }

                    UIScaling.ApplyToLoadedPanel(externalPanel);
                    externalPanel.Location = PanelDragManager.PlaceInside(
                        xml_panel.Left, xml_panel.Top, externalPanel.Size, parent.ClientSize);
                    dragManager.Register(externalPanel, xml_panel);

                    EventInfo externalCommandEvent = externalPanelType.GetEvent(xml_panel.Event);
                    if (externalCommandEvent != null)
                    {
                        MethodInfo localEventHandler;

                        if (xml_panel.MsgHandler != "")
                            localEventHandler = typeof(FrmMain).GetMethod(xml_panel.MsgHandler);
                        else
                            localEventHandler = typeof(FrmMain).GetMethod(nameof(ProcessCustomPanelEvent));

                        Delegate handler = Delegate.CreateDelegate(externalCommandEvent.EventHandlerType, this, localEventHandler);
                        externalCommandEvent.AddEventHandler(extrenalPanelInstance, handler);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses an HTML/CSS hex color (e.g. "#16323F") from a panel's config.
    /// A malformed value is swallowed so a bad config entry leaves the panel at
    /// its default color instead of crashing panel loading.
    /// </summary>
    private static bool TryParseHtmlColor(string value, out Color color)
    {
        try { color = ColorTranslator.FromHtml(value.Trim()); return true; }
        catch { color = Color.Empty; return false; }
    }

    /// <summary>
    /// After a panel's background is retinted from config, opaque children that
    /// owner-paint themselves with the old theme background (e.g. DarkCheckBox /
    /// DarkRadioButton fill their client rect with BackColor, and any label/
    /// sub-panel left at the theme shade) show up as mismatched rectangles on the
    /// new tint. Re-point every descendant still carrying <paramref name="from"/>
    /// to <paramref name="to"/> so they blend, while leaving interactive chrome
    /// (buttons, text/number inputs, combos, lists, grids, bars) on their own
    /// colors. Ambient children already report the new color and are skipped.
    /// </summary>
    private static void RetintBlendedChildren(Control parent, Color from, Color to)
    {
        foreach (Control c in parent.Controls)
        {
            bool isChrome =
                (c is ButtonBase and not CheckBox and not RadioButton)
                || c is TextBoxBase or NumericUpDown or ComboBox or ListControl
                   or DataGridView or ProgressBar or TrackBar;

            if (!isChrome && c.BackColor.ToArgb() == from.ToArgb())
                c.BackColor = to;

            RetintBlendedChildren(c, from, to);
        }
    }

    /// <summary>
    /// Applies a per-panel button-face color to every <see cref="LedButton"/> in
    /// the panel's control tree. Set from the panel's <c>&lt;buttonColor&gt;</c>
    /// config so only this panel's buttons change; other panels keep the global
    /// theme face color.
    /// </summary>
    private static void ApplyButtonFaceColor(Control parent, Color face)
    {
        foreach (Control c in parent.Controls)
        {
            // Skip buttons that already carry a hardcoded per-button face
            // (e.g. throttle Open/Close/Stop) — those win over the panel-wide color.
            if (c is LedButton lb && lb.FaceColor == null)
            {
                lb.FaceColor = face;
                lb.Invalidate();
            }
            ApplyButtonFaceColor(c, face);
        }
    }

    private void LoadCustomPanelsXML(string fileName)
    {
        try
        {
            var xml_panels = Serializer<XML_Panels>.Load(fileName);
            if (xml_panels != null && xml_panels.Panels != null)
            {
                Control? dragParent = FindControlByName("tabSystem");
                if (dragParent == null) return;
                _panelDragManager = new PanelDragManager(dragParent, xml_panels, fileName);

                var processSpec = APS.SystemControl.GUIControls.Process.ProcessPanelSpec.Load(config.ProcessPanelConfigFile);
                APS.SystemControl.GUIControls.Process.ProcessPanelSpec.Current = processSpec;
                foreach (var specError in processSpec.Errors)
                    PublishEvent("ProcessPanel", specError, APS.SystemControl.Core.Logging.LogEventLevel.Operator);

                foreach (XML_Panel xml_panel in xml_panels.Panels)
                {
                    LoadPanel(xml_panel, _panelDragManager);
                }
                // A reload while the chamber is Processing must not hand back enabled buttons.
                if (systemThread?.Core is { } core)
                    UpdateEmbeddedRecipeButtons(core.SystemState);
            }
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, $"Error loading external GUI Controls: {ex.Message}");
        }
    }

    /// <summary>
    /// Greys out every panel button that starts an embedded recipe the profile forbids in
    /// <paramref name="state"/> (notIn="…" in EmbedRecipes.xml) and re-enables the rest.
    /// Buttons are matched by Tag, which every operations panel sets to the recipe id, so
    /// no plugin API is involved. The Core refuses such a launch regardless; this only
    /// spares the operator confirming something that will be refused. UI thread.
    /// </summary>
    private void UpdateEmbeddedRecipeButtons(SystemState state)
    {
        var recipes = systemThread?.Core?.embRecipes;
        if (recipes is null) return;
        foreach (var (_, panel) in _dynamicPanels)
            foreach (var btn in Descendants(panel).OfType<Button>())
                if (btn.Tag is string id && recipes.Contains(id))
                    btn.Enabled = !recipes.IsBlockedIn(id, state);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
                yield return grandchild;
        }
    }

    /// <summary>
    /// Removes and disposes every panel LoadPanel added at runtime, then
    /// nulls the drag manager. Called by ReloadGuiConfig before re-running
    /// LoadCustomPanelsXML so panels are not duplicated.
    /// </summary>
    private void ClearDynamicPanels()
    {
        foreach (var (parent, panel) in _dynamicPanels)
        {
            parent.Controls.Remove(panel);
            panel.Dispose();
        }
        _dynamicPanels.Clear();
        _panelDragManager?.Dispose();
        _panelDragManager = null;
    }

    public void ProcessCustomPanelEvent(object? sender, CustomEventArgs e)
    {
        if (sender is IBaseCustomPanel panel)
        {
            SendOperatorCommand(new ParamMsg(MsgType.Command, panel.DeviceType, panel.ID, e.Data));
        }
    }

    /// <summary>
    /// Handles stepper drive panel events. The panel includes the target drive ID
    /// in args[0] since one panel controls both Holder and Shutter.
    /// </summary>
    public void ProcessStepperPanelEvent(object? sender, CustomEventArgs e)
    {
        if (e.Data.Length < 2) return;
        string driveId = (string)e.Data[0];
        object[] commandArgs = e.Data[1..];
        // Stop is the drive panel's safety action; everything else is an ordinary operator command.
        SendOperatorCommand(new ParamMsg(MsgType.Command, DeviceType.Drive, driveId, commandArgs),
            commandArgs is [string c, ..] && c.Equals("Stop", StringComparison.OrdinalIgnoreCase)
                ? CommandClass.Safety : CommandClass.Mutate);
    }

    public void EmbedRecipeButtonClick(object sender, CustomEventArgs e)
    {
        object[] data = e.Data;
        if (data.Length > 0)
        {
            // Panel buttons start machine-level sequences (vent, shutdown, transfer) on a
            // single click, so the operator confirms first. Only this UI path asks — remote
            // hosts, the Stream Deck and nested 'call' must never block on a modal dialog.
            string id = (string)data[0];
            string prompt = systemThread?.Core?.embRecipes?.ConfirmPrompt(id) ?? $"Start {id}?";
            if (frmMessage.Confirm(this, prompt, id, defaultNo: true) != DialogResult.Yes)
                return;
            SendOperatorCommand(new ThreadMsg(MsgType.Command, DeviceType.Recipe, id));
        }
    }

    /// <summary>
    /// Composes contributed menus from loaded HW/GUI assemblies and Menus.xml
    /// into the existing menuStrip1. Called from FrmMain_Load after panels
    /// are loaded so panel-instance contributors are visible to the scan.
    /// </summary>
    private void ComposeHardwareMenus()
    {
        if (pool is null || systemThread?.Core is null) return;

        // Drop any previously-composed menu items so a reload does not leak
        // duplicate Hardware entries (each composer only tracks its own items).
        _menuComposer?.Dispose();
        _menuComposer = null;

        var ctx = new APS.SystemControl.Core.Menus.MenuContext
        {
            Hw = new APS.SystemControl.Core.Menus.Services.HwCommandSinkAdapter(pool, OperatorAuthorize, LogControlMode),
            Hardware = new APS.SystemControl.Core.Menus.Services.HardwareRegistryAdapter(systemThread.Core),
            Ui = new APS.SystemControl.Core.Menus.Services.UiServicesAdapter(this),
            Flags = APS.SystemControl.Hosting.ControlApp.Edition.Features,
            Runtime = new APS.SystemControl.Core.Menus.Services.RuntimeStateAdapter(systemThread.Core),
            Auth = new APS.SystemControl.Accounts.AuthorizationAdapter(),
            Owner = this,
        };

        var composer = new APS.SystemControl.Core.Menus.MenuComposer(menuStrip1, ctx,
            dynamicRoot: "Hardware",
            logWarning: warning => DisplayLogMessage("Menus", warning));

        // Source 1: Loaded panels (each Control on tabSystem/tabProcess that
        // also implements IMenuContributor via the existing IBaseCustomPanel
        // path). Walk the children of the panels' parent containers.
        var panelContributors = APS.SystemControl.Core.Menus.ContributorDiscovery
            .DiscoverFromInstances(EnumerateLoadedPanels());

        // Source 2: Reflection scan over loaded HW + GUI lib assemblies. We
        // include every loaded non-dynamic assembly that ISN'T a known system/
        // framework/test assembly — any APS HW or GUI library gets picked up
        // automatically without having to whitelist by name. ContributorDiscovery
        // already silently ignores types without [MenuContributor], so the cost
        // of scanning a non-contributing assembly is just one GetTypes() call.
        var assemblyScan = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !IsFrameworkAssembly(a.GetName().Name));

        var xmlContributors = APS.SystemControl.Core.Menus.XmlMenuLoader.Load(config.MenusConfigFile);

        _menuComposer = composer;
        composer.Compose(panelContributors.Concat(xmlContributors), assemblyScan);
    }

    /// <summary>
    /// Returns true for assemblies whose names indicate they are framework,
    /// system, or test infrastructure rather than application code that might
    /// contribute menu items. Conservative list — extend if real APS code ever
    /// gets a name that matches one of these prefixes.
    /// </summary>
    private static bool IsFrameworkAssembly(string? name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        string[] frameworkPrefixes = [
            "System.", "Microsoft.", "mscorlib", "netstandard", "WindowsBase",
            "PresentationCore", "PresentationFramework", "WindowsFormsIntegration",
            "Accessibility",
        ];
        string[] thirdPartyPrefixes = [
            "xunit", "Newtonsoft.", "ScottPlot", "Sharp7",
            "Castle.", "Moq", "Microsoft.TestPlatform",
        ];
        foreach (var p in frameworkPrefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var p in thirdPartyPrefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("anonymously hosted", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private IEnumerable<object> EnumerateLoadedPanels()
    {
        IEnumerable<Control> Walk(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (var d in Walk(child)) yield return d;
        }

        if (tabSystem != null)
            foreach (var c in Walk(tabSystem)) yield return c;
        if (tabProcess != null)
            foreach (var c in Walk(tabProcess)) yield return c;
    }
}
