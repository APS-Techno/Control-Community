using System.Collections.Generic;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Diagnostics;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class HwDiagnosticsTests
{
    private sealed class FakeDevice(string id, DeviceType type, DeviceProbeResult result)
        : IBaseComponent, ISelfTestable
    {
        public IBaseDeviceParams Params { get; } = new BaseDeviceParams(id, 0, id) { Type = type };
        public bool Connected { get; set; } = true;
        public DeviceProbeResult Probe() => result;
    }

    [Fact]
    public void RunSelfTest_blocked_during_deposition_runs_nothing()
    {
        var diag = new HwDiagnostics();
        var dev = new FakeDevice("PSU1", DeviceType.Power, DeviceProbeResult.Good(1));
        var report = diag.RunSelfTest([dev], new Dictionary<string, IPLCController>(),
            isDepositionActive: true);
        Assert.False(report.Ran);
        Assert.NotNull(report.BlockedReason);
        Assert.Empty(report.Results);
    }

    [Fact]
    public void RunSelfTest_idle_probes_each_self_testable_device()
    {
        var diag = new HwDiagnostics();
        var ok  = new FakeDevice("PSU1", DeviceType.Power, DeviceProbeResult.Good(2, identity: "ID"));
        var bad = new FakeDevice("GAUGE_A", DeviceType.Controller,
            DeviceProbeResult.Bad(HealthLevel.WrongProtocol, "garbage"));
        var report = diag.RunSelfTest([ok, bad], new Dictionary<string, IPLCController>(),
            isDepositionActive: false);

        Assert.True(report.Ran);
        Assert.Equal(2, report.Results.Count);
        Assert.Contains(report.Results, r => r.DeviceId == "PSU1" && r.Result.Ok);
        Assert.Contains(report.Results, r => r.DeviceId == "GAUGE_A"
            && r.Result.Level == HealthLevel.WrongProtocol);
    }

    [Fact]
    public void RunSelfTest_non_self_testable_device_reports_MonitoredOnly()
    {
        var diag = new HwDiagnostics();
        var plain = new PlainDevice("CTRL1", DeviceType.Controller);
        var report = diag.RunSelfTest([plain], new Dictionary<string, IPLCController>(),
            isDepositionActive: false);
        Assert.Single(report.Results);
        Assert.Equal(HealthLevel.MonitoredOnly, report.Results[0].Result.Level);
    }

    private sealed class PlainDevice(string id, DeviceType type) : IBaseComponent
    {
        public IBaseDeviceParams Params { get; } = new BaseDeviceParams(id, 0, id) { Type = type };
        public bool Connected { get; set; } = true;
    }

    [Fact]
    public void RunSelfTest_plc_hosted_device_is_excluded_from_results()
    {
        var diag = new HwDiagnostics();
        var fakePlc = new FakePlc();
        var hostedDevice = new HostedDevice("VALVE_ON_PLC", fakePlc);

        var plcs = new Dictionary<string, IPLCController> { { "PLC1", fakePlc } };
        var report = diag.RunSelfTest([hostedDevice], plcs, isDepositionActive: false);

        Assert.True(report.Ran);
        Assert.Single(report.Results);
        Assert.Equal("PLC1", report.Results[0].DeviceId);
        Assert.DoesNotContain(report.Results, r => r.DeviceId == "VALVE_ON_PLC");
    }

    private sealed class FakePlc : IPLCController, ISelfTestable
    {
        public bool Connected { get; private set; }
        public DeviceProbeResult ProbeResult { get; } = DeviceProbeResult.Good(5, identity: "CPU-1");

        public DeviceProbeResult Probe() => ProbeResult;

        public void Connect() => Connected = true;
        public void Connect(int address) => Connected = true;
        public void Connect(string address) => Connected = true;
        public void Connect(string address, int rack, int slot) => Connected = true;
        public void Disconnect() => Connected = false;
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

    private sealed class HostedDevice(string id, IPLCController parentPlc)
        : IBaseComponent, IBasePLCComponent
    {
        public IBaseDeviceParams Params { get; } = new BaseDeviceParams(id, 0, id) { Type = DeviceType.Valve };
        public bool Connected { get; set; } = true;
        public IPLCController PLC => parentPlc;
    }
}
