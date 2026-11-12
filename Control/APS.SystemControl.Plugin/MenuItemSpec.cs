namespace APS.SystemControl.Plugin;

/// <summary>
/// Declarative description of one menu node contributed by an HW or GUI library.
/// Composed by the MenuComposer at startup. All callback fields receive the live
/// IMenuContext so contributors stay decoupled from FrmMain internals.
/// </summary>
public sealed record MenuItemSpec
{
    /// <summary>
    /// Slash-separated path from the dynamic root to this leaf (or interior
    /// node). The path is interpreted relative to MenuComposer's configured
    /// dynamic root (default "Hardware"), so a contributor emitting
    /// "Drives/Tune/Holder Offset…" lands at Hardware → Drives → Tune →
    /// Holder Offset…. A leading slash is allowed and stripped. Each segment
    /// becomes one ToolStripMenuItem caption.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Stable opaque ID used for live updates (DynamicCaption refresh,
    /// IsEnabled re-evaluation). Optional. Two contributors must not emit
    /// the same Id; the composer warns on collision.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Keyboard shortcut as a string the composer parses, e.g. "Ctrl+Shift+I".
    /// Format follows ToolStripMenuItem.ShortcutKeyDisplayString conventions.
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// Logical icon name resolved through APS.Icons.IconLibrary at composition
    /// time. Null = no icon.
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>
    /// Sort order within parent — lower first. Items with equal Order keep
    /// their contribution order (stable sort).
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Optional permission name (matching the host's permission enum). When set, the item
    /// is hidden unless the current user has that permission. Re-evaluated on Refresh.
    /// </summary>
    public string? RequiresPermission { get; init; }

    /// <summary>Optional predicate: hide the item when false. Re-evaluated on refresh.</summary>
    public Func<IMenuContext, bool>? IsVisible { get; init; }

    /// <summary>Optional predicate: gray out the item when false. Re-evaluated on refresh.</summary>
    public Func<IMenuContext, bool>? IsEnabled { get; init; }

    /// <summary>
    /// Optional dynamic caption — overrides the leaf segment of Path when present.
    /// </summary>
    public Func<IMenuContext, string>? DynamicCaption { get; init; }

    /// <summary>Click handler. Null is allowed for parent-only nodes and separators.</summary>
    public Action<IMenuContext>? OnClick { get; init; }

    /// <summary>True for separator items — Path's last segment is conventionally "-".</summary>
    public bool IsSeparator { get; init; }
}
