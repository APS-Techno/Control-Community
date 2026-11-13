using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using APS.SystemControl.Core;

namespace CoreLogicTests;

public class UserLayoutMainWindowTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);
    private static readonly Rectangle Secondary = new(1920, 0, 1920, 1040);

    private static UserLayout RoundTrip(UserLayout layout)
    {
        var ser = new XmlSerializer(typeof(UserLayout));
        using var sw = new StringWriter();
        ser.Serialize(sw, layout);
        using var sr = new StringReader(sw.ToString());
        return (UserLayout)ser.Deserialize(sr)!;
    }

    [Fact]
    public void NormalState_AndBounds_RoundTripThroughXml()
    {
        var layout = new UserLayout();
        layout.RememberMainWindow(FormWindowState.Normal, FormWindowState.Normal, new Rectangle(40, 30, 2000, 1080));

        var loaded = RoundTrip(layout);

        Assert.False(loaded.MainWindowMaximized);
        Assert.Equal((40, 30, 2000, 1080),
            (loaded.MainWindowX, loaded.MainWindowY, loaded.MainWindowWidth, loaded.MainWindowHeight));
        Assert.True(loaded.HasMainWindowBounds);
    }

    [Fact]
    public void LegacyXml_WithoutWindowElements_DefaultsToMaximizedWithNoBounds()
    {
        const string legacy = "<?xml version=\"1.0\"?><UserLayout><SystemTabLeftWidth>300</SystemTabLeftWidth></UserLayout>";
        using var sr = new StringReader(legacy);
        var loaded = (UserLayout)new XmlSerializer(typeof(UserLayout)).Deserialize(sr)!;

        Assert.Null(loaded.MainWindowMaximized);
        Assert.False(loaded.HasMainWindowBounds);
        Assert.Equal(300, loaded.SystemTabLeftWidth);
    }

    [Fact]
    public void Maximized_KeepsRestoreBounds()
    {
        var layout = new UserLayout();
        layout.RememberMainWindow(FormWindowState.Maximized, FormWindowState.Maximized, new Rectangle(100, 80, 1600, 900));

        Assert.True(layout.MainWindowMaximized);
        Assert.Equal((100, 80, 1600, 900),
            (layout.MainWindowX, layout.MainWindowY, layout.MainWindowWidth, layout.MainWindowHeight));
    }

    [Theory]
    [InlineData(FormWindowState.Maximized, true)]
    [InlineData(FormWindowState.Normal, false)]
    public void Minimized_PersistsStateItWasMinimizedFrom(FormWindowState before, bool expectedMaximized)
    {
        var layout = new UserLayout();
        layout.RememberMainWindow(FormWindowState.Minimized, before, new Rectangle(100, 80, 1600, 900));

        Assert.Equal(expectedMaximized, layout.MainWindowMaximized);
    }

    [Fact]
    public void DegenerateBounds_DoNotOverwriteSavedBounds()
    {
        var layout = new UserLayout();
        layout.RememberMainWindow(FormWindowState.Normal, FormWindowState.Normal, new Rectangle(40, 30, 2000, 1080));
        layout.RememberMainWindow(FormWindowState.Minimized, FormWindowState.Normal, new Rectangle(-32000, -32000, 160, 28));

        Assert.Equal((40, 30, 2000, 1080),
            (layout.MainWindowX, layout.MainWindowY, layout.MainWindowWidth, layout.MainWindowHeight));
    }

    [Fact]
    public void Fit_KeepsRectangleVisibleOnSecondaryMonitor()
    {
        var saved = new Rectangle(2100, 100, 1400, 800);

        Assert.Equal(saved, WindowPlacement.Fit(saved, [Primary, Secondary], Primary));
    }

    [Fact]
    public void Fit_CentersOnPrimary_WhenSecondaryMonitorIsGone()
    {
        var saved = new Rectangle(2100, 100, 1400, 800);

        var fitted = WindowPlacement.Fit(saved, [Primary], Primary);

        Assert.Equal(new Rectangle(260, 120, 1400, 800), fitted);
    }

    [Fact]
    public void Fit_ClampsSizeToPrimaryWorkingArea()
    {
        var saved = new Rectangle(5000, 5000, 3000, 2000);

        var fitted = WindowPlacement.Fit(saved, [Primary], Primary);

        Assert.Equal(Primary, fitted);
    }

    [Fact]
    public void Fit_RejectsRectangleWithOnlyAThinSliverVisible()
    {
        var saved = new Rectangle(1900, 100, 1400, 800);

        var fitted = WindowPlacement.Fit(saved, [Primary], Primary);

        Assert.NotEqual(saved, fitted);
        Assert.True(Primary.Contains(fitted));
    }
}
