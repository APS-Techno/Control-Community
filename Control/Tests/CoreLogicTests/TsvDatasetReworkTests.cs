using System.Globalization;
using System.Text.RegularExpressions;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Logging;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// TSV dataset contract:
///  1. Sputter PSUs log V + I only (power dropped).
///  2. A bias analyzer (IBiasSupply) logs BIAS_{id}_ch{n}_V/_I per channel, not PSU_.
///  3. A non-working MFC sanitizes denormalized garbage (~1e-38) to 0 at ReadFlow.
///  4. DataLogger writes measurements at G4 (0.1% dynamic) precision.
/// </summary>
public class TsvDatasetReworkTests
{
    private sealed class FakePsu : BasePowerSupply
    {
        public FakePsu(string id, float v, float i) : base(new BaseDeviceParams(id, 0, id))
        {
            Connected = true;
            Voltage = v;
            Current = i;
            Power = v * i;
        }
    }

    private sealed class FakeAmpereSupply(string id) : BasePowerSupply(new BaseDeviceParams(id, 0, id))
    {
        public override string CurrentUnit => "A";
    }

    private sealed class FakeBias : BasePowerSupply, IBiasSupply
    {
        private readonly float[] _v;
        private readonly float[] _i;
        public FakeBias(string id, float[] v, float[] i) : base(new BaseDeviceParams(id, 0, id))
        {
            Connected = true;
            _v = v;
            _i = i;
        }
        public int ChannelCount => _v.Length;
        public float ChannelVoltage(int channelIndex) => _v[channelIndex];
        public float ChannelCurrent(int channelIndex) => _i[channelIndex];
    }

    private sealed class FakeMfc : BaseMFC
    {
        private readonly float _raw;
        public FakeMfc(string id, float raw) : base(new BaseDeviceParams(id, 0, id))
        {
            Connected = true;
            _raw = raw;
        }
        protected override float ReadHW() => _raw;
    }

    private static HwElementCollection With(DeviceType type, string id, IBaseComponent el)
    {
        var hw = new HwElementCollection();
        hw.Item[type].Add(id, el);
        return hw;
    }

    private static readonly HashSet<string> NoSourcePsu = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> SourcePsu(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void SputterPsu_logs_voltage_and_current_but_not_power()
    {
        var hw = With(DeviceType.Power, "DC1", new FakePsu("DC1", v: 100f, i: 1.5f));
        var cfg = new LogConfig();
        var src = SourcePsu("DC1");

        var columns = cfg.BuildColumns(hw, src);
        Assert.Contains("PSU_DC1_V", columns.Keys);
        Assert.Contains("PSU_DC1_I", columns.Keys);
        Assert.DoesNotContain("PSU_DC1_P", columns.Keys);

        var values = cfg.CollectValues(hw, src);
        Assert.Equal(100f, values["PSU_DC1_V"]);
        Assert.Equal(1.5f, values["PSU_DC1_I"]);
        Assert.DoesNotContain("PSU_DC1_P", values.Keys);
    }

    [Fact]
    public void Sputter_psu_current_column_carries_the_supply_unit()
    {
        var hw = With(DeviceType.Power, "DC1", new FakePsu("DC1", v: 400f, i: 250f));
        hw.Item[DeviceType.Power].Add("ION", new FakeAmpereSupply("ION"));
        var columns = new LogConfig().BuildColumns(hw, SourcePsu("DC1", "ION"));
        Assert.Equal("mA", columns["PSU_DC1_I"]);
        Assert.Equal("A", columns["PSU_ION_I"]);
    }

    // A supply declared in HWConf but bolted to no mounted source must NOT be logged,
    // even though its COM port opened. Source set is
    // config-derived (Sources.xml), the same data the .depo.json snapshot records.
    [Fact]
    public void Unassigned_psu_gets_no_column()
    {
        var hw = new HwElementCollection();
        hw.Item[DeviceType.Power].Add("DC1",   new FakePsu("DC1", 100f, 1.5f)); // flange 2
        hw.Item[DeviceType.Power].Add("PSU-B", new FakePsu("PSU-B", 0f, 0f));   // declared, no source

        var src = SourcePsu("DC1");
        var columns = new LogConfig().BuildColumns(hw, src);
        Assert.Contains("PSU_DC1_V", columns.Keys);
        Assert.DoesNotContain("PSU_PSU-B_V", columns.Keys);
        Assert.DoesNotContain("PSU_PSU-B_I", columns.Keys);

        var values = new LogConfig().CollectValues(hw, src);
        Assert.Contains("PSU_DC1_V", values.Keys);
        Assert.DoesNotContain("PSU_PSU-B_V", values.Keys);
    }

    // A bias analyzer is logged even when no sputter PSU is assigned to a source.
    [Fact]
    public void Bias_is_logged_even_with_no_source_psus()
    {
        var hw = With(DeviceType.Power, "BIAS-1",
            new FakeBias("BIAS-1", [10f, 0f, 0f, 0f], [0.1f, 0f, 0f, 0f]));
        var columns = new LogConfig().BuildColumns(hw, NoSourcePsu);
        Assert.Contains("BIAS_BIAS-1_ch1_V", columns.Keys);
    }

    [Fact]
    public void BiasSupply_logs_all_channels_as_bias_not_psu()
    {
        var bias = new FakeBias("BIAS-1",
            v: [10f, 20f, 30f, 40f],
            i: [0.1f, 0.2f, 0.3f, 0.4f]);
        var hw = With(DeviceType.Power, "BIAS-1", bias);
        var cfg = new LogConfig();

        var columns = cfg.BuildColumns(hw, NoSourcePsu);
        for (int ch = 1; ch <= 4; ch++)
        {
            Assert.Equal("V", columns[$"BIAS_BIAS-1_ch{ch}_V"]);
            Assert.Equal("A", columns[$"BIAS_BIAS-1_ch{ch}_I"]);
        }
        Assert.DoesNotContain(columns.Keys, k => k.StartsWith("PSU_"));
        Assert.Equal(8, columns.Count);

        var values = cfg.CollectValues(hw, NoSourcePsu);
        Assert.Equal(10f, values["BIAS_BIAS-1_ch1_V"]);
        Assert.Equal(0.4f, values["BIAS_BIAS-1_ch4_I"]);
        Assert.DoesNotContain(values.Keys, k => k.StartsWith("PSU_"));
    }

    // Outcome.Means is accumulated (Core.LogDataRow) from the SAME per-row dict the
    // TSV writes (LogConfig.CollectValues), so it carries PSU V/I + per-channel bias
    // and NEVER a power (_P) key.
    [Fact]
    public void Snapshot_means_mirror_tsv_columns_not_power()
    {
        var hw = new HwElementCollection();
        hw.Item[DeviceType.Power].Add("DC1",    new FakePsu("DC1", v: 400f, i: 1.5f)); // source-assigned
        hw.Item[DeviceType.Power].Add("PSU-B",  new FakePsu("PSU-B", 0f, 0f));         // unassigned → skipped
        hw.Item[DeviceType.Power].Add("BIAS-1", new FakeBias("BIAS-1", [42f, 0f, 0f, 0f], [0.1f, 0f, 0f, 0f]));
        var cfg = new LogConfig();
        var src = SourcePsu("DC1");

        // Feed the accumulator from CollectValues over two rows, exactly as LogDataRow does.
        var actuals = new APS.SystemControl.Core.Evidence.RunActualsAccumulator();
        foreach (var kv in cfg.CollectValues(hw, src)) actuals.Sample(kv.Key, kv.Value);
        foreach (var kv in cfg.CollectValues(hw, src)) actuals.Sample(kv.Key, kv.Value);
        var means = actuals.Means();

        Assert.Equal(400.0, means["PSU_DC1_V"]);          // V, not power
        Assert.Equal(1.5,   means["PSU_DC1_I"]);
        Assert.Equal(42.0,  means["BIAS_BIAS-1_ch1_V"]);  // bias per channel
        Assert.DoesNotContain(means.Keys, k => k.EndsWith("_P")); // power dropped
        Assert.DoesNotContain(means.Keys, k => k.StartsWith("PSU_PSU-B")); // unassigned skipped
    }

    [Theory]
    [InlineData(1.4e-38f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Nonworking_mfc_sanitizes_garbage_to_zero(float garbage)
    {
        var mfc = new FakeMfc("Ar", garbage);
        Assert.Equal(0f, mfc.ReadFlow());
        Assert.Equal(0f, mfc.LastFlow);
    }

    [Fact]
    public void Working_mfc_passes_real_flow_through()
    {
        var mfc = new FakeMfc("Ar", 5.0f);
        Assert.Equal(5.0f, mfc.ReadFlow());
    }

    [Theory]
    [InlineData("tmp", "Temp", "C")]            // id token; the 'p' in "tmp" must not mean Torr
    [InlineData("C1_tmp", "C1 temperature", "C")]
    [InlineData("GAUGE-B", "Substrate Temp", "C")] // temp marker only in the description
    [InlineData("pm", "System", "Torr")]
    [InlineData("pl", "Loadlock", "Torr")]
    [InlineData("TM_pm", "TM pressure", "Torr")]
    [InlineData("thick", "Film thickness (modeled)", "nm")]
    public void Sensor_unit_is_inferred_from_id_and_description(string id, string descr, string expected)
    {
        var hw = With(DeviceType.Sensor, id, new BaseSensor(new BaseDeviceParams(id, 0, descr)) { Connected = true });
        var columns = new LogConfig().BuildColumns(hw, NoSourcePsu);
        Assert.Equal(expected, columns[$"S_{id}"]);
    }

    [Fact]
    public void Zero_film_thickness_is_logged_as_zero_not_as_an_unreported_gauge()
    {
        var hw = With(DeviceType.Sensor, "thick",
            new BaseSensor(new BaseDeviceParams("thick", 0, "Film thickness (modeled)")) { Connected = true });
        var values = new LogConfig().CollectValues(hw, NoSourcePsu);
        Assert.Equal(0d, values["S_thick"]);
    }

    [Fact]
    public void DataLogger_writes_measurements_at_g4_precision()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-tsv-g4", Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new DataLogger(dir))
            {
                // Insertion order defines column order: big value, then tiny pressure.
                var columns = new Dictionary<string, string> { ["Big"] = "V", ["Pm"] = "Torr" };
                logger.Start("run", columns);
                logger.LogRow(new Dictionary<string, double> { ["Big"] = 123.456, ["Pm"] = 1.23456e-7 });
                logger.Stop();
            }

            string[] lines = File.ReadAllLines(Directory.GetFiles(dir, "*.tsv").Single());
            // [0] headers, [1] units, [2] first data row: Time, Elapsed, Big, Pm
            string[] units = lines[1].Split('\t');
            Assert.Equal("HH:mm:ss", units[0]);             // Time is a clock, not seconds
            Assert.Equal("s", units[1]);                    // Elapsed is seconds
            string[] cells = lines[2].Split('\t');
            Assert.Equal("123.5", cells[2]);                 // G4 rounds 123.456 → 123.5
            Assert.Equal("1.235E-07", cells[3]);             // G4 keeps 4 sig-figs in sci-notation
            Assert.Matches(new Regex(@"^\d+\.\d$"), cells[1]); // Elapsed stays F1
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
