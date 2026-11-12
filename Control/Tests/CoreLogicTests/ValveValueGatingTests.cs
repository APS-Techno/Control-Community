using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Verifies Core change-gates the valve open/closed Value message: the 200 ms
/// poll re-reads the same state every cycle, but only genuine transitions are
/// forwarded to the GUI. Re-broadcasting an unchanged state made the valve
/// widget replay its colour-transition animation + glow pulse on every tick
/// (the "valve can't decide its colour, drifts red->black" shimmer).
/// </summary>
public class ValveValueGatingTests
{
    private sealed class TestValve : PlcValve
    {
        private readonly (int mode, int address, int byteIndex) _desc;
        public TestValve(IPLCController plc, IBaseDeviceParams ps,
                         (int, int, int) desc) : base(plc, ps)
        {
            _desc = desc;
            Connected = true;
        }
        public override bool TryGetBulkReadDescriptor(
            out (int mode, int address, int byteIndex) descriptor)
        {
            descriptor = _desc;
            return true;
        }
    }

    private static List<ThreadMsg> Drain(ChannelReader<ThreadMsg> r)
    {
        var list = new List<ThreadMsg>();
        while (r.TryRead(out var m)) list.Add(m!);
        return list;
    }

    [Fact]
    public void Unchanged_valve_state_is_not_rebroadcast_every_poll()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var plc = new FakePlcController();
        var core = new APS.SystemControl.Core.Core(ch.Writer);

        plc.WriteBool(3, 10, 0, true);
        var v1 = new TestValve(plc, new BaseDeviceParams("V1", 10, ""), (3, 10, 0));
        var elements = new Elements();
        elements.Add("V1", v1);

        // PollValvesBulk (poller side) seeds the cache; ReadState (coordinator) reads it.
        // First poll: status + value both forwarded.
        core.PollValvesBulk(elements);
        core.ReadState(elements, DeviceType.Valve);
        var first = Drain(ch.Reader);
        Assert.Single(first, m => m.Type == APS.SystemControl.Core.MsgType.Value && m.Device == "V1");
        Assert.Single(first, m => m.Type == APS.SystemControl.Core.MsgType.Status && m.Device == "V1");

        // Two more polls with identical state: no further Value messages.
        core.PollValvesBulk(elements);
        core.ReadState(elements, DeviceType.Valve);
        core.PollValvesBulk(elements);
        core.ReadState(elements, DeviceType.Valve);
        var rest = Drain(ch.Reader);
        Assert.DoesNotContain(rest, m => m.Type == APS.SystemControl.Core.MsgType.Value && m.Device == "V1");
    }

    [Fact]
    public void Valve_state_change_is_forwarded()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var plc = new FakePlcController();
        var core = new APS.SystemControl.Core.Core(ch.Writer);

        plc.WriteBool(3, 10, 0, false);
        var v1 = new TestValve(plc, new BaseDeviceParams("V1", 10, ""), (3, 10, 0));
        var elements = new Elements();
        elements.Add("V1", v1);

        // PollValvesBulk (poller side) seeds the cache; ReadState (coordinator) reads it.
        core.PollValvesBulk(elements);
        core.ReadState(elements, DeviceType.Valve);   // closed — primes the cache
        Drain(ch.Reader);

        plc.WriteBool(3, 10, 0, true);                // now open
        core.PollValvesBulk(elements);                // update the cache from wire
        core.ReadState(elements, DeviceType.Valve);
        var afterChange = Drain(ch.Reader);

        var values = afterChange
            .Where(m => m.Type == APS.SystemControl.Core.MsgType.Value && m.Device == "V1")
            .ToList();
        Assert.Single(values);
        Assert.True(((APS.SystemControl.Core.ValMsg<bool>)values[0]).Value);
    }
}
