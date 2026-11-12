using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The cluster panel and transfer-module widgets are refreshed by a 200 ms
/// WinForms timer on the UI thread. Live device reads (sensor.Value(),
/// pump.Status, valve.IsOpen, …) each issue a blocking PLC TCP round-trip and
/// race the background poll on the Sharp7 socket, so every device exposes a
/// Last* cache, refreshed as a side effect of the background poll's read, that
/// the UI reads with zero PLC I/O.
/// </summary>
public class DeviceLastValueCacheTests
{
    private sealed class CountingSensor : BaseSensor
    {
        public int Reads;
        public float Next;
        public CountingSensor(IBaseDeviceParams ps) : base(ps) => Connected = true;
        protected override float ReadHW() { Reads++; return Next; }
    }

    private sealed class CountingPump : BasePump
    {
        public int Reads;
        public bool[] Next = [];
        public CountingPump(IBaseDeviceParams ps) : base(ps) => Connected = true;
        protected override bool[] ReadStatus() { Reads++; return Next; }
    }

    private sealed class TestValve : PlcValve
    {
        private readonly (int, int, int) _desc;
        public TestValve(IPLCController plc, IBaseDeviceParams ps, (int, int, int) desc) : base(plc, ps)
        {
            _desc = desc;
            Connected = true;
        }
        public override bool TryGetBulkReadDescriptor(out (int mode, int address, int byteIndex) descriptor)
        {
            descriptor = _desc;
            return true;
        }
    }

    [Fact]
    public void Sensor_LastValue_returns_polled_value_with_no_extra_read()
    {
        var s = new CountingSensor(new BaseDeviceParams("pl", 0, "")) { Next = 1.5e-3f };
        Assert.Equal(0, s.Reads);

        float polled = s.Value();          // background poll
        Assert.Equal(1, s.Reads);
        Assert.Equal(1.5e-3f, polled);

        // UI reads the cache repeatedly — must not trigger another ReadHW.
        for (int i = 0; i < 5; i++) Assert.Equal(1.5e-3f, s.LastValue);
        Assert.Equal(1, s.Reads);
    }

    [Fact]
    public void Pump_LastRunning_reflects_poll_with_no_extra_read()
    {
        var p = new CountingPump(new BaseDeviceParams("TM_TP", 0, "")) { Next = [true] };

        _ = p.Status;                      // background poll
        Assert.Equal(1, p.Reads);
        Assert.True(p.LastRunning);

        int before = p.Reads;
        for (int i = 0; i < 5; i++) Assert.True(p.LastRunning);
        Assert.Equal(before, p.Reads);     // cache read is I/O-free
    }

    [Fact]
    public void Valve_LastOpen_is_populated_by_bulk_poll_and_then_is_io_free()
    {
        var plc = new FakePlcController();
        var core = new APS.SystemControl.Core.Core(Channel.CreateUnbounded<ThreadMsg>().Writer);

        plc.WriteBool(3, 10, 0, true);
        plc.WriteBool(3, 11, 0, false);

        var v1 = new TestValve(plc, new BaseDeviceParams("V1", 10, ""), (3, 10, 0));
        var v2 = new TestValve(plc, new BaseDeviceParams("V2", 11, ""), (3, 11, 0));
        var elements = new Elements();
        elements.Add("V1", v1);
        elements.Add("V2", v2);

        core.PollValvesBulk(elements);   // bus-poller wire pass populates LastOpen

        Assert.True(v1.LastOpen);
        Assert.False(v2.LastOpen);

        // The UI widget reads LastOpen — it must not issue any PLC read.
        int before = plc.BoolReads + plc.BoolBulkReads;
        for (int i = 0; i < 5; i++) { _ = v1.LastOpen; _ = v2.LastOpen; }
        Assert.Equal(before, plc.BoolReads + plc.BoolBulkReads);
    }
}
