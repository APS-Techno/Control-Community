using System.Drawing;
using APS.SystemControl.Controls.Vacuum;

namespace CoreLogicTests;

/// <summary>
/// Chambers must be looked up BEFORE devices in <c>ResolvePipeEndpoint</c>: the device
/// Name-suffix fallback (<c>EndsWith("_" + componentId)</c>), meant for sensor lookups,
/// otherwise shadows a chamber ID that ends a device name (chamber <c>C1</c> vs device
/// <c>MAG_C1</c>, chamber <c>LL</c> vs device <c>SV_LL</c>).
/// </summary>
public class VacuumSchematicAnchorResolutionTests
{
    private static VacuumSchematic MakeSchematic()
    {
        var s = new VacuumSchematic();
        s.SetTestCanvasArea(new Rectangle(0, 0, 1000, 1000));
        return s;
    }

    [Fact]
    public void ResolvePipeEndpoint_ChamberC1_WinsOverDeviceMAG_C1()
    {
        using var s = MakeSchematic();

        // Chamber "C1" with a top_1 anchor at (50%, 25%) of canvas.
        s.AddTestChamberAnchor("C1", "top_1", new PointF(50f, 25f));

        // Device whose WinForms .Name ends with "_C1" (like MAG_C1).
        s.AddTestDeviceWithName("vc_MAG_C1");

        var pt = s.ResolveEndpointForTest("C1:top_1");

        Assert.NotNull(pt);
        // Chamber anchor at (50%, 25%) on a 1000x1000 canvas → (500, 250).
        Assert.Equal(500f, pt!.Value.X, 1);
        Assert.Equal(250f, pt.Value.Y, 1);
    }

    [Fact]
    public void ResolvePipeEndpoint_ChamberLL_WinsOverDeviceSV_LL()
    {
        using var s = MakeSchematic();

        // Chamber "LL" with bottom_1 anchor at (45%, 15%).
        s.AddTestChamberAnchor("LL", "bottom_1", new PointF(45f, 15f));

        // Device SV_LL — name ends with "_LL".
        s.AddTestDeviceWithName("vc_SV_LL");

        var pt = s.ResolveEndpointForTest("LL:bottom_1");

        Assert.NotNull(pt);
        Assert.Equal(450f, pt!.Value.X, 1);
        Assert.Equal(150f, pt.Value.Y, 1);
    }

    [Fact]
    public void ResolvePipeEndpoint_NoChamber_FallsBackToNameSuffixForSensors()
    {
        using var s = MakeSchematic();

        // No chamber declared. A device's Name-suffix should still resolve,
        // preserving sensor lookups that depend on this fallback path.
        s.AddTestDeviceWithName("vc_sensor_C1");

        var pt = s.ResolveEndpointForTest("C1:center");

        Assert.NotNull(pt);
        // The Label control was Located at (900, 900). The fallback returns
        // its center, which is (905, 905) given the 10x10 size.
        Assert.Equal(905f, pt!.Value.X, 1);
        Assert.Equal(905f, pt.Value.Y, 1);
    }

    [Fact]
    public void ResolvePipeEndpoint_UnknownSpec_ReturnsNull()
    {
        using var s = MakeSchematic();

        Assert.Null(s.ResolveEndpointForTest("Nonexistent:top_1"));
        Assert.Null(s.ResolveEndpointForTest("malformed-no-colon"));
    }
}
