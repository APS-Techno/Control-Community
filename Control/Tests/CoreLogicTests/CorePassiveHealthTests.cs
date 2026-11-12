using System.Threading.Channels;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class CorePassiveHealthTests
{
    private static Core NewCore() => new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public void Core_exposes_non_null_diagnostics()
    {
        var c = NewCore();
        Assert.NotNull(c.Diagnostics);
    }

    [Fact]
    public void RunHardwareSelfTest_returns_non_null_report_when_idle()
    {
        var report = NewCore().RunHardwareSelfTest();
        Assert.NotNull(report);
        // No devices/PLCs loaded → runs and returns empty results (not blocked).
        Assert.True(report.Ran);
        Assert.Empty(report.Results);
    }
}
