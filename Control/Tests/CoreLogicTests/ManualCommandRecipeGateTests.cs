using System;
using System.Collections.Generic;
using System.Reflection;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Forms;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Read-only-during-deposition gates (code audit 2026-08-29):
///   C31 — manual PSU panel commands are rejected while a recipe runs; Power-OFF stays
///        always-allowed as the kill switch.
///   C28 — frmSources re-checks the gate on Save: a recipe can start from the agent/remote
///        channels while the dialog is open.
///   C29 — the mounted-target edit gate lives in the Commercial production module.
/// </summary>
public class ManualCommandRecipeGateTests
{
    private sealed class RecordingPsu : BasePowerSupply
    {
        public readonly List<(string name, float value)> Setpoints = new();
        public bool OutputOnCalled, OutputOffCalled;

        public RecordingPsu(string id) : base(new BaseDeviceParams(id, 0, "psu")) { Connected = true; }
        public override void ApplySetpoint(string parameter, float value) => Setpoints.Add((parameter, value));
        public override void OutputOn() => OutputOnCalled = true;
        public override void OutputOff() => OutputOffCalled = true;
    }

    private static void Dispatch(CoreThread thread, string psuId, params object[] args) =>
        typeof(CoreThread).GetMethod("ProcessPowerCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(thread, [new ParamMsg(MsgType.Command, DeviceType.Power, psuId, args)]);

    [Fact]
    public void Manual_psu_commands_are_rejected_while_a_recipe_runs_except_output_off()
    {
        var thread = new CoreThread(new ChannelPool());
        var psu = new RecordingPsu("RF1");
        thread.Core.hwElements.Item[DeviceType.Power].Add("RF1", psu);
        Assert.True(thread.Core.TryBeginRecipeSlot());

        Dispatch(thread, "RF1", "SetPower", 100f);
        Dispatch(thread, "RF1", "OutputOn");
        Dispatch(thread, "RF1", "OutputOff");            // the kill switch stays live

        Assert.Empty(psu.Setpoints);
        Assert.False(psu.OutputOnCalled);
        Assert.True(psu.OutputOffCalled);
    }

    [Fact]
    public void Manual_psu_commands_work_when_no_recipe_runs()
    {
        var thread = new CoreThread(new ChannelPool());
        var psu = new RecordingPsu("RF1");
        thread.Core.hwElements.Item[DeviceType.Power].Add("RF1", psu);

        Dispatch(thread, "RF1", "SetPower", 100f);

        Assert.Contains(("Power", 100f), psu.Setpoints);
    }

    // ProcessSingleWorkerCommandCore's default branch is the GUI's "click a valve on the scheme"
    // route; C31 applies to it too.

    private sealed class RecordingValve : BaseValve
    {
        public readonly List<bool> Commands = new();
        public RecordingValve(string id) : base(new BaseDeviceParams(id, 0, "valve")) { Connected = true; }
        public override void Open() { Commands.Add(true); }
        public override void Close() { Commands.Add(false); }
    }

    private sealed class RecordingMfc : BaseMFC
    {
        public readonly List<float> Flows = new();
        private float _flow;
        public RecordingMfc(string id) : base(new BaseDeviceParams(id, 0, "mfc")) { Connected = true; }
        protected override void WriteHW(float v) { Flows.Add(v); _flow = v; }
        protected override float ReadHW() => _flow;
    }

    private sealed class RecordingPump : BasePump
    {
        public readonly List<bool> Commands = new();
        private bool _on;
        public RecordingPump(string id) : base(new BaseDeviceParams(id, 0, "pump")) { Connected = true; }
        public override bool IsRunning => _on;
        protected override void WriteHW(bool state) { Commands.Add(state); _on = state; }
    }

    /// <summary>Reads back the position it was written, so an OpStage move "arrives" at once.
    /// Without that, a mutation that removed the gate would sit in StageArrivalWaiter for its
    /// full 120 s ceiling and HANG the mutation run instead of failing it.</summary>
    private sealed class RecordingStage : BaseStage
    {
        public int StopCalls;
        public readonly List<float> Moves = new();
        private float _pos;
        public RecordingStage(string id) : base(new BaseDeviceParams(id, 0, "stage")) { Connected = true; }
        public override void Stop() => StopCalls++;
        protected override void WriteHW(float p) { Moves.Add(p); _pos = p; }
        protected override float ReadHW() => _pos;
    }

    /// <summary>Drives the manual worker path exactly as frmMain's scheme clicks do:
    /// Target=Worker, Operation=&lt;op name&gt;, Args=[deviceType, deviceId, ...values].</summary>
    private static void DispatchWorker(CoreThread thread, string operation, params object[] args) =>
        ((Task)typeof(CoreThread)
            .GetMethod("ProcessSingleWorkerCommandCore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(thread, [new ParamMsg(MsgType.Command, DeviceType.Worker, operation, args)])!)
            .GetAwaiter().GetResult();

    private static void Register(APS.SystemControl.Core.Core core, DeviceType type, IBaseComponent device)
    {
        if (!core.hwElements.Item.TryGetValue(type, out var elements))
        {
            elements = new();
            core.hwElements.Item[type] = elements;
        }
        elements.Add(device.Params.ID, device);
    }

    private static (CoreThread Thread, RecordingValve Valve, RecordingMfc Mfc, RecordingPump Pump, RecordingStage Stage)
        NewWorkerThread()
    {
        var thread = new CoreThread(new ChannelPool());
        thread.Core.LoadEmbeddedOperations();
        var valve = new RecordingValve("V1");
        var mfc = new RecordingMfc("Ar");
        var pump = new RecordingPump("SP1");
        var stage = new RecordingStage("HS");
        Register(thread.Core, DeviceType.Valve, valve);
        Register(thread.Core, DeviceType.MFC, mfc);
        Register(thread.Core, DeviceType.Pump, pump);
        Register(thread.Core, DeviceType.Stage, stage);
        return (thread, valve, mfc, pump, stage);
    }

    /// <summary>All four device kinds this branch dispatches, not just the valve: closing the Ar
    /// valve, zeroing the MFC, stopping the backing pump and driving the stage mid-deposition are
    /// each independently destructive.
    /// </summary>
    [Fact]
    public void Manual_gas_pumping_and_motion_commands_are_rejected_while_a_recipe_runs()
    {
        var (thread, valve, mfc, pump, stage) = NewWorkerThread();
        Assert.True(thread.Core.TryBeginRecipeSlot());
        try
        {
            DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", false);
            DispatchWorker(thread, "MFC", DeviceType.MFC, "Ar", 0f);
            DispatchWorker(thread, "Pump", DeviceType.Pump, "SP1", false);
            DispatchWorker(thread, "Stage", DeviceType.Stage, "HS", 25f);

            Assert.Empty(valve.Commands);
            Assert.Empty(mfc.Flows);
            Assert.Empty(pump.Commands);
            Assert.Empty(stage.Moves);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void Manual_gas_pumping_and_motion_commands_work_when_no_recipe_runs()
    {
        var (thread, valve, mfc, pump, stage) = NewWorkerThread();

        DispatchWorker(thread, "Valve", DeviceType.Valve, "V1", true);
        DispatchWorker(thread, "MFC", DeviceType.MFC, "Ar", 0f);
        DispatchWorker(thread, "Pump", DeviceType.Pump, "SP1", true);
        DispatchWorker(thread, "Stage", DeviceType.Stage, "HS", 25f);

        Assert.Equal([true], valve.Commands);
        Assert.Equal([0f], mfc.Flows);
        Assert.Equal([true], pump.Commands);
        Assert.Equal([25f], stage.Moves);
    }

    /// <summary>StageStop is this path's kill switch — the OutputOff of manual motion. Gating it
    /// would leave an operator watching a stage travel into something with no way to stop it
    /// short of aborting the whole run.</summary>
    [Fact]
    public void StageStop_stays_live_during_a_run_as_the_motion_kill_switch()
    {
        var (thread, _, _, _, stage) = NewWorkerThread();
        Assert.True(thread.Core.TryBeginRecipeSlot());
        try
        {
            DispatchWorker(thread, "StageStop", DeviceType.Stage, "HS");
            Assert.Equal(1, stage.StopCalls);
        }
        finally { thread.Core.EndRecipeSlot(); }
    }

    [Fact]
    public void Sources_save_is_blocked_when_a_deposition_became_active_after_open()
    {
        var core = new CoreThread(new ChannelPool()).Core;
        var sm = new APS.SystemControl.Core.Classes.SourceManager(6);
        using var frm = new frmSources(sm, core: core);

        Assert.Null(frm.ValidateSave());

        Assert.True(core.TryBeginRecipeSlot());          // recipe starts while dialog is open
        var err = frm.ValidateSave();
        Assert.NotNull(err);
        Assert.Contains("deposition", err, StringComparison.OrdinalIgnoreCase);
    }
}
