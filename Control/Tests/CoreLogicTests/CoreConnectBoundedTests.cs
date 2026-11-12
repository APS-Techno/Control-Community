using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Boot-time hardware connect must be bounded per device. A serial port whose
/// peer is gone can wedge <c>SerialPort.Open()</c> (or an initial read)
/// indefinitely, and Connect runs on the UI thread inside the FrmMain
/// constructor before the message loop starts, so one wedged <c>Connect()</c>
/// would freeze the app on the splash.
/// </summary>
public class CoreConnectBoundedTests
{
    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public async Task ConnectControllers_bounds_a_wedged_Connect_and_still_connects_the_rest()
    {
        var core = NewCore();
        var release = new ManualResetEventSlim(false);
        var wedged = new HangingConnectController("GAUGE_A_WEDGED", release);
        var ok = new RecordingConnectController("GAUGE-B_OK");

        // Insertion order: the wedged device is reached first, so a naive
        // synchronous loop would never reach the second controller.
        AddElement(core, DeviceType.Controller, wedged);
        AddElement(core, DeviceType.Controller, ok);

        // Run on a worker so an unbounded (unfixed) ConnectControllers can't hang
        // the test host; the guard turns the boot-freeze into a clean failure.
        var connect = Task.Run(() => core.ConnectControllers());
        var finished = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(20)));
        bool completed = finished == connect;

        release.Set();   // let the abandoned worker unwind cleanly

        Assert.True(completed,
            "ConnectControllers hung on a wedged Connect() instead of bounding it.");
        Assert.True(ok.ConnectCalled,
            "A wedged controller stranded the controllers that follow it.");
        Assert.False(wedged.Connected,
            "A connect that timed out must leave the device disconnected.");
    }

    [Fact]
    public void ConnectRobots_connects_every_robot()
    {
        var core = NewCore();
        var robot = new RecordingConnectRobot("ROBOT");
        AddElement(core, DeviceType.Robot, robot);

        core.ConnectRobots();

        Assert.True(robot.ConnectCalled);
        Assert.True(robot.Connected);
    }

    private static void AddElement(APS.SystemControl.Core.Core core, DeviceType type, IBaseComponent device)
    {
        if (!core.hwElements.Item.TryGetValue(type, out var elements))
        {
            elements = new Elements();
            core.hwElements.Item[type] = elements;
        }
        elements.Add(device.Params.ID, device);
    }

    /// <summary>Controller whose Connect() blocks until released — stands in for the
    /// .NET SerialPort.Open() wedge on a virtual port whose peer has gone.</summary>
    private sealed class HangingConnectController(string id, ManualResetEventSlim release)
        : BaseController(new BaseDeviceParams(id, 0, id))
    {
        public override bool Connect()
        {
            release.Wait();
            return true;   // would "succeed" eventually, but far too late for boot
        }
    }

    private sealed class RecordingConnectController(string id)
        : BaseController(new BaseDeviceParams(id, 0, id))
    {
        public bool ConnectCalled { get; private set; }
        public override bool Connect() { ConnectCalled = true; Connected = true; return true; }
    }

    private sealed class RecordingConnectRobot(string id) : BaseRobot(new BaseDeviceParams(id, 0, id))
    {
        public bool ConnectCalled { get; private set; }
        public override bool Connect() { ConnectCalled = true; Connected = true; return true; }
        public override void Poll() { }
        public override void Transfer(byte srcFace, byte dstFace) { }
        public override void Home() { }
        public override void Abort() { }
    }
}
