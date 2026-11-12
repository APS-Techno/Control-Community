using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace CoreLogicTests;

public class AlarmConfigLoaderDeviceStatusTests : IDisposable
{
    private readonly string _tmp;

    public AlarmConfigLoaderDeviceStatusTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "alarm-loader-devstatus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { /* best-effort */ }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Test stand-in for an alarm-addressable device. Mirrors the surface
    /// S7200TurboPump exposes (IBaseComponent + IStatusBitNamed) without
    /// pulling in the PLC + Sharp7 deps.
    /// </summary>
    private sealed class FakeStatusDevice : IBaseComponent, IStatusBitNamed
    {
        public IBaseDeviceParams Params { get; }
        public bool Connected { get; set; }
        public IReadOnlyList<string> StatusBitNames { get; }

        public FakeStatusDevice(string id, params string[] bitNames)
        {
            Params = new BaseDeviceParams(id, 0, id);
            StatusBitNames = bitNames;
        }
    }

    private sealed class FakePlainDevice : IBaseComponent
    {
        public IBaseDeviceParams Params { get; }
        public bool Connected { get; set; }
        public FakePlainDevice(string id)
        {
            Params = new BaseDeviceParams(id, 0, id);
        }
    }

    [Fact]
    public void Loads_DeviceStatusAlarm_FromXml()
    {
        string path = WriteFile("Alarms.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm device="TP" status="alarm" state="processing">Turbo alarm!</alarm>
            </alarms>
            """);
        var tp = new FakeStatusDevice("TP", "accel", "normal", "rotating", "alarm");
        var result = AlarmConfigLoader.Load(path, deviceResolver: id => id == "TP" ? tp : null);
        var alarms = result.ChamberAlarms[CoreConstants.DefaultChamberId];

        Assert.Single(alarms.DeviceStatusRules);
        var rule = alarms.DeviceStatusRules[0];
        Assert.Equal("TP", rule.DeviceId);
        Assert.Equal(3, rule.StatusBit);
        Assert.Equal(SystemState.Processing, rule.State);
        Assert.Equal("Turbo alarm!", rule.Message);
    }

    [Fact]
    public void WarnsAndSkips_OnUnknownStatusName()
    {
        string path = WriteFile("Alarms.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm device="TP" status="bogus" state="processing">x</alarm>
            </alarms>
            """);
        var tp = new FakeStatusDevice("TP", "accel", "normal", "rotating", "alarm");
        var result = AlarmConfigLoader.Load(path, deviceResolver: id => id == "TP" ? tp : null);
        Assert.Single(result.Warnings);
        Assert.Contains("bogus", result.Warnings[0]);
        Assert.Contains("accel", result.Warnings[0]);
        Assert.Empty(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }

    [Fact]
    public void WarnsAndSkips_OnDeviceWithoutStatusBitNames()
    {
        string path = WriteFile("Alarms.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm device="TP" status="alarm" state="processing">x</alarm>
            </alarms>
            """);
        var tp = new FakePlainDevice("TP");
        var result = AlarmConfigLoader.Load(path, deviceResolver: id => id == "TP" ? tp : null);
        Assert.Single(result.Warnings);
        Assert.Contains("named status bits", result.Warnings[0]);
        Assert.Empty(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }

    [Fact]
    public void WarnsAndSkips_OnUnknownDeviceId()
    {
        string path = WriteFile("Alarms.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm device="ZZ" status="alarm" state="processing">x</alarm>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path, deviceResolver: _ => null);
        Assert.Single(result.Warnings);
        Assert.Contains("ZZ", result.Warnings[0]);
        Assert.Empty(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }

    [Fact]
    public void WarnsAndSkips_OnAmbiguousAttributes()
    {
        string path = WriteFile("Alarms.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <alarm state="processing">x</alarm>
            </alarms>
            """);
        var result = AlarmConfigLoader.Load(path, deviceResolver: _ => null);
        Assert.Single(result.Warnings);
        Assert.Contains("sensor", result.Warnings[0]);
        Assert.Contains("device", result.Warnings[0]);
        Assert.Empty(result.ChamberAlarms[CoreConstants.DefaultChamberId].Rules);
    }
}
