using APS.SystemControl.Plugin;
using Xunit;

namespace CoreLogicTests;

public class MenuItemSpecTests
{
    [Fact]
    public void Path_IsRequired_AndPreserved()
    {
        var spec = new MenuItemSpec { Path = "Drives/Tune/Holder Offset" };
        Assert.Equal("Drives/Tune/Holder Offset", spec.Path);
    }

    [Fact]
    public void OptionalFields_DefaultToNullOrFalse()
    {
        var spec = new MenuItemSpec { Path = "X" };
        Assert.Null(spec.Id);
        Assert.Null(spec.Shortcut);
        Assert.Null(spec.IconName);
        Assert.Equal(0, spec.Order);
        Assert.Null(spec.IsVisible);
        Assert.Null(spec.IsEnabled);
        Assert.Null(spec.DynamicCaption);
        Assert.Null(spec.OnClick);
        Assert.False(spec.IsSeparator);
    }

    [Fact]
    public void Separator_HasIsSeparatorTrue()
    {
        var spec = new MenuItemSpec { Path = "Drives/-", IsSeparator = true };
        Assert.True(spec.IsSeparator);
    }
}
