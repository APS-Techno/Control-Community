using System;
using System.Collections.Generic;
using System.Reflection;
using APS.Controls.Core;
using APS.Controls.Vacuum;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The transfer-module widget's service actions must post worker operations
/// to the HW channel like every other manual control. Calling BaseRobot directly
/// from the click handler blocks the UI thread on synchronous S7-TCP round-trips,
/// contends with CoreThread's socket lock and drops a failed write silently (audit C8).
/// </summary>
public class TransferWidgetWiringTests
{
    private sealed class FakeRobot : BaseRobot
    {
        public FakeRobot() : base(new BaseDeviceParams("TM_R", 0, "robot"))
        {
            Faces = new Dictionary<string, byte> { ["C1"] = 1, ["LL"] = 5 };
        }
        public override void Poll() { }
        public override void Transfer(byte srcFace, byte dstFace) => throw new InvalidOperationException("direct HW call from UI wiring");
        public override void Home() => throw new InvalidOperationException("direct HW call from UI wiring");
        public override void Abort() => throw new InvalidOperationException("direct HW call from UI wiring");
    }

    /// <summary>Raises one of the widget's service events via its compiler-generated
    /// backing field (the events are only raised from the private service dialog).</summary>
    private static void Raise(TransferModuleControl w, string eventName, (byte, byte)? transferArgs = null)
    {
        var d = (Delegate?)typeof(TransferModuleControl)
            .GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w);
        if (transferArgs is { } t) ((EventHandler<(byte src, byte dst)>?)d)?.Invoke(w, t);
        else ((EventHandler?)d)?.Invoke(w, EventArgs.Empty);
    }

    [Fact]
    public void Home_click_posts_home_operation_to_hw_channel()
    {
        using var widget = new TransferModuleControl("TM_R");
        var posted = new List<ThreadMsg>();
        TransferWidgetWiring.Wire(widget, new FakeRobot(), posted.Add);

        Raise(widget, nameof(TransferModuleControl.HomeRequested));

        var msg = Assert.IsType<ParamMsg>(Assert.Single(posted));
        Assert.Equal(MsgType.Command, msg.Type);
        Assert.Equal("home", msg.Operation);
        Assert.Equal(DeviceType.Robot, msg.Args[0]);
        Assert.Equal("TM_R", msg.Args[1]);
    }

    [Fact]
    public void Abort_click_posts_abort_operation_to_hw_channel()
    {
        using var widget = new TransferModuleControl("TM_R");
        var posted = new List<ThreadMsg>();
        TransferWidgetWiring.Wire(widget, new FakeRobot(), posted.Add);

        Raise(widget, nameof(TransferModuleControl.AbortRequested));

        var msg = Assert.IsType<ParamMsg>(Assert.Single(posted));
        Assert.Equal("abort", msg.Operation);
        Assert.Equal("TM_R", msg.Args[1]);
    }

    [Fact]
    public void Transfer_click_posts_transfer_with_face_names()
    {
        using var widget = new TransferModuleControl("TM_R");
        var posted = new List<ThreadMsg>();
        TransferWidgetWiring.Wire(widget, new FakeRobot(), posted.Add);

        Raise(widget, nameof(TransferModuleControl.TransferRequested), (1, 5));

        var msg = Assert.IsType<ParamMsg>(Assert.Single(posted));
        Assert.Equal("transfer", msg.Operation);
        Assert.Equal(DeviceType.Robot, msg.Args[0]);
        Assert.Equal("TM_R", msg.Args[1]);
        Assert.Equal("C1", msg.Args[2]);   // byte face → configured name (worker ops take names)
        Assert.Equal("LL", msg.Args[3]);
    }

    [Fact]
    public void FaceName_falls_back_to_numeric_string_for_unknown_face()
    {
        var faces = new Dictionary<string, byte> { ["C1"] = 1 };
        Assert.Equal("C1", TransferWidgetWiring.FaceName(faces, 1));
        Assert.Equal("7", TransferWidgetWiring.FaceName(faces, 7));
    }
}
