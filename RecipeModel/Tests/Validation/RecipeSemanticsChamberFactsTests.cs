using System.Collections.Generic;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class RecipeSemanticsChamberFactsTests
{
    private static RecipeSemantics Make(
        IReadOnlyDictionary<string, ChamberAlarmLimits>? limits = null,
        IReadOnlyDictionary<string, string>? chambers = null)
    {
        var ops = new List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)>
        {
            ("pwait", "TC1", ["Controller", "f", "f"]),
            ("mfc", null, ["MFC", "f"]),
        };
        var devices = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Controller"] = new[] { "TC1" },
            ["MFC"] = new[] { "Ar1" },
        };
        return RecipeSemantics.Build(ops, devices, chamberLimits: limits, deviceChamber: chambers);
    }

    [Fact]
    public void Bound_device_of_a_bound_op_is_the_declared_device()
    {
        var s = Make();

        Assert.Equal("TC1", s.BoundDeviceOf("pwait"));
        Assert.True(s.HasBoundDevice("pwait"));
    }

    [Fact]
    public void Bound_device_of_an_unbound_or_unknown_op_is_null()
    {
        var s = Make();

        Assert.Null(s.BoundDeviceOf("mfc"));
        Assert.Null(s.BoundDeviceOf("nope"));
    }

    [Fact]
    public void Chamber_of_a_device_is_case_insensitive_and_null_when_unknown()
    {
        var s = Make(chambers: new Dictionary<string, string> { ["TC1"] = "Main" });

        Assert.Equal("Main", s.ChamberOf("tc1"));
        Assert.Null(s.ChamberOf("Ar1"));
    }

    [Fact]
    public void Limits_of_a_chamber_expose_the_ceilings_delay_and_guard()
    {
        var limits = new Dictionary<string, ChamberAlarmLimits>
        {
            ["C1"] = new(1e-4f, 0, "C1_A1 open", 2e-1f),
        };
        var s = Make(limits: limits);

        var c1 = s.LimitsOf("c1")!;
        Assert.Equal(1e-4f, c1.IdleCeilingTorr);
        Assert.Equal(0d, c1.IdleDelaySeconds);
        Assert.Equal("C1_A1 open", c1.IdleUnless);
        Assert.Equal(2e-1f, c1.ProcessingCeilingTorr);
        Assert.Null(s.LimitsOf("Loadlock"));
    }

    [Fact]
    public void Semantics_built_without_chamber_inputs_answers_null()
    {
        var s = Make();

        Assert.Null(s.ChamberOf("TC1"));
        Assert.Null(s.LimitsOf("Main"));
    }
}
