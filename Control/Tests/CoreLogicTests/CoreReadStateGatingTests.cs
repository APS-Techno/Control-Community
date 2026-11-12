using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using MsgType = APS.SystemControl.Core.MsgType;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;
using ValMsgFloat = APS.SystemControl.Core.ValMsg<float>;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The 200 ms HW poll re-reads every device's state each cycle. Re-broadcasting
/// an unchanged Value message makes the GUI replay a full <c>RecalculateFlow</c>
/// + repaint of the (cluster-sized) vacuum schematic every tick, so discrete
/// device types are change-gated like valves (Core.DispatchStatusAndMessage).
/// Sensors stay un-gated so the live ScottPlot trends keep receiving every sample.
/// </summary>
public class CoreReadStateGatingTests
{
    private sealed class TestStage : PLCStage
    {
        public float Pos;
        public TestStage(IPLCController plc, IBaseDeviceParams ps) : base(plc, ps) => Connected = true;
        protected override float ReadHW() => Pos;
    }

    private sealed class TestSensor : BaseSensor
    {
        public float Val;
        public TestSensor(IBaseDeviceParams ps) : base(ps) => Connected = true;
        protected override float ReadHW() => Val;
    }

    private static int CountValueMessagesFor(ChannelReader<ThreadMsg> reader, string device)
    {
        int n = 0;
        while (reader.TryRead(out var msg))
            if (msg.Type == MsgType.Value && msg.Device == device)
                n++;
        return n;
    }

    [Fact]
    public void ReadState_for_stage_suppresses_unchanged_value()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var core = new APS.SystemControl.Core.Core(ch.Writer);

        var stage = new TestStage(new FakePlcController(), new BaseDeviceParams("VS", 0, "")) { Pos = 5.0f };
        var elements = new Elements();
        elements.Add("VS", stage);

        // Poll refreshes the cache; ReadState (coordinator) reads the cache.
        stage.Poll(); core.ReadState(elements, DeviceType.Stage);
        stage.Poll(); core.ReadState(elements, DeviceType.Stage);
        stage.Poll(); core.ReadState(elements, DeviceType.Stage);

        Assert.Equal(1, CountValueMessagesFor(ch.Reader, "VS"));
    }

    [Fact]
    public void ReadState_for_stage_emits_again_when_value_changes()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var core = new APS.SystemControl.Core.Core(ch.Writer);
        var stage = new TestStage(new FakePlcController(), new BaseDeviceParams("VS", 0, "")) { Pos = 5.0f };
        var elements = new Elements();
        elements.Add("VS", stage);

        stage.Poll(); core.ReadState(elements, DeviceType.Stage);   // emit @5
        stage.Pos = 6.0f;
        stage.Poll(); core.ReadState(elements, DeviceType.Stage);   // emit @6 (changed)
        stage.Poll(); core.ReadState(elements, DeviceType.Stage);   // suppressed @6

        Assert.Equal(2, CountValueMessagesFor(ch.Reader, "VS"));
    }

    [Fact]
    public void ReadState_for_sensor_streams_every_cycle_even_when_unchanged()
    {
        var ch = Channel.CreateUnbounded<ThreadMsg>();
        var core = new APS.SystemControl.Core.Core(ch.Writer);

        var sensor = new TestSensor(new BaseDeviceParams("pl", 0, "")) { Val = 1.0e-3f };
        var elements = new Elements();
        elements.Add("pl", sensor);

        sensor.Poll(); core.ReadState(elements, DeviceType.Sensor);
        sensor.Poll(); core.ReadState(elements, DeviceType.Sensor);
        sensor.Poll(); core.ReadState(elements, DeviceType.Sensor);

        // Sensors feed the live trend graph — every poll must reach the GUI.
        Assert.Equal(3, CountValueMessagesFor(ch.Reader, "pl"));
    }
}
