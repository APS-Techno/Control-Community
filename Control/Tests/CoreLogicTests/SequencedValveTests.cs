using System.Xml.Linq;
using APS.Hardware.S7;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.XML;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// <see cref="S7200SequencedValve"/> is one config-driven driver for every valve
/// whose M-bit the firmware recomputes (PP, FL, per-chamber cluster equivalents).
/// It writes the configured V-memory command flag (DB1) and reads status back from
/// the M-bit. These tests pin the command target, polarity, and status path using
/// two valves as cases — no per-valve subclass involved.
/// </summary>
public class SequencedValveTests
{
    private const int DB = (int)Mode.DB;
    private const int MB = (int)Mode.MB;

    // address/index = status M-bit; cmdaddr/cmdbit = DB command flag; cmdinvert: 1 = active-low.
    private static S7200SequencedValve Fl(IPLCController plc) =>
        new(plc, "FL", 2, 6, "Fore line", cmdAddress: 15, cmdBit: 6, cmdInvert: 0) { Connected = true };

    private static S7200SequencedValve Pp(IPLCController plc) =>
        new(plc, "PP", 2, 4, "Pre-pump", cmdAddress: 5, cmdBit: 3, cmdInvert: 1) { Connected = true };

    [Fact]
    public void Open_sets_the_command_flag_and_never_touches_the_M_bit()
    {
        var plc = new FakePlcController();
        Fl(plc).Open();

        Assert.True(plc.ReadBool(DB, 15, 6));    // V15.6 commanded open
        Assert.False(plc.ReadBool(MB, 2, 6));    // M2.6 left to the firmware
    }

    [Fact]
    public void Close_clears_the_command_flag()
    {
        var plc = new FakePlcController();
        var fl = Fl(plc);
        fl.Open();
        fl.Close();

        Assert.False(plc.ReadBool(DB, 15, 6));
    }

    [Fact]
    public void Status_is_read_back_from_the_status_M_bit()
    {
        var plc = new FakePlcController();
        var fl = Fl(plc);
        plc.WriteBool(MB, 2, 6, true);           // firmware/emulator resolved FL open onto M2.6

        Assert.Equal(ValveState.Open, fl.State);
    }

    [Fact]
    public void Exposes_the_status_M_bit_as_its_bulk_read_descriptor()
    {
        var fl = Fl(new FakePlcController());

        Assert.True(fl.TryGetBulkReadDescriptor(out var d));
        Assert.Equal((MB, 2, 6), d);
    }

    [Fact]
    public void Inverted_close_sets_the_flag_and_open_clears_it()
    {
        var plc = new FakePlcController();
        var pp = Pp(plc);

        pp.Close();
        Assert.True(plc.ReadBool(DB, 5, 3));     // force-close asserted
        Assert.False(plc.ReadBool(MB, 2, 4));    // M2.4 untouched

        pp.Open();
        Assert.False(plc.ReadBool(DB, 5, 3));    // released → firmware may open
    }

    [Fact]
    public void Failed_command_write_raises_WriteFailed_and_clears_verified_flag()
    {
        var plc = new FakePlcController();
        var fl = Fl(plc);
        bool? failedTarget = null;
        fl.WriteFailed += t => failedTarget = t;

        plc.DropNextWrites((DB, 15), 5);          // every verify attempt drops
        fl.Open();

        Assert.False(fl.LastWriteVerified);
        Assert.True(failedTarget);                // target state that failed = OPEN
    }

    // The cmd attributes must reach the ctor in document order, exactly as Core.LoadHardware does it.

    [Fact]
    public void HWConf_row_constructs_a_working_driver_via_the_positional_loader()
    {
        var el = new XElement("valve", "PP");
        el.SetAttributeValue("address", "2");
        el.SetAttributeValue("index", "4");
        el.SetAttributeValue("descr", "Pre-pump");
        el.SetAttributeValue("cmdaddr", "5");
        el.SetAttributeValue("cmdbit", "3");
        el.SetAttributeValue("cmdinvert", "1");

        // Core builds the positional arg array (id first, attrs in doc order).
        object[] attrs = XMLHardware.Attributes(el);
        Assert.Equal(new object[] { "PP", 2, 4, "Pre-pump", 5, 3, 1 }, attrs);

        // Core prepends the PLC and Activator-constructs the configured hwtype.
        var plc = new FakePlcController();
        object[] fullArgs = [plc, .. attrs];
        var pp = (S7200SequencedValve)Activator.CreateInstance(typeof(S7200SequencedValve), fullArgs)!;
        pp.Connected = true;

        // Close hits V5.3 (inverted) only if the cmd attributes reached the ctor.
        pp.Close();
        Assert.True(plc.ReadBool(DB, 5, 3));
        Assert.False(plc.ReadBool(MB, 2, 4));
    }
}
