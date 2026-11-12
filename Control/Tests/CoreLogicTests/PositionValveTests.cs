using System.Xml.Linq;
using APS.Hardware.S7;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.XML;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// <see cref="S7200PositionValve"/> shows its open/closed STATE from a PLC input
/// position-sensor bit (EB area) while commanding the normal output marker (MB).
/// Cases use a valve with its sensor at EB2.7.
/// </summary>
public class PositionValveTests
{
    private const int EB = (int)Mode.EB;   // 1
    private const int MB = (int)Mode.MB;   // 3

    // GVL: command MB2.7, status EB2.7 non-inverted.
    private static S7200PositionValve Gvl(IPLCController plc) =>
        new(plc, "GVL", 2, 7, "Gate LL", statusAddress: 2, statusBit: 7, statusInvert: 0) { Connected = true };

    [Fact]
    public void Status_comes_from_the_EB_position_sensor_not_the_command_marker()
    {
        var plc = new FakePlcController();
        var gvl = Gvl(plc);

        plc.WriteBool(MB, 2, 7, true);            // command marker set...
        Assert.Equal(ValveState.Closed, gvl.State); // ...but status ignores it

        plc.WriteBool(EB, 2, 7, true);            // position sensor reports open
        Assert.Equal(ValveState.Open, gvl.State);
    }

    [Fact]
    public void Command_still_writes_the_MB_marker_unchanged()
    {
        var plc = new FakePlcController();
        var gvl = Gvl(plc);

        gvl.Open();

        Assert.True(plc.ReadBool(MB, 2, 7));      // command landed on the marker
        Assert.False(plc.ReadBool(EB, 2, 7));     // command did NOT touch the sensor image
    }

    [Fact]
    public void Inverted_sensor_reads_active_low()
    {
        var plc = new FakePlcController();
        // GVM-style: EB bit, inverted (0 = open).
        var v = new S7200PositionValve(plc, "GVx", 2, 6, "Gate", statusAddress: 2, statusBit: 6, statusInvert: 1)
            { Connected = true };

        Assert.Equal(ValveState.Open, v.State);   // EB2.6 == 0 → open (inverted)
        plc.WriteBool(EB, 2, 6, true);
        Assert.Equal(ValveState.Closed, v.State);
    }

    [Fact]
    public void Opts_out_of_bulk_read_so_inversion_is_applied_per_valve()
    {
        Assert.False(Gvl(new FakePlcController()).TryGetBulkReadDescriptor(out _));
    }

    [Fact]
    public void HWConf_row_constructs_a_working_driver_via_the_positional_loader()
    {
        // Mirrors the GVL row: address index descr statusaddr statusbit statusinvert
        var el = new XElement("valve", "GVL");
        el.SetAttributeValue("address", "2");
        el.SetAttributeValue("index", "7");
        el.SetAttributeValue("descr", "Gate LL");
        el.SetAttributeValue("statusaddr", "2");
        el.SetAttributeValue("statusbit", "7");
        el.SetAttributeValue("statusinvert", "0");

        object[] attrs = XMLHardware.Attributes(el);
        Assert.Equal(new object[] { "GVL", 2, 7, "Gate LL", 2, 7, 0 }, attrs);

        var plc = new FakePlcController();
        object[] fullArgs = [plc, .. attrs];
        var gvl = (S7200PositionValve)Activator.CreateInstance(typeof(S7200PositionValve), fullArgs)!;
        gvl.Connected = true;

        plc.WriteBool(EB, 2, 7, true);            // the EB sensor the ctor was configured with
        Assert.Equal(ValveState.Open, gvl.State);
    }
}
