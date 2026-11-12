using System.Diagnostics;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using APS.Workers.Generic;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class GenericOperationsTests : IDisposable
{
    private const string SourcesXml = """
        <sources>
          <source flange="3" shutterId="Sh3"><material>Cu</material></source>
          <source flange="5" name="Mo_A" shutterId="Sh5"><material>Mo</material></source>
        </sources>
        """;

    private readonly string _configDir;
    private readonly List<string> _log = [];

    public GenericOperationsTests()
    {
        Operations.ClearStaticState();
        Operations.PwaitSettleSamples = 2;
        Operations.PwaitCommandDelayMs = 1;
        Operations.PwaitPollMs = 5;
        Operations.DriveSettleMs = 1;
        Operations.DrivePollMs = 1;
        Operations.RobotPollMs = 5;
        Operations.LogReporter = (source, message) => _log.Add($"{source}: {message}");

        _configDir = Path.Combine(Path.GetTempPath(), "aps-genops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
        File.WriteAllText(Path.Combine(_configDir, "Sources.xml"), SourcesXml);
    }

    public void Dispose()
    {
        Operations.ClearStaticState();
        Directory.Delete(_configDir, recursive: true);
    }

    private sealed class StubPsu : BasePowerSupply
    {
        private readonly bool _throwOnSetpoint;

        public StubPsu(bool responding, bool throwOnSetpoint)
            : base(new BaseDeviceParams("PSU1", 0, "stub"))
        {
            Connected = true;
            IsResponding = responding;
            RampEnabled = false;
            _throwOnSetpoint = throwOnSetpoint;
        }

        public override void ApplySetpoint(string parameter, float value)
        {
            if (_throwOnSetpoint) throw new ArgumentException(parameter);
        }
    }

    private class StubController : BaseController
    {
        public readonly List<object[]> Commands = [];

        public StubController(string id) : base(new BaseDeviceParams(id, 0, "stub")) => Connected = true;

        public override void Command(object[] args) => Commands.Add(args);
    }

    private sealed class StubGaugeController : StubController, IPressureController
    {
        public StubGaugeController(string id) : base(id) { }

        public float PressureTorr { get; set; }
        public float ValvePositionPercent { get; set; }
        public bool FollowsTarget { get; init; } = true;

        public override void Command(object[] args)
        {
            base.Command(args);
            if (FollowsTarget && args[0] is "SetPressure") PressureTorr = Convert.ToSingle(args[1]);
        }
    }

    private sealed class StubDrive : BaseStepperDrive
    {
        private readonly Stopwatch _clock;

        public StubDrive(string id, Stopwatch clock) : base(new BaseDeviceParams(id, 0, "stub"))
        {
            _clock = clock;
            Connected = true;
        }

        public readonly List<(string Move, long Ms)> Moves = [];

        public override void MoveToFlange(int flangeNum)
        {
            Moves.Add(($"F{flangeNum}", _clock.ElapsedMilliseconds));
            CurrentPosition = flangeNum * 100;
        }

        public override void Home()
        {
            Moves.Add(("Home", _clock.ElapsedMilliseconds));
            CurrentPosition = 0;
        }
    }

    private sealed class StubValve : BaseValve
    {
        private readonly Stopwatch _clock;
        private bool _open;

        public StubValve(string id, Stopwatch clock) : base(new BaseDeviceParams(id, 0, "stub"))
        {
            _clock = clock;
            Connected = true;
        }

        public readonly List<(string Event, long Ms)> Events = [];

        protected override bool ReadHW() => _open;

        protected override void WriteHW(bool newstate)
        {
            _open = newstate;
            Events.Add((newstate ? "open" : "close", _clock.ElapsedMilliseconds));
        }
    }

    private sealed class StubRobot : BaseRobot
    {
        private int _remaining;
        private byte _nextId = 1;

        public StubRobot() : base(new BaseDeviceParams("ROBOT", 0, "stub"))
        {
            Connected = true;
            State = RobotState.Idle;
            Homed = true;
            Faces = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase) { ["LL"] = 1, ["C1"] = 2 };
        }

        public int TransferringPolls = 3;
        public int PollCount;
        public readonly List<(byte Src, byte Dst)> Transfers = [];

        public override void Poll()
        {
            PollCount++;
            if (State == RobotState.Transferring && --_remaining <= 0)
                State = RobotState.Idle;
            LastCmdId = LastIssuedCmdId;
        }

        public override void Transfer(byte srcFace, byte dstFace)
        {
            Transfers.Add((srcFace, dstFace));
            State = RobotState.Transferring;
            _remaining = TransferringPolls;
            LastIssuedCmdId = _nextId++;
        }

        public override void Home() { }
        public override void Abort() { }
    }

    [Fact]
    public async Task PowerOff_UnresponsiveSupply_DoesNotThrow()
    {
        var psu = new StubPsu(responding: false, throwOnSetpoint: false);
        await Operations.Power(psu, false, CancellationToken.None);
    }

    [Fact]
    public async Task Set_UnknownParameter_ReturnsNull()
    {
        var psu = new StubPsu(responding: true, throwOnSetpoint: true);
        Assert.Null(await Operations.Set(psu, "Bogus", 1f, CancellationToken.None));
    }

    [Fact]
    public void ExposesNoBiasOrScanOperations()
    {
        var names = typeof(Operations).GetMethods().Select(m => m.Name).ToHashSet();
        foreach (var banned in new[] { "Bias", "BiasArb", "Scan", "ScanR" })
            Assert.DoesNotContain(banned, names);
    }

    [Fact]
    public async Task Pwait_Bound_ReachesTarget_ReturnsNonNull()
    {
        var controller = new StubGaugeController("TC1");

        var result = await Operations.Pwait(controller, "pm", 0.005f, 1f, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0.005f, Assert.IsType<float>(result));
        var command = Assert.Single(controller.Commands);
        Assert.Equal("SetPressure", command[0]);
        Assert.Equal(0.005f, command[1]);
    }

    [Fact]
    public async Task Pwait_Timeout_ReturnsNull()
    {
        var controller = new StubGaugeController("TC1") { FollowsTarget = false, PressureTorr = 1f };

        Assert.Null(await Operations.Pwait(controller, "pm", 0.005f, 0.05f, CancellationToken.None));
        Assert.Contains(_log, line => line.Contains("did not converge"));
    }

    [Fact]
    public async Task Pwait_Unbound_UsesThrottleResolverForSensor()
    {
        var throttle = new StubController("C1_TV");
        Operations.ThrottleResolver = sensor => sensor == "C1_pm" ? throttle : null;
        Operations.PressureReader = sensor => sensor == "C1_pm" ? 0.003f : null;

        var result = await Operations.Pwait("C1_pm", 0.003f, 1f, CancellationToken.None);

        Assert.NotNull(result);
        var command = Assert.Single(throttle.Commands);
        Assert.Equal("SetPressure", command[0]);
        Assert.Equal(0.003f, command[1]);

        Assert.Null(await Operations.Pwait("C9_pm", 0.003f, 1f, CancellationToken.None));
        Assert.Single(throttle.Commands);
    }

    [Fact]
    public async Task Pwait_ZeroTarget_ParksAndReturns()
    {
        var controller = new StubGaugeController("TC1") { PressureTorr = 1f };

        var result = await Operations.Pwait(controller, "pm", 0f, 1f, CancellationToken.None);

        Assert.NotNull(result);
        var command = Assert.Single(controller.Commands);
        Assert.Single(command);
        Assert.Equal("Open", command[0]);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("9")]
    [InlineData("-1")]
    [InlineData("Gr")]
    public async Task Holder_UnknownToken_ReturnsNull_NoMove(string token)
    {
        Operations.Init(_configDir);
        var drive = new StubDrive("Holder", Stopwatch.StartNew());

        Assert.Null(await Operations.Holder(drive, token, CancellationToken.None));
        Assert.Empty(drive.Moves);
    }

    [Fact]
    public async Task Holder_SourceName_MovesToItsFlange()
    {
        Operations.Init(_configDir);
        var drive = new StubDrive("Holder", Stopwatch.StartNew());

        Assert.NotNull(await Operations.Holder(drive, "Mo_A", CancellationToken.None));
        Assert.NotNull(await Operations.Holder(drive, "2", CancellationToken.None));
        Assert.NotNull(await Operations.Holder(drive, "Home", CancellationToken.None));

        Assert.Equal(new[] { "F5", "F2", "Home" }, drive.Moves.Select(m => m.Move).ToArray());
    }

    [Fact]
    public async Task Deposit3_MovesDwellsRetracts()
    {
        Operations.Init(_configDir);
        var clock = Stopwatch.StartNew();
        var holder = new StubDrive("Holder", clock);
        var shutter = new StubDrive("Shutter", clock);
        Operations.DriveResolver = id => id == "Shutter" ? shutter : null;
        (int Flange, double Seconds)? reported = null;
        Operations.DepositReporter = (flange, seconds) => reported = (flange, seconds);

        var result = await Operations.Deposit(holder, "Mo_A", 0.15f, "Home", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new[] { "F5" }, holder.Moves.Select(m => m.Move).ToArray());
        Assert.Equal(new[] { "F5", "Home" }, shutter.Moves.Select(m => m.Move).ToArray());
        Assert.True(shutter.Moves[1].Ms - shutter.Moves[0].Ms >= 100);
        Assert.Equal(5, reported!.Value.Flange);
        Assert.Equal(0.15, reported.Value.Seconds, 3);
    }

    [Fact]
    public async Task Deposit3_GuardVeto_ReturnsNull_ShutterNotMoved()
    {
        Operations.Init(_configDir);
        var clock = Stopwatch.StartNew();
        var holder = new StubDrive("Holder", clock);
        var shutter = new StubDrive("Shutter", clock);
        Operations.DriveResolver = id => id == "Shutter" ? shutter : null;
        Operations.DepositGuard = (_, _, _, _) => "blocked";

        Assert.Null(await Operations.Deposit(holder, "Mo_A", 0.05f, "Home", CancellationToken.None));
        Assert.Empty(shutter.Moves);
        Assert.Contains(_log, line => line.Contains("blocked"));
    }

    [Fact]
    public async Task Deposit2_OpensAndClosesShutterValve()
    {
        Operations.Init(_configDir);
        var valve = new StubValve("Sh5", Stopwatch.StartNew());
        Operations.ShutterResolver = id => id == "Sh5" ? valve : null;

        var result = await Operations.Deposit("Mo_A", 0.15f, CancellationToken.None);

        Assert.Equal(false, result);
        Assert.Equal(new[] { "open", "close" }, valve.Events.Select(e => e.Event).ToArray());
        Assert.True(valve.Events[1].Ms - valve.Events[0].Ms >= 100);
    }

    [Fact]
    public async Task Deposit2_UninstalledSource_ReturnsNull()
    {
        Operations.Init(_configDir);
        var valve = new StubValve("Sh5", Stopwatch.StartNew());
        Operations.ShutterResolver = _ => valve;

        Assert.Null(await Operations.Deposit("Gr", 0.05f, CancellationToken.None));
        Assert.Empty(valve.Events);
    }

    [Fact]
    public async Task Transfer_UnknownFace_ReturnsNull()
    {
        var robot = new StubRobot();

        Assert.Null(await Operations.Transfer(robot, "LL", "Nowhere", CancellationToken.None));
        Assert.Empty(robot.Transfers);
    }

    [Fact]
    public async Task Transfer_WaitsForIdle()
    {
        var robot = new StubRobot { TransferringPolls = 3 };

        var result = await Operations.Transfer(robot, "LL", "C1", CancellationToken.None);

        Assert.Equal(true, result);
        Assert.Equal(RobotState.Idle, robot.State);
        Assert.Equal(((byte)1, (byte)2), Assert.Single(robot.Transfers));
        Assert.True(robot.PollCount >= 4);
    }
}
