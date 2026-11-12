using System;
using System.Collections.Generic;
using APS.SystemControl.Plugin;

namespace CoreLogicTests.Fixtures;

internal sealed class SpecListContributor : IMenuContributor
{
    private readonly IReadOnlyList<MenuItemSpec> _specs;

    public SpecListContributor(IEnumerable<MenuItemSpec> specs)
    {
        _specs = new List<MenuItemSpec>(specs);
    }

#pragma warning disable CS0067
    public event EventHandler? ItemsChanged;
#pragma warning restore CS0067

    public IEnumerable<MenuItemSpec> GetMenuItems(IMenuContext ctx) => _specs;
}
