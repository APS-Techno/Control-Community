using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus;

/// <summary>
/// Internal tree node built by MenuTreeBuilder. Interior nodes (no Spec)
/// represent folders constructed from path segments; leaf nodes carry the
/// originating MenuItemSpec.
/// </summary>
internal sealed class MenuNode
{
    public string Caption { get; set; } = "";
    public List<MenuNode> Children { get; } = [];
    public MenuItemSpec? Spec { get; set; }

    /// <summary>
    /// Sequence number assigned when this node is first added to its parent's
    /// Children collection. Used as a stable tie-break in the sort comparator
    /// so interior nodes (which have no Spec) keep their insertion order even
    /// when multiple share the default Order = 0.
    /// </summary>
    public int InsertionOrder { get; set; }
}
