using System.Diagnostics;
using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

public class CoreDisconnectAllTests
{
    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public void DisconnectAll_calls_Disconnect_on_power_supplies_controllers_and_PLCs()
    {
        var core = NewCore();
        var psu = new RecordingPowerSupply("PSU1");
        var ctl = new RecordingController("GAUGE_A");
        var plc = new RecordingPlc();

        AddElement(core, DeviceType.Power, psu);
        AddElement(core, DeviceType.Controller, ctl);
        core.plcs.Add("main", plc);

        core.DisconnectAll();

        Assert.True(psu.DisconnectCalled);
        Assert.True(ctl.DisconnectCalled);
        Assert.True(plc.DisconnectCalled);
    }

    [Fact]
    public void DisconnectAll_swallows_per_device_failures_and_continues()
    {
        var core = NewCore();
        var thrower = new ThrowingPowerSupply("PSU_BAD");
        var ok      = new RecordingPowerSupply("PSU_OK");

        AddElement(core, DeviceType.Power, thrower);
        AddElement(core, DeviceType.Power, ok);

        core.DisconnectAll();

        Assert.True(ok.DisconnectCalled);   // second device still ran
    }

    [Fact]
    public void DisconnectAll_is_safe_with_no_devices_registered()
    {
        var core = NewCore();
        core.DisconnectAll();
    }

    [Fact]
    public void DisconnectAll_with_a_wedged_device_Close_bounds_teardown_and_returns()
    {
        // A serial port with no peer wedges .NET's SerialPort.Close() forever in the
        // native WaitCommEvent join, and DisconnectAll runs on the UI thread in
        // FormClosing, so it must bound the teardown and abandon the wedged Close.
        var core = NewCore();
        var release = new ManualResetEventSlim(false);
        AddElement(core, DeviceType.Controller, new HangingController("GAUGE_A_WEDGED", release));

        var sw = Stopwatch.StartNew();
        core.DisconnectAll();
        sw.Stop();

        release.Set();          // let the abandoned worker thread unwind cleanly

        // Waits the bounded timeout (~5s) but never blocks indefinitely.
        Assert.InRange(sw.Elapsed.TotalSeconds, 3.0, 9.0);
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

    private sealed class RecordingPowerSupply(string id)
        : BasePowerSupply(new BaseDeviceParams(id, 0, id))
    {
        public bool DisconnectCalled { get; private set; }
        public override void Disconnect() => DisconnectCalled = true;
    }

    private sealed class ThrowingPowerSupply(string id)
        : BasePowerSupply(new BaseDeviceParams(id, 0, id))
    {
        public override void Disconnect()
            => throw new InvalidOperationException("simulated transport failure");
    }

    private sealed class RecordingController(string id)
        : BaseController(new BaseDeviceParams(id, 0, id))
    {
        public bool DisconnectCalled { get; private set; }
        public override void Disconnect() => DisconnectCalled = true;
    }

    /// <summary>Controller whose Disconnect() blocks until released — stands in for
    /// the .NET SerialPort.Close() wedge on a port with no peer.</summary>
    private sealed class HangingController(string id, ManualResetEventSlim release)
        : BaseController(new BaseDeviceParams(id, 0, id))
    {
        public override void Disconnect() => release.Wait();
    }

    private sealed class RecordingPlc : IPLCController
    {
        public bool Connected { get; private set; }
        public bool DisconnectCalled { get; private set; }
        public void Connect() { Connected = true; }
        public void Connect(int address) { Connected = true; }
        public void Connect(string address) { Connected = true; }
        public void Connect(string address, int rack, int slot) { Connected = true; }
        public void Disconnect() { DisconnectCalled = true; Connected = false; }
        public void WriteFloat(int mode, int address, float value) { }
        public float ReadFloat(int mode, int address) => 0f;
        public void WriteInt(int mode, int address, short value) { }
        public int ReadInt(int mode, int address) => 0;
        public void WriteBool(int mode, int address, int index, bool value) { }
        public bool ReadBool(int mode, int address, int index) => false;
        public void WriteString(int mode, int address, string value) { }
        public string ReadString(int mode, int address) => "";
        public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests)
            => new bool[requests.Count];
        public byte ReadByte(int mode, int address) => 0;
        public void WriteByte(int mode, int address, byte value) { }
    }
}
