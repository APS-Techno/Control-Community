using APS.SystemControl.BaseHWClasses;
using APS.Hardware.S7;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class S7200MFCWriteFailedTests
{
    [Fact]
    public void WriteHW_RaisesWriteFailed_WhenVerifyExhausted()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.DB, 200), count: 99);
        var mfc = new S7200MFC(fake, "Ar1", address: 200, description: "Ar inlet");
        mfc.Connected = true;
        float? observed = null;
        mfc.WriteFailed += v => observed = v;
        mfc.SetFlow(7.5f);
        Assert.Equal(7.5f, observed);
    }

    [Fact]
    public void WriteHW_DoesNotRaiseWriteFailed_OnHappyPath()
    {
        var fake = new FakePlcController();
        var mfc = new S7200MFC(fake, "Ar1", address: 200, description: "Ar inlet");
        mfc.Connected = true;
        bool fired = false;
        mfc.WriteFailed += _ => fired = true;
        mfc.SetFlow(5.0f);
        Assert.False(fired);
        Assert.Equal(5.0f, fake.ReadFloat((int)Mode.DB, 200));
    }

    [Fact]
    public void SetFlow_DoesNotRaiseWriteFailed_WhenPlcDisconnected()
    {
        var fake = new FakePlcController { Connected = false };
        var mfc = new S7200MFC(fake, "Ar1", address: 200, description: "Ar inlet");
        // Drive Connected through the BaseMFC public surface; leave PLC.Connected = false
        // so PLCMFC.SetFlow returns early.
        mfc.Connected = true;

        bool fired = false;
        mfc.WriteFailed += _ => fired = true;
        mfc.SetFlow(7.5f);

        Assert.False(fired);
        Assert.Equal(0, fake.FloatWrites);   // no PLC traffic
    }

    // H-8: a verified-write failure must clear LastWriteVerified so the recipe
    // MFC op returns null (abort) instead of a read-back flow read as success.
    [Fact]
    public void LastWriteVerified_False_AfterFailedWrite()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.DB, 200), count: 99);
        var mfc = new S7200MFC(fake, "Ar1", address: 200, description: "Ar inlet");
        mfc.Connected = true;

        mfc.SetFlow(7.5f);

        Assert.False(mfc.LastWriteVerified);
    }

    [Fact]
    public void LastWriteVerified_True_OnHappyPath()
    {
        var fake = new FakePlcController();
        var mfc = new S7200MFC(fake, "Ar1", address: 200, description: "Ar inlet");
        mfc.Connected = true;

        mfc.SetFlow(5.0f);

        Assert.True(mfc.LastWriteVerified);
    }

    // U-6: a failed verified write must not cache the setpoint or raise
    // SetpointChanged — a fabricated value would otherwise feed the
    // Idle->Processing group-transition logic.
    [Fact]
    public void FailedWrite_DoesNotCacheSetpoint_OrRaiseSetpointChanged()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.DB, 200), count: 99);
        var mfc = new S7200MFC(fake, "Ar1", address: 200, description: "Ar inlet");
        mfc.Connected = true;
        int fires = 0;
        mfc.SetpointChanged += _ => fires++;

        mfc.SetFlow(7.5f);

        Assert.Equal(0f, mfc.Setpoint);
        Assert.Equal(0, fires);
    }
}
