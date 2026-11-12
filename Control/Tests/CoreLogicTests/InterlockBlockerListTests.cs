using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

/// <summary>
/// <see cref="InterlockCond.Blockers"/> names every blocking device in one denial, so the operator
/// does not discover them one retry at a time.
/// </summary>
public class InterlockBlockerListTests
{
    [Fact]
    public void Message_lists_every_blocking_device_when_the_set_is_known()
    {
        var cond = new InterlockCond
        {
            Target = new BaseValve(new BaseDeviceParams("V5", 2, "Inlet")),
            Blockers = "V5, M3, GVM",
        };

        Assert.Equal("V5, M3, GVM", Core.InterLockToString(cond));
    }

    [Fact]
    public void Message_falls_back_to_the_conditions_own_device()
    {
        var cond = new InterlockCond
        {
            Target = new BaseValve(new BaseDeviceParams("GVM", 2, "Gate Main")),
        };

        Assert.Equal("GVM", Core.InterLockToString(cond));
    }

    [Fact]
    public void A_single_blocker_reads_as_just_that_device()
    {
        var cond = new InterlockCond
        {
            Target = new BaseValve(new BaseDeviceParams("V5", 2, "Inlet")),
            Blockers = "V5",
        };

        Assert.Equal("V5", Core.InterLockToString(cond));
    }
}
