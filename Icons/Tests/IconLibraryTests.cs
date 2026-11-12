using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace APS.Icons.Tests;

public class IconLibraryTests
{
    [Fact]
    public void Get_ReturnsBitmapOfRequestedSize()
    {
        using var bmp = IconLibrary.Get("save", 16);
        Assert.Equal(16, bmp.Width);
        Assert.Equal(16, bmp.Height);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        using var lower = IconLibrary.Get("save", 24);
        using var mixed = IconLibrary.Get("SAVE", 24);
        Assert.Equal(lower.Size, mixed.Size);
    }

    [Fact]
    public void Get_UnknownName_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            IconLibrary.Get("nonexistent-icon", 16));
    }

    [Theory]
    [InlineData(20)]   // not pre-rendered: from 24, resized down
    [InlineData(30)]   // 24 logical px at 125 % DPI
    [InlineData(36)]   // 150 %
    [InlineData(42)]   // 175 %
    [InlineData(48)]   // 200 % (exact source)
    public void Get_ToolbarSizeAtEveryDpi_ReturnsExactRawBitmap(int size)
    {
        // Assert on the raw bitmap, NOT on ImageList.Images[key]: an ImageList always
        // hands back a copy stretched to its ImageSize, which would hide a regression
        // that returned the 24 px source unscaled.
        using var bmp = IconLibrary.Get(IconName.Run, size);
        Assert.Equal(size, bmp.Width);
        Assert.Equal(size, bmp.Height);
    }

    [Fact]
    public void ConfigureToolStrip_SetsScalingSizeAndListAndResolvesImageKey()
    {
        using var strip = new ToolStrip();
        var list = IconLibrary.ConfigureToolStrip(strip, 36, IconName.Run, IconName.Stop);

        Assert.Equal(new Size(36, 36), strip.ImageScalingSize);
        Assert.Same(list, strip.ImageList);

        // Designer-created buttons arrive with a resource Image already set; an explicit
        // Image wins over ImageKey, so callers null it first. Guard that sequence.
        var btn = new ToolStripButton { Image = new Bitmap(32, 32) };
        strip.Items.Add(btn);
        btn.Image = null;
        btn.ImageKey = IconName.Run;
        Assert.NotNull(btn.Image);
        Assert.Equal(new Size(36, 36), btn.Image!.Size);
    }

    [Fact]
    public void ConfigureToolStrip_DisposesListWithStrip()
    {
        var strip = new ToolStrip();
        var list = IconLibrary.ConfigureToolStrip(strip, 24, IconName.Run);
        bool disposed = false;
        list.Disposed += (_, _) => disposed = true;

        strip.Dispose();

        Assert.True(disposed);
    }

    [Fact]
    public void BuildImageList_PopulatesKeysByName()
    {
        using var list = IconLibrary.BuildImageList(16, IconName.Save);
        Assert.Contains("save", list.Images.Keys.Cast<string>());
        Assert.Equal(new Size(16, 16), list.ImageSize);
    }
}
