using APS.SystemEmulator.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public class RobotChamberResolverTests
{
    private static List<SlitConnection> ClusterDemoSlits() => new()
    {
        new SlitConnection { ValveId = "SV_LL", ChamberA = "TM", ChamberB = "LL" },
        new SlitConnection { ValveId = "SV_C1", ChamberA = "TM", ChamberB = "C1" },
    };

    private static Dictionary<string, ChamberType> ClusterDemoChambers() => new()
    {
        ["TM"] = ChamberType.Transfer,
        ["LL"] = ChamberType.Loadlock,
        ["C1"] = ChamberType.Process,
    };

    [Fact]
    public void Intersection_OfTwoSlits_IsTheTransferModule()
    {
        var home = RobotChamberResolver.Resolve(
            new[] { "SV_LL", "SV_C1" }, ClusterDemoSlits(), ClusterDemoChambers());
        Assert.Equal("TM", home);
    }

    [Fact]
    public void SlitValveIdMatch_IsCaseInsensitive()
    {
        var home = RobotChamberResolver.Resolve(
            new[] { "sv_ll", "sv_c1" }, ClusterDemoSlits(), ClusterDemoChambers());
        Assert.Equal("TM", home);
    }

    [Fact]
    public void SingleSlit_AmbiguousIntersection_FallsBackToSingleTransferChamber()
    {
        // One slit → intersection {TM, LL} is ambiguous → fall back to the lone Transfer chamber.
        var home = RobotChamberResolver.Resolve(
            new[] { "SV_LL" }, ClusterDemoSlits(), ClusterDemoChambers());
        Assert.Equal("TM", home);
    }

    [Fact]
    public void NoSlitsAndNoTransferChamber_ReturnsNull()
    {
        var chambers = new Dictionary<string, ChamberType> { ["Main"] = ChamberType.Process };
        var home = RobotChamberResolver.Resolve(
            Array.Empty<string>(), new List<SlitConnection>(), chambers);
        Assert.Null(home);
    }
}
