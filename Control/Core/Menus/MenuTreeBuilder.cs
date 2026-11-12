using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus;

/// <summary>
/// Pure function: list of MenuItemSpec → tree of MenuNode. Handles path
/// merging, ordering (stable on Order ties — for both leaf and interior
/// nodes), duplicate-path warnings, and separator preservation. Has no UI
/// dependency — fully unit-testable.
/// </summary>
internal static class MenuTreeBuilder
{
    public static (MenuNode Root, IReadOnlyList<string> Warnings) Build(IEnumerable<MenuItemSpec> specs)
    {
        var root = new MenuNode { Caption = "" };
        var warnings = new List<string>();
        int nextInsertion = 0;

        foreach (var spec in specs)
        {
            string path = spec.Path?.TrimStart('/') ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                warnings.Add("MenuItemSpec with empty Path skipped.");
                continue;
            }

            string[] segments = path.Split('/');
            MenuNode cursor = root;

            for (int i = 0; i < segments.Length; i++)
            {
                bool isLeaf = i == segments.Length - 1;
                string seg = segments[i];

                MenuNode? child = cursor.Children.FirstOrDefault(c => c.Caption == seg && c.Spec == null);
                // Existing leaf with same caption only matches when path is identical
                if (isLeaf)
                    child = cursor.Children.FirstOrDefault(c => c.Caption == seg);

                if (child == null)
                {
                    child = new MenuNode { Caption = seg, InsertionOrder = nextInsertion++ };
                    cursor.Children.Add(child);
                }
                else if (isLeaf && child.Spec != null)
                {
                    warnings.Add($"Duplicate menu path '{path}' — last contribution wins.");
                }

                if (isLeaf) child.Spec = spec;
                cursor = child;
            }
        }

        SortRecursive(root);
        return (root, warnings);
    }

    private static void SortRecursive(MenuNode node)
    {
        node.Children.Sort((a, b) =>
        {
            int orderA = a.Spec?.Order ?? 0;
            int orderB = b.Spec?.Order ?? 0;
            if (orderA != orderB) return orderA.CompareTo(orderB);
            return a.InsertionOrder.CompareTo(b.InsertionOrder);
        });
        foreach (var c in node.Children) SortRecursive(c);
    }
}
