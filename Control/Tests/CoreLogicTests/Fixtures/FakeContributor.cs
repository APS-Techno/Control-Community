using APS.SystemControl.Plugin;

namespace CoreLogicTests.Fixtures;

[MenuContributor]
public sealed class FakeContributor : IMenuContributor
{
#pragma warning disable CS0067
    public event EventHandler? ItemsChanged;
#pragma warning restore CS0067
    public IEnumerable<MenuItemSpec> GetMenuItems(IMenuContext ctx)
        => [new MenuItemSpec { Path = "Fake/Item", Id = "fake-1" }];
}

public sealed class UnattributedContributor : IMenuContributor
{
#pragma warning disable CS0067
    public event EventHandler? ItemsChanged;
#pragma warning restore CS0067
    public IEnumerable<MenuItemSpec> GetMenuItems(IMenuContext ctx) => [];
}
