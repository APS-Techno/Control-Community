using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using APS.SystemControl.Core.Menus;
using APS.SystemControl.Plugin;
using CoreLogicTests.Fixtures;
using Xunit;

namespace CoreLogicTests;

public class MenuComposerTests
{
    [Fact]
    public void Contribution_LandsUnderConfiguredRoot_NotAsNewTopLevel()
    {
        using var host = new MenuStrip();
        var existingRoot = new ToolStripMenuItem { Text = "Hardware" };
        host.Items.Add(existingRoot);
        using var ctx = new StubMenuContext();

        var contributor = new SpecListContributor(new[]
        {
            new MenuItemSpec { Path = "Drives/Tune/Holder Offset", Id = "drives.holder.offset" }
        });

        var composer = new MenuComposer(host, ctx, dynamicRoot: "Hardware", logWarning: _ => { });
        composer.Compose(new IMenuContributor[] { contributor }, Array.Empty<Assembly>());

        // No new "Drives" top-level item.
        Assert.Single(host.Items);
        Assert.Same(existingRoot, host.Items[0]);

        // Contribution landed under Hardware → Drives → Tune → Holder Offset.
        var drives = Assert.Single(existingRoot.DropDownItems.OfType<ToolStripMenuItem>());
        Assert.Equal("Drives", drives.Text);
        var tune = Assert.Single(drives.DropDownItems.OfType<ToolStripMenuItem>());
        Assert.Equal("Tune", tune.Text);
        var leaf = Assert.Single(tune.DropDownItems.OfType<ToolStripMenuItem>());
        Assert.Equal("Holder Offset", leaf.Text);
    }

    [Fact]
    public void MissingHostTopLevel_LogsWarning_AndCreatesFallback()
    {
        using var host = new MenuStrip();
        using var ctx = new StubMenuContext();
        var warnings = new List<string>();

        var contributor = new SpecListContributor(new[]
        {
            new MenuItemSpec { Path = "Drives/Emergency Stop", Id = "drives.estop" }
        });

        var composer = new MenuComposer(host, ctx, dynamicRoot: "Hardware", logWarning: warnings.Add);
        composer.Compose(new IMenuContributor[] { contributor }, Array.Empty<Assembly>());

        Assert.Contains(warnings, w => w.Contains("Hardware") && w.Contains("missing", StringComparison.OrdinalIgnoreCase));
        var fallback = Assert.IsType<ToolStripMenuItem>(host.Items[0]);
        Assert.Equal("Hardware", fallback.Text);
        Assert.Single(fallback.DropDownItems);
    }

    [Fact]
    public void Dispose_RemovesOwnedItems_SoNextComposerDoesNotDuplicate()
    {
        using var host = new MenuStrip();
        var root = new ToolStripMenuItem { Text = "Hardware" };
        host.Items.Add(root);
        using var ctx = new StubMenuContext();

        var spec = new[] { new MenuItemSpec { Path = "Drives/Holder Offset", Id = "drives.holder.offset" } };

        var first = new MenuComposer(host, ctx, dynamicRoot: "Hardware", logWarning: _ => { });
        first.Compose(new IMenuContributor[] { new SpecListContributor(spec) }, Array.Empty<Assembly>());
        Assert.Single(root.DropDownItems.OfType<ToolStripMenuItem>());

        // Simulates FrmMain reload.
        first.Dispose();
        Assert.Empty(root.DropDownItems.OfType<ToolStripMenuItem>());

        var second = new MenuComposer(host, ctx, dynamicRoot: "Hardware", logWarning: _ => { });
        second.Compose(new IMenuContributor[] { new SpecListContributor(spec) }, Array.Empty<Assembly>());

        Assert.Single(root.DropDownItems.OfType<ToolStripMenuItem>());

        // Dispose is idempotent.
        second.Dispose();
        second.Dispose();
    }
}
