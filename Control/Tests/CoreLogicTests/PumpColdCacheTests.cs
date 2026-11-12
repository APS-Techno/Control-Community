using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using MsgType = APS.SystemControl.Core.MsgType;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The coordinator reads
/// <see cref="BasePump.LastStatus"/> (cache), which starts as an empty array and
/// stays empty until the pump's bus poller runs its first <see cref="BasePump.Poll"/>.
/// On the startup race the coordinator must NOT forward that empty array — the
/// turbo-pump widget (<c>TurboPumpControl.SetState</c>) throws on any array that
/// is not exactly its status length. Mirrors the Controller case's empty guard.
/// </summary>
public class PumpColdCacheTests
{
    private sealed class TestPump : BasePump
    {
        public bool[] Hw = [];
        public TestPump() : base(new BaseDeviceParams("TP", 0, "")) { Connected = true; }
        protected override bool[] ReadStatus() => Hw;
    }

    private static int CountValueMessagesFor(ChannelReader<ThreadMsg> reader, string device)
    {
        int n = 0;
        while (reader.TryRead(out var msg))
            if (msg.Type == MsgType.Value && msg.Device == device) n++;
        return n;
    }

    [Fact]
    public void ReadState_does_not_forward_empty_cold_pump_status()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var core = new APS.SystemControl.Core.Core(ch.Writer);
        var pump = new TestPump();          // never polled -> LastStatus == []
        var elements = new Elements();
        elements.Add("TP", pump);

        core.ReadState(elements, DeviceType.Pump);

        Assert.Equal(0, CountValueMessagesFor(ch.Reader, "TP"));
    }
}
