using System;
using System.Collections.Generic;
using System.Linq;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class SourceTargetValidationTests
{
    private static readonly string[] Sources = ["Si", "C", "Ion", "Ru", "Co"];

    private static RecipeValidator Make(
        IReadOnlyDictionary<string, string>? sourceToPsu = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? mfcGateValves = null,
        bool rotationOperational = true,
        bool boundPwait = true,
        string[]? sources = null)
    {
        var ops = new List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)>
        {
            ("valve", null, ["Valve", "b"]),
            ("mfc", null, ["MFC", "f"]),
            ("power", null, ["Power", "b"]),
            ("set", null, ["Power", "s", "f"]),
            ("pwait", boundPwait ? "CTRL#1" : null, boundPwait ? ["Controller", "f", "f"] : ["Sensor", "f", "f"]),
        };
        var devices = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Valve"] = new[] { "V1", "V2", "V3", "M2" },
            ["MFC"] = new[] { "Ar1", "N21" },
            ["Power"] = new[] { "PSU_B", "RF601" },
            ["Controller"] = new[] { "CTRL#1" },
            ["Sensor"] = new[] { "pm" },
        };
        var semantics = RecipeSemantics.Build(ops, devices,
            sourceToPsu: sourceToPsu, mfcGateValves: mfcGateValves, rotationOperational: rotationOperational);
        return new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: semantics.AllDeviceNames,
            knownSourceNames: sources ?? Sources,
            semantics: semantics);
    }

    private static readonly IReadOnlyDictionary<string, string> CToPsuB =
        new Dictionary<string, string> { ["C"] = "PSU_B", ["Ru"] = "RF601" };

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Gates =
        new Dictionary<string, IReadOnlyCollection<string>> { ["Ar1"] = new[] { "V2", "V3" } };

    private static List<RecipeError> With(RecipeValidator v, string text, string fragment) =>
        v.Validate(text).Where(e => e.Message.Contains(fragment, StringComparison.Ordinal)).ToList();

    [Fact]
    public void Deposit_UnknownSource_ReportsInstalledSources()
    {
        var v = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: new[] { "M1" },
            knownSourceNames: new[] { "Si", "W_1", "W_2" });
        var errors = v.Validate("deposit W_9 10 Home");
        Assert.Contains(errors, e => e.Message.Contains("Installed sources", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(errors, e => e.Message.Contains("materials", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deposit_InvalidRetractPositionFalse_ReportsError()
    {
        var e = Assert.Single(With(Make(), "deposit Ru 418 false", "retract position"));

        Assert.Equal(RecipeErrorSeverity.Error, e.Severity);
        Assert.Equal(1, e.Line);
        Assert.Equal(
            "Unknown retract position 'false' for 'deposit'. Third argument parks the shutter and must be " +
            "Home, 0, a flange number, or an installed source: C, Co, Ion, Ru, Si", e.Message);
    }

    [Theory]
    [InlineData("deposit Ru 30 Home")]
    [InlineData("deposit Ru 30 home")]
    [InlineData("deposit Ru 30 0")]
    [InlineData("deposit Ru 30 1")]
    [InlineData("deposit Ru 30 C")]
    [InlineData("deposit Ru 30 $ret")]
    [InlineData("var ret Home\ndeposit Ru 30 $ret")]
    [InlineData("var ret C\ndeposit Ru 30 ret")]
    [InlineData("deposit Ru 30")]
    public void Deposit_ValidOrRuntimeOnlyRetractPositions_Pass(string recipe)
    {
        Assert.Empty(With(Make(), recipe, "retract position"));
    }

    [Theory]
    [InlineData("var ret false\ndeposit Ru 30 $ret", "false")]
    [InlineData("var ret banana\ndeposit Ru 30 ret", "banana")]
    [InlineData("deposit Ru 30 banana", "banana")]
    public void Deposit_LiteralBackedUnknownRetract_ReportsError(string recipe, string value)
    {
        var e = Assert.Single(With(Make(), recipe, "retract position"));
        Assert.Contains($"'{value}'", e.Message);
    }

    [Fact]
    public void Deposit_ClusterTwoArgumentForm_NeedsNoRetract()
    {
        var v = Make(sources: ["Cu", "Ti"]);
        Assert.Empty(With(v, "deposit Cu 60", "retract"));
    }

    [Fact]
    public void Message_AlwaysWarnsAboutTheBlockingDialog()
    {
        var w = Assert.Single(With(Make(), "valve V1 open\nmessage \"load sample\"", "blocking operator dialog"));

        Assert.Equal(RecipeErrorSeverity.Warning, w.Severity);
        Assert.Equal(2, w.Line);
        Assert.Equal(
            "'message' opens a blocking operator dialog on the Control UI that halts unattended " +
            "execution and survives abort_recipe", w.Message);
    }

    [Theory]
    [InlineData("rotate 20", true)]
    [InlineData("rotate 0", false)]
    [InlineData("rotate $speed", false)]
    [InlineData("rotation on", true)]
    [InlineData("rotation true 20", true)]
    [InlineData("rotation off", false)]
    public void Rotation_OnMachineWithoutOperationalRotation_WarnsOnlyWhenItWouldTurn(string recipe, bool warns)
    {
        var found = With(Make(rotationOperational: false), recipe, "is not operational on this machine");

        Assert.Equal(warns, found.Count == 1);
        if (warns)
        {
            Assert.Equal(RecipeErrorSeverity.Warning, found[0].Severity);
            Assert.Contains($"('{recipe.Split(' ')[0]}')", found[0].Message);
        }
    }

    [Fact]
    public void Rotation_OnMachineWithOperationalRotation_DoesNotWarn()
    {
        Assert.Empty(With(Make(rotationOperational: true), "rotate 20\nrotation on", "not operational"));
    }

    [Fact]
    public void Deposit_AfterWarmupWithoutPowerOn_ReportsWarning()
    {
        var w = Assert.Single(With(Make(CToPsuB),
            "set PSU_B Current 300\nwarmup 60 10\ndeposit C 133 Home", "not energized"));

        Assert.Equal(RecipeErrorSeverity.Warning, w.Severity);
        Assert.Equal(3, w.Line);
        Assert.Equal(
            "deposit 'C' runs while its power supply 'PSU_B' is not energized ('warmup' does not leave " +
            "power on; add 'power PSU_B on' before depositing)", w.Message);
    }

    [Theory]
    [InlineData("power PSU_B on\ndeposit C 133 Home")]
    [InlineData("power PSU_B true\ndeposit C 133 Home")]
    [InlineData("power PSU_B on\nwarmup 60 10\ndeposit C 133 Home")]
    [InlineData("power PSU_B on\nloop 30\ndeposit C 133 Home\nendloop")]
    [InlineData("power PSU_B on\nisolate\ndeposit C 133 Home")]
    [InlineData("power $psu on\ndeposit C 133 Home")]
    public void Deposit_WithSupplyEnergized_DoesNotWarn(string recipe)
    {
        Assert.Empty(With(Make(CToPsuB), recipe, "not energized"));
    }

    [Theory]
    [InlineData("power PSU_B on\npower PSU_B off\ndeposit C 133 Home")]
    [InlineData("power PSU_B on\npower PSU_B false\ndeposit C 133 Home")]
    [InlineData("power RF601 on\ndeposit C 133 Home")]
    [InlineData("deposit C 133 Home")]
    public void Deposit_WithSupplyNotEnergized_Warns(string recipe)
    {
        Assert.Single(With(Make(CToPsuB), recipe, "'PSU_B' is not energized"));
    }

    [Fact]
    public void Deposit_OfSourceWithNoSupplyMapping_IsNotJudged()
    {
        Assert.Empty(With(Make(CToPsuB), "deposit Co 30 Home", "not energized"));
        Assert.Empty(With(Make(), "deposit C 30 Home", "not energized"));
    }

    [Fact]
    public void Valve_ClosedWithoutEverOpening_Warns()
    {
        var w = Assert.Single(With(Make(), "valve V1 open\nvalve V1 close\nvalve M2 close", "never opened"));

        Assert.Equal(RecipeErrorSeverity.Warning, w.Severity);
        Assert.Equal(3, w.Line);
        Assert.Equal("valve 'M2' is closed but was never opened in this recipe", w.Message);
    }

    [Theory]
    [InlineData("valve M2 open\nvalve M2 close")]
    [InlineData("valve M2 true\nvalve M2 false")]
    [InlineData("valve M2 open\nisolate\nvalve M2 closed")]
    [InlineData("valve $v close")]
    public void Valve_ClosedAfterOpen_DoesNotWarn(string recipe)
    {
        Assert.Empty(With(Make(), recipe, "never opened"));
    }

    [Fact]
    public void Mfc_WithFlowAndNoGateValveOpen_WarnsOncePerMfc()
    {
        var found = With(Make(mfcGateValves: Gates),
            "mfc Ar1 40\npwait 10mT 60\ndeposit Ru 30 Home\npwait 10mT 60", "inlet gate valves");

        var w = Assert.Single(found);
        Assert.Equal(RecipeErrorSeverity.Warning, w.Severity);
        Assert.Equal(
            "mfc 'Ar1' has non-zero flow but none of its inlet gate valves (V2, V3) are open", w.Message);
    }

    [Theory]
    [InlineData("valve V2 open\nmfc Ar1 40\npwait 10mT 60")]
    [InlineData("mfc Ar1 40\nvalve V3 open\npwait 10mT 60")]
    [InlineData("valve V3 open\nvalve V2 close\nmfc Ar1 40\npwait 10mT 60")]
    [InlineData("mfc Ar1 40\nmfc Ar1 0\npwait 10mT 60")]
    [InlineData("mfc Ar1 40\nisolate\npwait 10mT 60")]
    [InlineData("mfc N21 40\npwait 10mT 60")]
    [InlineData("mfc Ar1 40")]
    [InlineData("valve V2 open\nloop 3\nmfc Ar1 40\ndeposit Ru 30 Home\nendloop")]
    public void Mfc_RoutedOrIdleOrUngated_DoesNotWarn(string recipe)
    {
        Assert.Empty(With(Make(mfcGateValves: Gates), recipe, "inlet gate valves"));
    }

    [Fact]
    public void Mfc_ClosedGateValveAfterOpen_WarnsAtNextWait()
    {
        Assert.Single(With(Make(mfcGateValves: Gates),
            "valve V2 open\nmfc Ar1 40\nvalve V2 close\npwait 10mT 60", "inlet gate valves"));
    }

    [Fact]
    public void Pwait_FinalAtGaugeFloor_Warns()
    {
        var w = Assert.Single(With(Make(), "var Pbase 0.01mT\npwait Pbase 10", "gauge floor"));

        Assert.Equal(RecipeErrorSeverity.Warning, w.Severity);
        Assert.Equal(
            "final pwait target '0.01mT' is at or below the gauge floor (0.01 mTorr): the runtime opens " +
            "the throttle fully and then waits for the gauge to read at or below the floor, so the step " +
            "fails with a timeout if the chamber cannot pump that low; use target 0 ('pwait 0 1') to " +
            "open the throttle and continue at once", w.Message);
    }

    [Theory]
    [InlineData("pwait 0.01mT 10", true)]
    [InlineData("pwait 0.005mT 10", true)]
    [InlineData("pwait 0.0104mT 10", false)]
    [InlineData("pwait 0.00001 10", true)]
    [InlineData("pwait 0.02mT 10", false)]
    [InlineData("pwait 50mT 10", false)]
    [InlineData("pwait 0 1", false)]
    [InlineData("pwait 0.01mT 10\npwait 0 1", false)]
    [InlineData("pwait 0.01mT 10\npwait 5mT 10", false)]
    public void Pwait_FloorCheckAppliesToTheLastPwaitOnly(string recipe, bool warns)
    {
        Assert.Equal(warns, With(Make(), recipe, "gauge floor").Count == 1);
    }

    [Fact]
    public void Pwait_FloorCheckNeedsABoundController()
    {
        Assert.Empty(With(Make(boundPwait: false), "pwait pm 0.01mT 10", "gauge floor"));
    }

    [Fact]
    public void Ac2StyleRecipe_SurfacesEachHardwareDefectWithItsLine()
    {
        const string recipe =
            "var Pbase 0.01mT\n" +
            "valve V1 open\n" +
            "mfc Ar1 40\n" +
            "rotate 20\n" +
            "message \"check shutter\"\n" +
            "set PSU_B Current 300\n" +
            "warmup 60 10\n" +
            "deposit C 133 false\n" +
            "valve M2 close\n" +
            "pwait Pbase 10";
        var errors = Make(CToPsuB, Gates, rotationOperational: false).Validate(recipe);

        int LineOf(string fragment) => Assert.Single(errors, e => e.Message.Contains(fragment)).Line;
        Assert.Equal(4, LineOf("is not operational"));
        Assert.Equal(5, LineOf("blocking operator dialog"));
        Assert.Equal(8, LineOf("retract position"));
        Assert.Equal(8, LineOf("not energized"));
        Assert.Equal(9, LineOf("never opened"));
        Assert.Equal(10, LineOf("gauge floor"));
        Assert.Equal(8, LineOf("inlet gate valves"));
    }
}
