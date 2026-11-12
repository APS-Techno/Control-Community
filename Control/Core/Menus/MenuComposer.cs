using System.Reflection;
using APS.Icons;
using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus;

/// <summary>
/// Orchestration: discovery → tree → ToolStripMenuItem composition into a
/// host MenuStrip. Subscribes to each contributor's ItemsChanged so the
/// affected subtree is rebuilt on hardware-state change.
///
/// All contributions land under a single host top-level item whose caption
/// is supplied via the constructor's <c>dynamicRoot</c> parameter (default
/// "Hardware"). Each contributor's <c>Path</c> is therefore interpreted
/// relative to that root — the first path segment is a category under
/// the dynamic root, not a top-level menu name.
/// </summary>
internal sealed class MenuComposer : IDisposable
{
    private readonly MenuStrip _host;
    private readonly IMenuContext _ctx;
    private readonly Action<string> _logWarning;
    private readonly string _dynamicRoot;
    private readonly List<ToolStripItem> _ownedItems = [];
    private readonly List<IMenuContributor> _contributors = [];
    private System.Windows.Forms.ImageList? _menuIcons;
    private ToolStripMenuItem? _resolvedRoot;
    // Items with DynamicCaption — refreshed whenever their parent dropdown opens.
    private readonly List<(ToolStripMenuItem Item, MenuItemSpec Spec)> _dynamicCaptionItems = [];
    private readonly HashSet<ToolStripDropDownItem> _subscribedParents = [];

    public MenuComposer(MenuStrip host, IMenuContext ctx, string dynamicRoot = "Hardware", Action<string>? logWarning = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(dynamicRoot);
        _host = host;
        _ctx = ctx;
        _dynamicRoot = dynamicRoot;
        _logWarning = logWarning ?? (_ => { });
    }

    public void Compose(
        IEnumerable<IMenuContributor> contributors,
        IEnumerable<Assembly> assembliesToScan)
    {
        foreach (var c in contributors)
        {
            _contributors.Add(c);
            c.ItemsChanged += OnContributorChanged;
        }
        foreach (var c in ContributorDiscovery.DiscoverFromAssemblies(assembliesToScan, _logWarning))
        {
            _contributors.Add(c);
            c.ItemsChanged += OnContributorChanged;
        }
        Rebuild();
    }

    private void OnContributorChanged(object? sender, EventArgs e)
    {
        if (_host.InvokeRequired) _host.BeginInvoke(Rebuild);
        else Rebuild();
    }

    /// <summary>Re-evaluate all contributed items (e.g. after a switch-user changes
    /// the current user's permissions). Marshals to the UI thread if needed.</summary>
    public void Refresh()
    {
        if (_host.InvokeRequired) _host.BeginInvoke(Rebuild);
        else Rebuild();
    }

    private void Rebuild()
    {
        TearDownOwnedItems();

        var specs = new List<MenuItemSpec>();
        foreach (var c in _contributors)
        {
            try { specs.AddRange(c.GetMenuItems(_ctx)); }
            catch (Exception ex) { _logWarning($"Contributor {c.GetType().Name} threw: {ex.Message}"); }
        }

        var (root, warnings) = MenuTreeBuilder.Build(specs);
        foreach (var w in warnings) _logWarning(w);

        // Build one ImageList for the entire run, so item.Image references
        // stay valid for the menu's lifetime.
        var iconNames = specs
            .Where(s => !string.IsNullOrEmpty(s.IconName))
            .Select(s => s.IconName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (iconNames.Length > 0)
        {
            // 16 logical px, DPI-scaled to match menuStrip1.ImageScalingSize (see FrmMain.Toolbar.cs).
            try { _menuIcons = APS.Icons.IconLibrary.BuildImageList(APS.Controls.Theme.DpiScale.Px(16), iconNames); }
            catch { _menuIcons = null; }
        }

        ToolStripMenuItem hostItem = ResolveDynamicRoot();
        ComposeChildren(hostItem.DropDownItems, root.Children);
    }

    /// <summary>
    /// Removes and disposes every item this composer created, clears caption /
    /// parent tracking, and disposes the shared icon ImageList. Used both at the
    /// start of each Rebuild and by Dispose. Does NOT unsubscribe contributors —
    /// Rebuild reuses them; Dispose handles that separately.
    /// </summary>
    private void TearDownOwnedItems()
    {
        foreach (var item in _ownedItems)
        {
            if (item.Owner is ToolStripDropDown dd) dd.Items.Remove(item);
            else _host.Items.Remove(item);
            item.Dispose();
        }
        _ownedItems.Clear();
        _dynamicCaptionItems.Clear();
        foreach (var p in _subscribedParents)
            p.DropDownOpening -= OnDropDownOpening;
        _subscribedParents.Clear();
        _menuIcons?.Dispose();
        _menuIcons = null;
    }

    /// <summary>
    /// Tears down all composed items and detaches from every contributor's
    /// ItemsChanged. After Dispose the composer holds no menu items and fires no
    /// further rebuilds. Idempotent.
    /// </summary>
    public void Dispose()
    {
        TearDownOwnedItems();
        foreach (var c in _contributors)
            c.ItemsChanged -= OnContributorChanged;
        _contributors.Clear();
        _resolvedRoot = null;
    }

    private ToolStripMenuItem ResolveDynamicRoot()
    {
        if (_resolvedRoot is not null) return _resolvedRoot;

        foreach (ToolStripItem existing in _host.Items)
        {
            if (existing is ToolStripMenuItem m && string.Equals(m.Text, _dynamicRoot, StringComparison.OrdinalIgnoreCase))
            {
                _resolvedRoot = m;
                return m;
            }
        }

        // Designer-level error: the static '<root>' top-level wasn't declared in the
        // host MenuStrip. Log a warning and create one so contributions still appear
        // instead of being silently dropped. The fallback is NOT added to _ownedItems
        // — Rebuild's teardown would dispose it and orphan the _resolvedRoot cache.
        _logWarning($"MenuComposer: host MenuStrip is missing a top-level '{_dynamicRoot}' item; creating one as a fallback.");
        var fallback = new ToolStripMenuItem(_dynamicRoot);
        _host.Items.Add(fallback);
        _resolvedRoot = fallback;
        return fallback;
    }

    private void ComposeChildren(ToolStripItemCollection dropDown, IList<MenuNode> children)
    {
        foreach (var node in children)
        {
            if (node.Spec is { IsSeparator: true })
            {
                var sep = new ToolStripSeparator();
                dropDown.Add(sep);
                _ownedItems.Add(sep);
                continue;
            }

            string caption = node.Spec?.DynamicCaption?.Invoke(_ctx) ?? node.Caption;

            if (node.Spec?.RequiresPermission is { } perm && !_ctx.Auth.Can(perm)) continue;

            // Evaluated at compose time; re-evaluation comes from contributor.ItemsChanged.
            if (node.Spec?.IsVisible is { } vis && !vis(_ctx)) continue;

            var item = new ToolStripMenuItem(caption)
            {
                Enabled = node.Spec?.IsEnabled?.Invoke(_ctx) ?? true,
            };

            if (node.Spec?.IconName is { } icon && _menuIcons is not null)
            {
                try { item.Image = _menuIcons.Images[icon.ToLowerInvariant()]; }
                catch { }
            }

            if (node.Spec?.Shortcut is { } scut)
            {
                if (TryParseShortcut(scut, out Keys keys))
                {
                    item.ShortcutKeys = keys;
                }
                else
                {
                    // Fall back to display-only when parsing fails — at least
                    // the menu shows the intended chord even if the binding
                    // doesn't work, and we log a warning.
                    item.ShortcutKeyDisplayString = scut;
                    _logWarning($"Failed to parse shortcut '{scut}' for '{node.Spec.Path}' — display-only.");
                }
            }

            if (node.Spec?.OnClick is { } click)
                item.Click += (_, _) =>
                {
                    try { click(_ctx); }
                    catch (Exception ex) { _logWarning($"OnClick for '{node.Spec.Path}' threw: {ex.Message}"); }
                };

            dropDown.Add(item);
            _ownedItems.Add(item);

            if (node.Spec?.DynamicCaption is not null)
            {
                _dynamicCaptionItems.Add((item, node.Spec));
                // Subscribe the parent (if it's a ToolStripDropDownItem) once.
                // The MenuStrip top-level items ARE ToolStripMenuItems, which
                // inherit ToolStripDropDownItem, so this works for them too.
                if (item.OwnerItem is ToolStripDropDownItem ddOwner && _subscribedParents.Add(ddOwner))
                {
                    ddOwner.DropDownOpening += OnDropDownOpening;
                }
            }

            ComposeChildren(item.DropDownItems, node.Children);
        }
    }

    private void OnDropDownOpening(object? sender, EventArgs e)
    {
        if (sender is not ToolStripDropDownItem parent) return;
        foreach (var (item, spec) in _dynamicCaptionItems)
        {
            if (item.OwnerItem != parent) continue;
            if (spec.DynamicCaption is { } cap)
            {
                try { item.Text = cap(_ctx); }
                catch (Exception ex) { _logWarning($"DynamicCaption for '{spec.Path}' threw: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Parses strings like "Ctrl+Shift+F", "Ctrl+Alt+P", "F5" into a Keys enum.
    /// Modifier order is flexible; case-insensitive. Returns false on any
    /// unrecognized token.
    /// </summary>
    private static bool TryParseShortcut(string text, out Keys keys)
    {
        keys = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Keys mods = Keys.None;
        Keys main = Keys.None;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= Keys.Control;
                    break;
                case "shift":
                    mods |= Keys.Shift;
                    break;
                case "alt":
                    mods |= Keys.Alt;
                    break;
                default:
                    if (!Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed))
                        return false;
                    if (main != Keys.None)
                        return false;
                    main = parsed;
                    break;
            }
        }
        if (main == Keys.None) return false;
        keys = main | mods;
        return true;
    }
}
