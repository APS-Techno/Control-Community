using System.Xml.Linq;
using APS.Hardware.S7;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.XML;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// <see cref="S7200PanelValve"/> models a valve where command =
/// a momentary pulse on a PAIRED open/close V-flag (DB1), status = an EB position
/// limit sensor. Cases use FL: open V1.4, close V1.5, status EB I2.3.
/// </summary>
public class S7200PanelValveTests
{
    private const int DB = (int)Mode.DB;   // 0
    private const int EB = (int)Mode.EB;   // 1

    // FL: open V1.4, close V1.5, status EB2.3 non-inverted.
    private static S7200PanelValve Fl(IPLCController plc) =>
        new(plc, "FL", 2, 3, "Fore line",
            openAddress: 1, openBit: 4, closeAddress: 1, closeBit: 5, statusInvert: 0)
        { Connected = true };

    [Fact]
    public void Status_comes_from_the_EB_position_sensor()
    {
        var plc = new FakePlcController();
        var fl = Fl(plc);

        Assert.Equal(ValveState.Closed, fl.State);   // EB2.3 == 0
        plc.WriteBool(EB, 2, 3, true);               // open-limit sensor trips
        Assert.Equal(ValveState.Open, fl.State);
    }

    [Fact]
    public void Open_pulses_the_open_flag_high_then_low()
    {
        var plc = new FakePlcController();
        var fl = Fl(plc);
        plc.BoolWriteLog.Clear();

        fl.Open();

        // exactly V1.4 ← true, then V1.4 ← false; close flag untouched
        var writes = plc.BoolWriteLog.FindAll(w => w.mode == DB);
        Assert.Equal(new[] { (DB, 1, 4, true), (DB, 1, 4, false) }, writes);
    }

    [Fact]
    public void Close_pulses_the_close_flag_high_then_low()
    {
        var plc = new FakePlcController();
        var fl = Fl(plc);
        plc.BoolWriteLog.Clear();

        fl.Close();

        var writes = plc.BoolWriteLog.FindAll(w => w.mode == DB);
        Assert.Equal(new[] { (DB, 1, 5, true), (DB, 1, 5, false) }, writes);
    }

    [Fact]
    public void Inverted_status_reads_the_closed_limit_active_low()
    {
        var plc = new FakePlcController();
        // GVM-style: status EB2.6 inverted (closed-limit; 0 = open).
        var gvm = new S7200PanelValve(plc, "GVM", 2, 6, "Gate Main",
            openAddress: 1, openBit: 6, closeAddress: 1, closeBit: 7, statusInvert: 1)
        { Connected = true };

        Assert.Equal(ValveState.Open, gvm.State);    // EB2.6 == 0 → open
        plc.WriteBool(EB, 2, 6, true);
        Assert.Equal(ValveState.Closed, gvm.State);
    }

    [Fact]
    public void Declares_a_settle_timeout_because_the_pulse_returns_before_the_valve_moves()
    {
        // The recipe valve op waits for the position sensor for up to this long;
        // a plain marker valve actuates on the write and declares no wait.
        var plc = new FakePlcController();
        Assert.Equal(S7200PanelValve.SettleMs, Fl(plc).SettleTimeoutMs);
        Assert.InRange(Fl(plc).SettleTimeoutMs, 5_000, 60_000);
        Assert.Equal(0, new S7200Valve(plc, "A1", 2, 1, "Air release").SettleTimeoutMs);
        Assert.Equal(0, new S7200InputValve(plc, "GVM", 2, 6, "Gate Main").SettleTimeoutMs);
    }

    [Fact]
    public void Opts_out_of_bulk_read_so_inversion_is_applied_per_valve()
    {
        Assert.False(Fl(new FakePlcController()).TryGetBulkReadDescriptor(out _));
    }

    [Fact]
    public void HWConf_row_constructs_a_working_driver_via_the_positional_loader()
    {
        // Mirrors the FL row: address index descr openaddr openbit closeaddr closebit statusinvert
        var el = new XElement("valve", "FL");
        el.SetAttributeValue("address", "2");
        el.SetAttributeValue("index", "3");
        el.SetAttributeValue("descr", "Fore line");
        el.SetAttributeValue("openaddr", "1");
        el.SetAttributeValue("openbit", "4");
        el.SetAttributeValue("closeaddr", "1");
        el.SetAttributeValue("closebit", "5");
        el.SetAttributeValue("statusinvert", "0");

        object[] attrs = XMLHardware.Attributes(el);
        Assert.Equal(new object[] { "FL", 2, 3, "Fore line", 1, 4, 1, 5, 0 }, attrs);

        var plc = new FakePlcController();
        object[] fullArgs = [plc, .. attrs];
        var fl = (S7200PanelValve)Activator.CreateInstance(typeof(S7200PanelValve), fullArgs)!;
        fl.Connected = true;

        plc.WriteBool(EB, 2, 3, true);            // the EB sensor the ctor was configured with
        Assert.Equal(ValveState.Open, fl.State);
    }
}
