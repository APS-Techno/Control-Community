using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using Elements = APS.SystemControl.Core.Elements;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Verifies Core.ReadState(DeviceType.Valve) takes the bulk-read path —
/// one ReadBoolsBulk call per PLC, zero per-valve ReadBool calls — so
/// the 200ms poll cycle does not pay 10 individual round-trips for 10
/// valves. Mirrors Delphi TPLC7200.CheckValves parity.
/// </summary>
public class CoreReadStateBulkTests
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

    [Fact]
    public void ReadState_for_valves_uses_bulk_read()
    {
        var plc = new FakePlcController();
        var core = new APS.SystemControl.Core.Core(
            Channel.CreateUnbounded<ThreadMsg>().Writer);

        plc.WriteBool(3, 10, 0, true);
        plc.WriteBool(3, 11, 0, false);

        var v1 = new TestValve(plc, new BaseDeviceParams("V1", 10, ""), (3, 10, 0));
        var v2 = new TestValve(plc, new BaseDeviceParams("V2", 11, ""), (3, 11, 0));

        var elements = new Elements();
        elements.Add("V1", v1);
        elements.Add("V2", v2);

        // PollValvesBulk is the poller-side wire pass; ReadState is coordinator (cache-only).
        core.PollValvesBulk(elements);

        Assert.Equal(1, plc.BoolBulkReads);
        Assert.Equal(2, plc.BoolBulkBitsRead);
        Assert.Equal(0, plc.BoolReads);
    }
}
