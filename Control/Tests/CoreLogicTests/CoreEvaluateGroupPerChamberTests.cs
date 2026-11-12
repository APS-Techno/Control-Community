using System.Threading.Channels;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;
using SystemState = APS.SystemControl.Core.SystemState;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Group-transition evaluation must be scoped to each chamber's OWN gas valves
/// and MFCs; otherwise any chamber's gas flow drags every other chamber into
/// <c>Processing</c> (idle→processing on <c>gasvalve anyOpen</c>) and keeps
/// them from returning to <c>Idle</c> (processing→idle on <c>gas allOff</c>).
/// </summary>
public class CoreEvaluateGroupPerChamberTests
{
    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    private sealed class FakeGasValve : BaseValve
    {
        private readonly bool _open;
        public override bool IsGasValve => true;
        public FakeGasValve(string id, string chamber, bool open)
            : base(new BaseDeviceParams(id, 0, id))
        {
            _open = open;
            Chamber = chamber;
            Connected = true;
        }
        protected override bool ReadHW() => _open;
    }

    private static Alarms ProcessingRules(string chamberId)
    {
        var a = new Alarms(chamberId);
        a.AddGroupTransition(SystemState.Idle,       SystemState.Processing, GroupKind.GasValve, GroupCondition.AnyOpen);
        a.AddGroupTransition(SystemState.Processing, SystemState.Idle,       GroupKind.Gas,      GroupCondition.AllOff);
        return a;
    }

    [Fact]
    public void Other_chambers_gas_does_not_drag_an_idle_chamber_into_processing()
    {
        var core = NewCore();
        core.chamberAlarms["C1"] = ProcessingRules("C1");
        core.chamberAlarms["C2"] = ProcessingRules("C2");
        core.SetChamberState("C1", SystemState.Idle);
        core.SetChamberState("C2", SystemState.Idle);

        // Only C2 has gas flowing. C1's own gas is closed.
        var c1v = new FakeGasValve("C1_V", "C1", open: false);
        var c2v = new FakeGasValve("C2_V", "C2", open: true);
        c1v.CacheOpen(false);
        c2v.CacheOpen(true);
        core.hwElements.Item[DeviceType.Valve].Add("C1_V", c1v);
        core.hwElements.Item[DeviceType.Valve].Add("C2_V", c2v);

        core.CheckGroupTransitionAndLog("test");

        // C2 reacts to its own open gas valve; C1 must stay idle.
        Assert.Equal(SystemState.Processing, core.GetChamberState("C2"));
        Assert.Equal(SystemState.Idle,       core.GetChamberState("C1"));
    }

    /// <summary>Actual flow (sccm) MFC fixed for a chamber; Setpoint stays 0
    /// (the crash-restart case where the commanded setpoint is lost).</summary>
    private sealed class FakeFlowingMfc : BaseMFC
    {
        private readonly float _flow;
        public FakeFlowingMfc(string id, string chamber, float lastFlow)
            : base(new BaseDeviceParams(id, 0, id))
        {
            _flow = lastFlow;
            Chamber = chamber;
            Connected = true;
        }
        protected override float ReadHW() => _flow;
    }

    [Fact]
    public void Processing_chamber_stays_processing_when_mfc_flows_but_setpoint_was_lost()
    {
        // Crash-restart mid-deposition: Setpoint is 0 (never re-commanded this
        // session) but the MFC is physically flowing, and no gas valve is present
        // to keep the group active. The poll-fed LastFlow must keep it Processing.
        var core = NewCore();
        core.chamberAlarms["C1"] = ProcessingRules("C1");
        core.SetChamberState("C1", SystemState.Processing);

        var mfc = new FakeFlowingMfc("C1_Ar", "C1", lastFlow: 5f);
        mfc.Poll();                                 // LastFlow = 5; Setpoint stays 0
        core.hwElements.Item[DeviceType.MFC].Add("C1_Ar", mfc);

        core.CheckGroupTransitionAndLog("test");

        Assert.Equal(SystemState.Processing, core.GetChamberState("C1"));
    }

    [Fact]
    public void A_processing_chamber_returns_to_idle_when_its_own_gas_is_off_even_if_another_chamber_runs()
    {
        var core = NewCore();
        core.chamberAlarms["C1"] = ProcessingRules("C1");
        core.chamberAlarms["C2"] = ProcessingRules("C2");
        core.SetChamberState("C1", SystemState.Processing);
        core.SetChamberState("C2", SystemState.Processing);

        // C1's gas is now off (e.g. vent recipe closed it); C2 still running.
        var c1v = new FakeGasValve("C1_V", "C1", open: false);
        var c2v = new FakeGasValve("C2_V", "C2", open: true);
        c1v.CacheOpen(false);
        c2v.CacheOpen(true);
        core.hwElements.Item[DeviceType.Valve].Add("C1_V", c1v);
        core.hwElements.Item[DeviceType.Valve].Add("C2_V", c2v);

        core.CheckGroupTransitionAndLog("test");

        // C1 (own gas off) → idle; C2 (own gas on) stays processing.
        Assert.Equal(SystemState.Idle,       core.GetChamberState("C1"));
        Assert.Equal(SystemState.Processing, core.GetChamberState("C2"));
    }
}
