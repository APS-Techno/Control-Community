using System.ComponentModel;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.GUIControls.Process;

namespace CoreLogicTests;

public class FloatingPanelSmokeTests
{
    public FloatingPanelSmokeTests()
    {
        // The bus boot-gate would downgrade Operator → Detail and hide the
        // smoke-test entries; open it for every test in this class.
        EventLogBus.MarkBootComplete();
    }

    /// <summary>
    /// Realizes the control's Win32 handle, returning false when the session
    /// cannot create windows. On non-interactive / restricted desktop sessions
    /// (VMware console, disconnected RDP, headless CI) <see cref="Control.CreateControl()"/>
    /// fails with Win32Exception 0x80004005 ("Failed to set Win32 parent window")
    /// — these are UI smoke tests, so treat that as a skip rather than letting
    /// the unhandled exception crash the test host with a JIT-debug dialog.
    /// </summary>
    private static bool TryCreateControl(Control c)
    {
        try { c.CreateControl(); return true; }
        catch (Win32Exception) { return false; }
    }

    /// <summary>
    /// Forces the control's handle into existence (CreateControl is a no-op on
    /// an invisible control). AppendEvent drops pre-handle entries by design
    /// — a publisher thread must never be the one to create
    /// the handle — so these smoke tests realize it explicitly first.
    /// </summary>
    private static bool TryRealizeHandle(Control c)
    {
        try { _ = c.Handle; return true; }
        catch (Win32Exception) { return false; }
    }


    [Fact]
    public void FloatingPanel_DefaultsAreSensible()
    {
        using var form = new Form { Width = 800, Height = 600 };
        var fp = new FloatingPanel
        {
            Caption = "Events",
            MinSize = new Size(200, 100),
            Bounds = new Rectangle(50, 50, 300, 200),
        };
        form.Controls.Add(fp);
        if (!TryCreateControl(form)) return;   // no window station — skip

        Assert.Equal("Events", fp.Caption);
        Assert.True(form.Controls.Contains(fp));
        Assert.Equal(300, fp.Width);
        Assert.Equal(200, fp.Height);
        Assert.NotNull(fp.ContentPanel);
    }

    [Fact]
    public void EventsLogControl_AppendsOperatorEventOnly()
    {
        using var form = new Form { Width = 400, Height = 300 };
        var log = new EventsLogControl { Dock = DockStyle.Fill };
        form.Controls.Add(log);
        if (!TryCreateControl(form)) return;   // no window station — skip
        if (!TryRealizeHandle(log)) return;

        log.AppendEvent(new EventLogEntry(DateTime.Now, "Recipe", "started", LogEventLevel.Operator));
        log.AppendEvent(new EventLogEntry(DateTime.Now, "PLC",    "tick",    LogEventLevel.Detail));

        Assert.Contains("started", log.Text);
        Assert.DoesNotContain("tick", log.Text);
    }

    [Fact]
    public void EventsLogControl_RespondsToBusPublish()
    {
        using var form = new Form { Width = 400, Height = 300 };
        var log = new EventsLogControl { Dock = DockStyle.Fill };
        form.Controls.Add(log);
        if (!TryCreateControl(form)) return;   // no window station — skip
        if (!TryRealizeHandle(log)) return;

        EventLogBus.Publish("Recipe", "stack 2/4", LogEventLevel.Operator);
        // Allow any BeginInvoke to drain via DoEvents — but we're on the test
        // thread without a message loop. AppendEvent will take the same-thread
        // branch since the control was created on this thread, so the text
        // should already be in.
        Application.DoEvents();

        Assert.Contains("stack 2/4", log.Text);
    }
}
