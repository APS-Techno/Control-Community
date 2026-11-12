namespace APS.SystemControl.Plugin;

/// <summary>
/// Implemented by any class wanting to contribute menu items. Discovered by
/// the composer either via [MenuContributor] attribute scan or by the panel
/// loader (any IBaseCustomPanel that also implements IMenuContributor).
/// </summary>
public interface IMenuContributor
{
    /// <summary>
    /// Return the menu items contributed by this object. Called once at
    /// composition time and again whenever ItemsChanged fires.
    /// </summary>
    IEnumerable<MenuItemSpec> GetMenuItems(IMenuContext ctx);

    /// <summary>
    /// Fire to ask the composer to rebuild this contributor's subtree —
    /// e.g., after hardware connection state changes the set of valid
    /// items. Subscribers run on the UI thread; raise on UI or call
    /// IUiServices.RunOnUi first.
    /// </summary>
    event EventHandler? ItemsChanged;
}
