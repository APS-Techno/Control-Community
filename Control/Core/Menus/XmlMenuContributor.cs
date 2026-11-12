using APS.Controls.Core;
using APS.SystemControl.Plugin;
using APS.SystemControl.XML.GUI;

namespace APS.SystemControl.Core.Menus;

/// <summary>
/// Adapter that turns a list of XML_MenuContribution rows into an
/// IMenuContributor. One instance per Menus.xml file. The action string
/// is parsed lazily inside the OnClick callback so reload works without
/// re-instantiating contributors.
/// </summary>
internal sealed class XmlMenuContributor : IMenuContributor
{
    private readonly IReadOnlyList<XML_MenuContribution> _rows;

    public XmlMenuContributor(IReadOnlyList<XML_MenuContribution> rows)
    {
        // Defensive copy — caller may not retain ownership, and an immutable
        // snapshot makes GetMenuItems' lazy enumeration deterministic.
        _rows = rows.ToList();
    }

    /// <summary>
    /// XML contributors are static — Menus.xml never reloads in-process — so
    /// this event never fires. Empty add/remove suppresses CS0067 and signals
    /// the design intent: no live reload from this source.
    /// </summary>
    public event EventHandler? ItemsChanged
    {
        add { }
        remove { }
    }

    public IEnumerable<MenuItemSpec> GetMenuItems(IMenuContext ctx)
    {
        foreach (var row in _rows)
        {
            yield return new MenuItemSpec
            {
                Path = row.Path,
                Order = row.Order,
                IconName = row.Icon,
                Shortcut = row.Shortcut,
                RequiresPermission = row.RequiresPermission,
                IsVisible = row.Requires is { } feature
                    ? c => c.Flags.Has(feature)
                    : null,
                OnClick = string.IsNullOrEmpty(row.Action) ? null : c => Invoke(c, row.Action),
            };
        }
    }

    private static void Invoke(IMenuContext ctx, string action)
    {
        // "command:Type:Id:Cmd[:args…]" or "form:Lib.dll:Namespace.Type"
        string[] parts = action.Split(':');
        switch (parts[0])
        {
            case "command" when parts.Length >= 4:
                if (Enum.TryParse<DeviceType>(parts[1], out var devType))
                {
                    object[] args = parts.Length > 4 ? parts[4..] : [];
                    object[] payload = new object[args.Length + 1];
                    payload[0] = parts[3];
                    Array.Copy(args, 0, payload, 1, args.Length);
                    ctx.Hw.SendCommand(devType, parts[2], payload);
                }
                break;

            case "form" when parts.Length >= 3:
                // Not wired: XmlMenuLoader is pure (no Assembly.LoadFrom).
                ctx.Ui.ShowMessage("Menu", $"form: action not yet wired ({action}).");
                break;
        }
    }
}
