using System;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Cluster;
using Xunit;

namespace CoreLogicTests;

public class HwReadTests
{
    [Fact]
    public void Safe_double_returns_value_when_read_succeeds()
    {
        Assert.Equal(1.5, HwRead.Safe(() => (double?)1.5));
    }

    [Fact]
    public void Safe_double_returns_null_on_plc_comm_failure()
    {
        Assert.Null(HwRead.Safe(() => throw new PlcCommException(1, "read")));
    }

    [Fact]
    public void Safe_double_does_not_swallow_other_exceptions()
    {
        Assert.Throws<InvalidOperationException>(
            () => HwRead.Safe(() => throw new InvalidOperationException()));
    }

    [Fact]
    public void Safe_generic_returns_value_when_read_succeeds()
    {
        Assert.Equal((true, false), HwRead.Safe(() => (true, false), (false, false)));
    }

    [Fact]
    public void Safe_generic_returns_fallback_on_plc_comm_failure()
    {
        Assert.False(HwRead.Safe<bool>(() => throw new PlcCommException(2, "status"), fallback: false));
        Assert.Equal((false, false),
            HwRead.Safe(() => throw new PlcCommException(3, "pumps"), (false, false)));
    }

    [Fact]
    public void Safe_generic_does_not_swallow_other_exceptions()
    {
        Assert.Throws<InvalidOperationException>(
            () => HwRead.Safe<bool>(() => throw new InvalidOperationException(), fallback: false));
    }
}
