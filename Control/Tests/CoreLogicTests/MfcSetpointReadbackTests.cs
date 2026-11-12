using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Tests.CoreLogic;
using APS.Hardware.S7;

namespace CoreLogicTests;

/// <summary>
/// <see cref="BaseMFC.Setpoint"/> is the last setpoint CONTROL wrote and stays 0 until SetFlow
/// succeeds, so after a Control restart mid-process an MFC holding 30 sccm read as "flow 30,
/// setpoint 0", i.e. gas nobody commanded. The setpoint register (<c>Address + 0</c>) is readable,
/// so the poll reports what the device holds beside the actual flow (<c>Address + 4</c>).
/// </summary>
public class MfcSetpointReadbackTests
{
    private const int Addr = 168;

    [Fact]
    public void Poll_reads_the_setpoint_the_device_actually_holds()
    {
        var fake = new FakePlcController();
        fake.WriteFloat((int)Mode.DB, Addr, 30f);       // setpoint register
        fake.WriteFloat((int)Mode.DB, Addr + 4, 30f);   // actual flow register
        var mfc = new S7200MFC(fake, "Ar1", Addr, "MFC #2") { Connected = true };

        mfc.Poll();

        Assert.Equal(30f, mfc.LastSetpoint, 3);
        Assert.Equal(30f, mfc.LastFlow, 3);
    }

    /// <summary>
    /// The commanded cache keeps its existing meaning — it is consulted by
    /// group-transition evaluation, so it must NOT start tracking the device.
    /// </summary>
    [Fact]
    public void Commanded_setpoint_stays_zero_until_Control_writes_one()
    {
        var fake = new FakePlcController();
        fake.WriteFloat((int)Mode.DB, Addr, 30f);
        fake.WriteFloat((int)Mode.DB, Addr + 4, 30f);
        var mfc = new S7200MFC(fake, "Ar1", Addr, "MFC #2") { Connected = true };

        mfc.Poll();

        Assert.Equal(0f, mfc.Setpoint);
    }

    [Fact]
    public void After_a_write_both_the_command_and_the_readback_agree()
    {
        var fake = new FakePlcController();
        var mfc = new S7200MFC(fake, "Ar1", Addr, "MFC #2") { Connected = true };

        mfc.SetFlow(12.5f);
        fake.WriteFloat((int)Mode.DB, Addr + 4, 12.5f);   // device settles
        mfc.Poll();

        Assert.Equal(12.5f, mfc.Setpoint, 3);
        Assert.Equal(12.5f, mfc.LastSetpoint, 3);
    }

    /// <summary>
    /// Same junk-floor guard ReadFlow already applies: a dead register decodes
    /// to a denormalized value and must not surface as a setpoint.
    /// </summary>
    [Fact]
    public void Denormalized_garbage_reads_as_zero_not_as_a_setpoint()
    {
        var fake = new FakePlcController();
        fake.WriteFloat((int)Mode.DB, Addr, 1e-38f);
        var mfc = new S7200MFC(fake, "Ar1", Addr, "MFC #2") { Connected = true };

        mfc.Poll();

        Assert.Equal(0f, mfc.LastSetpoint);
    }
}
