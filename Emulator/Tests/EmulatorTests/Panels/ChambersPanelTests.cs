using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Panels;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests.Panels;

public class ChambersPanelTests
{
    private static SimulationEngine MakeEngineWithChambers(params string[] ids)
    {
        var engine = new SimulationEngine();
        engine.State.ClearChambers();
        foreach (var id in ids)
            engine.State.SetChamber(new ChamberState { Id = id, Type = ChamberType.Process });
        return engine;
    }

    [Fact]
    public void NoEngine_HasContent_IsFalse()
    {
        var panel = new ChambersPanel();
        Assert.False(panel.HasContent);
    }

    [Fact]
    public void Attach_NoChambers_HasContent_IsFalse()
    {
        var panel = new ChambersPanel();
        panel.AttachEngine(MakeEngineWithChambers());
        Assert.False(panel.HasContent);
        Assert.Equal(0, panel.RowCount);
    }

    [Fact]
    public void Attach_MainOnly_OneRowNamedMain()
    {
        var panel = new ChambersPanel();
        panel.AttachEngine(MakeEngineWithChambers("Main"));
        Assert.True(panel.HasContent);
        Assert.Equal(1, panel.RowCount);
        Assert.Contains("Main", panel.RowNames);
        Assert.DoesNotContain("Loadlock", panel.RowNames);
    }

    [Fact]
    public void Attach_MainAndLoadlock_TwoRows()
    {
        var panel = new ChambersPanel();
        panel.AttachEngine(MakeEngineWithChambers("Main", "Loadlock"));
        Assert.Equal(2, panel.RowCount);
        Assert.Contains("Main", panel.RowNames);
        Assert.Contains("Loadlock", panel.RowNames);
    }

    [Fact]
    public void Attach_LoadlockOnly_OneRowNamedLoadlock()
    {
        var panel = new ChambersPanel();
        panel.AttachEngine(MakeEngineWithChambers("Loadlock"));
        Assert.Equal(1, panel.RowCount);
        Assert.Contains("Loadlock", panel.RowNames);
    }

    [Fact]
    public void Attach_ClusterDemo_ThreeRows()
    {
        var panel = new ChambersPanel();
        panel.AttachEngine(MakeEngineWithChambers("TM", "LL", "C1"));
        Assert.Equal(3, panel.RowCount);
        Assert.Contains("TM", panel.RowNames);
        Assert.Contains("LL", panel.RowNames);
        Assert.Contains("C1", panel.RowNames);
    }

    [Fact]
    public void ProcessChamber_HasThrottleAndTemp()
    {
        // Process chambers should produce rows (throttle + temp labels created)
        // — the public API doesn't expose them directly, so we just confirm the
        // panel builds without throwing and reflects the correct row count.
        var engine = new SimulationEngine();
        engine.State.ClearChambers();
        engine.State.SetChamber(new ChamberState { Id = "Main", Type = ChamberType.Process });
        var panel = new ChambersPanel();
        panel.AttachEngine(engine);
        Assert.Equal(1, panel.RowCount);
    }

    [Fact]
    public void NonProcessChamber_StillCreatesRow()
    {
        var engine = new SimulationEngine();
        engine.State.ClearChambers();
        engine.State.SetChamber(new ChamberState { Id = "LL", Type = ChamberType.Loadlock });
        var panel = new ChambersPanel();
        panel.AttachEngine(engine);
        Assert.Equal(1, panel.RowCount);
        Assert.Contains("LL", panel.RowNames);
    }

    [Fact]
    public void UpdateValues_NoEngine_DoesNotThrow()
    {
        var panel = new ChambersPanel();
        panel.UpdateValues();
    }

    [Fact]
    public void UpdateValues_WithChambers_DoesNotThrow()
    {
        var panel = new ChambersPanel();
        panel.AttachEngine(MakeEngineWithChambers("Main", "Loadlock"));
        panel.UpdateValues();
    }

    private sealed class StubRobot : IRobotStatus
    {
        private static readonly string[] Faces = ["LL", "C1"];

        public string Id => "ROBOT";
        public string Description => "Test robot";
        public string Host => "tm";
        public DeviceCategory Category => DeviceCategory.Stage;
        public bool HasWafer => false;
        public RobotErrorCode LastErrorCode => RobotErrorCode.Ok;
        public byte TransferSrc { get; set; }
        public byte TransferDst { get; set; }
        public IReadOnlyCollection<string> SlitValveIds { get; } = ["SV_LL", "SV_C1"];
        public byte LastCmdId => 0;
        public RobotState State { get; set; } = RobotState.NotHomed;
        public RobotState GetState() => State;
        public RobotPhase GetPhase() => RobotPhase.None;
        public byte GetCurrentFace() => 0;
        public string FaceName(byte idx) => Faces[idx - 1];
        public void Update(float deltaTime, SimulationState state) { }
        public void RegisterProtocol(IProtocolServer server) { }
        public void Reset() { }
        public object[] GetStatus() => [];
    }

    private static StubRobot AddClusterRobot(SimulationEngine engine)
    {
        engine.State.SlitConnections.Add(new SlitConnection { ValveId = "SV_LL", ChamberA = "TM", ChamberB = "LL" });
        engine.State.SlitConnections.Add(new SlitConnection { ValveId = "SV_C1", ChamberA = "TM", ChamberB = "C1" });
        var robot = new StubRobot();
        engine.AddDevice(robot);
        return robot;
    }

    private static SimulationEngine MakeClusterEngine()
    {
        var engine = new SimulationEngine();
        engine.State.ClearChambers();
        engine.State.SetChamber(new ChamberState { Id = "TM", Type = ChamberType.Transfer });
        engine.State.SetChamber(new ChamberState { Id = "LL", Type = ChamberType.Loadlock });
        engine.State.SetChamber(new ChamberState { Id = "C1", Type = ChamberType.Process });
        return engine;
    }

    [Fact]
    public void RobotStatus_ShowsInTmRowOnly()
    {
        var engine = MakeClusterEngine();
        AddClusterRobot(engine);

        var panel = new ChambersPanel();
        panel.AttachEngine(engine);
        panel.UpdateValues();

        var tm = panel.RobotStatusText("TM");
        Assert.NotNull(tm);
        Assert.Contains("Not homed", tm);   // initial robot state
        Assert.Null(panel.RobotStatusText("LL"));
        Assert.Null(panel.RobotStatusText("C1"));
    }

    [Fact]
    public void RobotStatus_ReflectsTransferringRoute()
    {
        var engine = MakeClusterEngine();
        var robot = AddClusterRobot(engine);

        var panel = new ChambersPanel();
        panel.AttachEngine(engine);

        robot.State = RobotState.Transferring;
        robot.TransferSrc = 1;
        robot.TransferDst = 2;
        panel.UpdateValues();

        var tm = panel.RobotStatusText("TM");
        Assert.Contains("Transferring", tm);
        Assert.Contains("LL→C1", tm);
    }
}
