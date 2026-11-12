using System.Collections.Generic;
using System.Linq;
using APS.SystemControl.Core.Menus;
using APS.SystemControl.Plugin;
using Xunit;

namespace CoreLogicTests;

public class MenuTreeBuilderTests
{
    [Fact]
    public void SinglePath_BuildsLinearChain()
    {
        var specs = new List<MenuItemSpec> {
            new() { Path = "Drives/Tune/Holder Offset" }
        };
        var (root, warnings) = MenuTreeBuilder.Build(specs);

        Assert.Empty(warnings);
        var drive = Assert.Single(root.Children);
        Assert.Equal("Drives", drive.Caption);
        var tune = Assert.Single(drive.Children);
        Assert.Equal("Tune", tune.Caption);
        var leaf = Assert.Single(tune.Children);
        Assert.Equal("Holder Offset", leaf.Caption);
    }

    [Fact]
    public void TwoPaths_SharingPrefix_ShareInteriorNode()
    {
        var specs = new List<MenuItemSpec> {
            new() { Path = "Drives/Tune/Holder Offset" },
            new() { Path = "Drives/Tune/Shutter Offset" }
        };
        var (root, _) = MenuTreeBuilder.Build(specs);

        var drive = Assert.Single(root.Children);
        var tune = Assert.Single(drive.Children);
        Assert.Equal(2, tune.Children.Count);
        Assert.Contains(tune.Children, n => n.Caption == "Holder Offset");
        Assert.Contains(tune.Children, n => n.Caption == "Shutter Offset");
    }

    [Fact]
    public void LeadingSlash_IsStripped()
    {
        var specs = new List<MenuItemSpec> { new() { Path = "/Drives/Tune" } };
        var (root, _) = MenuTreeBuilder.Build(specs);

        Assert.Equal("Drives", root.Children[0].Caption);
    }

    [Fact]
    public void EmptyPath_ProducesWarning()
    {
        var specs = new List<MenuItemSpec> { new() { Path = "" } };
        var (_, warnings) = MenuTreeBuilder.Build(specs);

        Assert.Single(warnings);
        Assert.Contains("empty", warnings[0], System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicatePath_LastWins_AndWarns()
    {
        var first = new MenuItemSpec { Path = "Drives/A", Id = "first" };
        var second = new MenuItemSpec { Path = "Drives/A", Id = "second" };
        var (root, warnings) = MenuTreeBuilder.Build(new[] { first, second });

        var drive = Assert.Single(root.Children);
        var leaf = Assert.Single(drive.Children);
        Assert.Equal("second", leaf.Spec!.Id);
        Assert.Single(warnings);
        Assert.Contains("Drives/A", warnings[0]);
    }

    [Fact]
    public void Order_IsRespected_Stable()
    {
        var specs = new List<MenuItemSpec> {
            new() { Path = "Root/B", Order = 2 },
            new() { Path = "Root/A", Order = 1 },
            new() { Path = "Root/C", Order = 1 }, // tie with A; preserve insertion order
        };
        var (root, _) = MenuTreeBuilder.Build(specs);
        var rootNode = Assert.Single(root.Children);
        var captions = rootNode.Children.Select(c => c.Caption).ToList();
        Assert.Equal(new[] { "A", "C", "B" }, captions);
    }

    [Fact]
    public void Separator_PreservedAsNode()
    {
        var specs = new List<MenuItemSpec> {
            new() { Path = "Drives/A" },
            new() { Path = "Drives/-", IsSeparator = true, Order = 5 },
            new() { Path = "Drives/B", Order = 10 },
        };
        var (root, _) = MenuTreeBuilder.Build(specs);
        var drive = Assert.Single(root.Children);
        Assert.Equal(3, drive.Children.Count);
        Assert.True(drive.Children[1].Spec!.IsSeparator);
    }

    [Fact]
    public void InteriorNodes_PreserveInsertionOrder_OnOrderTie()
    {
        // Interior nodes (Drives, Power, Tools) carry no Spec and all tie at Order = 0;
        // List<T>.Sort is unstable, so insertion order must be tracked on the node.
        var specs = new List<MenuItemSpec> {
            new() { Path = "Drives/A" },
            new() { Path = "Power/X" },
            new() { Path = "Tools/Z" },
        };
        var (root, _) = MenuTreeBuilder.Build(specs);

        var captions = root.Children.Select(c => c.Caption).ToList();
        Assert.Equal(new[] { "Drives", "Power", "Tools" }, captions);
    }
}
