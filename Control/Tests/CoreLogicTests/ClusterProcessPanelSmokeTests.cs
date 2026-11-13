using System.Collections.Generic;
using System.Linq;
using APS.ProcessPanel.Contracts;
using APS.Controls.Generic;
using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

[Collection("ProcessPanelSpec")]
public class ClusterProcessPanelSmokeTests
{
    private static readonly ProcessPanelSpec DefaultSpec = new(
    [
        new("C1·Ar", ReadingGroup.MassFlow, "C1·Ar", "F1", "sccm"),
        new("C2·Ar", ReadingGroup.MassFlow, "C2·Ar", "F1", "sccm"),
        new("C1·pm", ReadingGroup.Pressure, "C1·pm", "E1", "Torr"),
        new("C2·pm", ReadingGroup.Pressure, "C2·pm", "E1", "Torr"),
        new("LL·pm", ReadingGroup.Pressure, "LL·pm", "E1", "Torr"),
        new("TM·pm", ReadingGroup.Pressure, "TM·pm", "E1", "Torr"),
        new("C1·tmp", ReadingGroup.Substrate, "C1·tmp", "F0", "°C"),
        new("C1·RF", ReadingGroup.Power, "C1·RF", "F0", "W"),
    ], [], [], []);

    private static ClusterProcessPanel NewPanel(ProcessPanelSpec? spec = null)
    {
        var saved = ProcessPanelSpec.Current;
        ProcessPanelSpec.Current = spec ?? DefaultSpec;
        try { return new ClusterProcessPanel(); }
        finally { ProcessPanelSpec.Current = saved; }
    }

    [Fact]
    public void Ctor_NoSpec_BuildsWithoutRows()
    {
        using var panel = NewPanel(ProcessPanelSpec.Empty);
        Assert.Empty(panel.ReadingIds);
    }

    [Fact]
    public void Ctor_WithSpec_RegistersEverySpecReading()
    {
        using var panel = NewPanel();
        Assert.Equal(DefaultSpec.Readings.Select(r => r.Id).Order(), panel.ReadingIds.Order());
    }

    [Fact]
    public void UpdateCluster_ChamberWithoutRow_DoesNotThrow()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(600, 600);
        var snap = new ClusterSnapshot(
            [new ClusterChamberInfo("C9", 60, SystemStateCode.Processing, 1e-3, false,
                Flows: [new ClusterReadingInfo("C9_Ar", "Ar", 5.0)])],
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false));
        Assert.Null(Record.Exception(() => panel.UpdateCluster(snap)));
    }

    [Fact]
    public void UpdateCluster_EmptyChamberList_DoesNotThrow()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(600, 600);
        var snap = new ClusterSnapshot([],
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false));
        Assert.Null(Record.Exception(() =>
        {
            panel.UpdateCluster(snap);
            using var bmp = new System.Drawing.Bitmap(600, 600);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, 600, 600));
        }));
    }

    [Fact]
    public void UpdateCluster_SingleChamber_PaintsWithoutThrow()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(600, 600);
        var snap = new ClusterSnapshot(
            [new ClusterChamberInfo("C1", 0, SystemStateCode.Processing, 1e-3, true)],
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, 1e-6, true, true));
        Assert.Null(Record.Exception(() =>
        {
            panel.UpdateCluster(snap);
            using var bmp = new System.Drawing.Bitmap(600, 600);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, 600, 600));
        }));
    }

    [Fact]
    public void Construct_and_update_does_not_throw()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(600, 600);

        var snap = new ClusterSnapshot(
            new[]
            {
                new ClusterChamberInfo("LL", 0,   SystemStateCode.Idle,       2.3e-7, false),
                new ClusterChamberInfo("C1", 60,  SystemStateCode.Processing, 1.2e-3, true),
                new ClusterChamberInfo("C2", 120, SystemStateCode.Idle,       null,   false),
            },
            new ClusterRobotInfo(RobotStateCode.Transferring, 1, 64, 200, true, false, 5e-7, true, true));

        var ex = Record.Exception(() => panel.UpdateCluster(snap));
        Assert.Null(ex);
    }

    [Fact]
    public void Implements_panel_contracts()
    {
        using var panel = NewPanel();
        Assert.IsAssignableFrom<IClusterProcessPanel>(panel);
        Assert.IsAssignableFrom<APS.SystemControl.Controls.General.IBaseCustomPanel>(panel);
    }

    [Fact]
    public void Paint_does_not_throw()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(600, 600);

        var snap = new ClusterSnapshot(
            new[]
            {
                new ClusterChamberInfo("LL", 0,   SystemStateCode.Idle,       2.3e-7, false),
                new ClusterChamberInfo("C1", 60,  SystemStateCode.Processing, 1.2e-3, true),
                new ClusterChamberInfo("C2", 120, SystemStateCode.Stopped,    null,   false),
                new ClusterChamberInfo("C3", 180, SystemStateCode.Maintenance,5.0e-8, true),
                new ClusterChamberInfo("C4", 240, SystemStateCode.Unknown,    3.1e-3, false),
            },
            new ClusterRobotInfo(RobotStateCode.Transferring, 2, 96, 220, true, false, 5e-7, true, true));

        panel.UpdateCluster(snap);

        var ex = Record.Exception(() =>
        {
            using var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
        });
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(0)]    // arm straight up
    [InlineData(64)]   // ~90deg, right
    [InlineData(128)]  // ~180deg, down
    [InlineData(191)]  // ~270deg, left
    public void Retracted_arm_and_error_paint_at_axis_angles(byte angleNorm)
    {
        // The arm-length clamp ray/box-intersects the central chip,
        // which is brittle at axis-aligned angles (potential divide-by-zero) and
        // when fully retracted (ExtendNorm 0). Error-active exercises the red path.
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(600, 600);

        var snap = new ClusterSnapshot(
            new[] { new ClusterChamberInfo("LL", 0, SystemStateCode.Idle, 2.3e-7, false) },
            new ClusterRobotInfo(RobotStateCode.Error, 1, angleNorm, 0, false, true, null, true, true));

        panel.UpdateCluster(snap);

        var ex = Record.Exception(() =>
        {
            using var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Paint_with_no_data_does_not_throw()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(400, 300);
        // No UpdateCluster call -> snapshot is null -> placeholder paint path.
        var ex = Record.Exception(() =>
        {
            using var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Rich_snapshot_paints_with_chrome()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(1000, 700);

        // Two MFCs (Ar + N2) and two RF supplies (RF + RF B) exercise the
        // secondary-device render path on both the tile and the reading rows.
        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 1.2e-3, true,
            Temperature: 25,
            Flows: new[] { new ClusterReadingInfo("C1_Ar", "Ar", 12),
                           new ClusterReadingInfo("C1_N2", "N2", 3) },
            Powers: new[] { new ClusterReadingInfo("C1_RF", "RF", 240),
                            new ClusterReadingInfo("C1_RF_B", "RF B", 80) },
            Valves: new[] { new ClusterValveInfo("C1_V", "gas", true),
                            new ClusterValveInfo("C1_GV", "gate", false) },
            DrivePosition: 100, TurboOn: true, ScrollOn: false);
        var ll = new ClusterChamberInfo("LL", 0, SystemStateCode.Idle, 2.3e-7, false);
        var snap = new ClusterSnapshot(new[] { ll, c1 },
            new ClusterRobotInfo(RobotStateCode.Transferring, 2, 96, 220, true, false, 5e-7, true, true));

        panel.UpdateCluster(snap);          // builds extras + routes readings/valves
        panel.UpdateCluster(snap);          // second tick exercises the no-rebuild path

        var ex = Record.Exception(() =>
        {
            using var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Shutter_leds_built_from_sources_and_excluded_from_generic_valve_rows()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(1000, 700);

        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 1.2e-3, true,
            Powers: new[] { new ClusterReadingInfo("C1_RF", "RF", 240) },
            Valves: new[] { new ClusterValveInfo("C1_V",  "gas",     true),
                            new ClusterValveInfo("C1_Sh", "shutter", true) },
            Sources: new[] { new ClusterSourceInfo("Cu", "C1_Sh", "C1_RF") });
        var snap = new ClusterSnapshot(new[] { c1 },
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false));

        panel.UpdateCluster(snap);

        var shutterLeds = AllControls(panel).OfType<ClusterValveLed>()
            .Where(l => l.ValveId == "C1_Sh").ToArray();
        var led = Assert.Single(shutterLeds);          // exactly one — not duplicated in "C1 valves"
        Assert.True(led.IsOpen);                       // driven via the existing valve pipeline
        Assert.Equal("C1 Cu",                          // labelled with the target material
            led.Controls.OfType<System.Windows.Forms.Label>().Single().Text);

        // The plain gas valve still gets its generic LED.
        Assert.Single(AllControls(panel).OfType<ClusterValveLed>(), l => l.ValveId == "C1_V");
    }

    [Fact]
    public void Paint_with_sources_covers_all_source_row_variants()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(1000, 700);

        // C1: dual source — Cu matched to a live RF reading with shutter OPEN,
        // Ti matched to a live RF B reading with shutter CLOSED.
        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 1.2e-3, true,
            Temperature: 120,
            Flows:  new[] { new ClusterReadingInfo("C1_Ar", "Ar", 20) },
            Powers: new[] { new ClusterReadingInfo("C1_RF", "RF", 300),
                            new ClusterReadingInfo("C1_RF_B", "RF B", 0) },
            Valves: new[] { new ClusterValveInfo("C1_Sh",   "shutter", true),
                            new ClusterValveInfo("C1_Sh_B", "shutter", false) },
            Sources: new[] { new ClusterSourceInfo("Cu", "C1_Sh",   "C1_RF"),
                             new ClusterSourceInfo("Ti", "C1_Sh_B", "C1_RF_B") });

        // C2: source whose supply has NO power reading (offline) → "—" row, no bar;
        // material empty → falls back to the supply id label;
        // third source has a shutter id not present in Valves → renders material row, no glyph.
        var c2 = new ClusterChamberInfo("C2", 120, SystemStateCode.Idle, 5.0e-7, false,
            Valves:  new[] { new ClusterValveInfo("C2_Sh", "shutter", false) },
            Sources: new[] { new ClusterSourceInfo("W",  "C2_Sh",         "C2_RF"),
                             new ClusterSourceInfo("",   null,             "C2_RF_B"),
                             new ClusterSourceInfo("Mo", "C2_Sh_MISSING", null) });

        var snap = new ClusterSnapshot(new[] { c1, c2 },
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false));
        panel.UpdateCluster(snap);

        var ex = Record.Exception(() =>
        {
            using var bmp = new System.Drawing.Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Miscased_shutter_id_still_resolves_to_a_live_valve_led()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(1000, 700);

        // Sources.xml says "c1_sh"; the HWConf valve is "C1_Sh". The LED must be
        // registered under the VALVE's id so the Ordinal routing keeps driving it.
        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Idle, 1e-6, false,
            Valves:  new[] { new ClusterValveInfo("C1_Sh", "shutter", true) },
            Sources: new[] { new ClusterSourceInfo("Cu", "c1_sh", "C1_RF") });
        panel.UpdateCluster(new ClusterSnapshot(new[] { c1 },
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false)));

        var led = Assert.Single(AllControls(panel).OfType<ClusterValveLed>(),
            l => l.ValveId == "C1_Sh");
        Assert.True(led.IsOpen);
        Assert.Equal("C1 Cu",
            led.Controls.OfType<System.Windows.Forms.Label>().Single().Text);
    }

    [Fact]
    public void Source_with_unknown_shutter_valve_gets_no_dead_led()
    {
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(1000, 700);

        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Idle, 1e-6, false,
            Valves:  new[] { new ClusterValveInfo("C1_V", "gas", true) },
            Sources: new[] { new ClusterSourceInfo("Mo", "C1_GHOST", null) });
        panel.UpdateCluster(new ClusterSnapshot(new[] { c1 },
            new ClusterRobotInfo(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false)));

        // No Shutters LED for the stale id — and the generic valve row is intact.
        Assert.DoesNotContain(AllControls(panel).OfType<ClusterValveLed>(),
            l => l.ValveId == "C1_GHOST");
        Assert.Single(AllControls(panel).OfType<ClusterValveLed>(), l => l.ValveId == "C1_V");
    }

    [Fact]
    public void Paint_at_high_dpi_does_not_throw()
    {
        // Fonts are point-sized (render 1.5x at 144 DPI), so geometry must scale
        // with DPI or text outruns the tiles.
        // OnPaintSchematic is invoked via reflection with a Graphics whose DpiY
        // is genuinely 144 (DrawToBitmap always paints at the screen DPI).
        using var panel = NewPanel();
        panel.Size = new System.Drawing.Size(1000, 700);

        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 1.2e-3, true,
            Temperature: 120,
            Flows:  new[] { new ClusterReadingInfo("C1_Ar", "Ar", 20) },
            Powers: new[] { new ClusterReadingInfo("C1_RF", "RF", 300),
                            new ClusterReadingInfo("C1_RF_B", "RF B", 0) },
            Valves: new[] { new ClusterValveInfo("C1_Sh",   "shutter", true),
                            new ClusterValveInfo("C1_Sh_B", "shutter", false) },
            Sources: new[] { new ClusterSourceInfo("Cu", "C1_Sh",   "C1_RF"),
                             new ClusterSourceInfo("Ti", "C1_Sh_B", "C1_RF_B") });
        var snap = new ClusterSnapshot(new[] { c1 },
            new ClusterRobotInfo(RobotStateCode.Transferring, 1, 64, 200, true, false, 5e-7, true, true));
        panel.UpdateCluster(snap);

        using var bmp = new System.Drawing.Bitmap(1000, 700);
        bmp.SetResolution(144f, 144f);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        var paint = typeof(ClusterProcessPanel).GetMethod("OnPaintSchematic",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(paint);

        var ex = Record.Exception(() =>
            paint!.Invoke(panel, new object[] { g, new System.Drawing.Rectangle(0, 0, 1000, 700) }));
        Assert.Null(ex);
    }

    [Fact]
    public void Tile_with_subepsilon_power_reading_does_not_throw()
    {
        // Just after recipe start a PSU forward-power
        // reading can be tiny-but-positive (ADC noise). norm = 1e-9/600 makes the
        // mini-bar fill width fall below GDI+'s REAL_EPSILON (1.192e-7), which
        // AddArc rejects as "Parameter is not valid" — a <= 0 guard doesn't catch it.
        // DrawTile is driven directly with a tall tile so TilePlan keeps ShowBars on
        // regardless of how the panel layout divides the screen.
        var c1 = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 1.2e-3, true,
            Powers: new[] { new ClusterReadingInfo("C1_RF", "RF", 1e-9) });

        var drawTile = typeof(ClusterProcessPanel).GetMethod("DrawTile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(drawTile);

        var ex = Record.Exception(() =>
        {
            using var bmp = new System.Drawing.Bitmap(400, 400);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            drawTile!.Invoke(null, new object[]
                { g, new System.Drawing.Rectangle(10, 10, 260, 320), c1, 1f });
        });
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(1e-10f)]                  // sub-epsilon positive: GDI+ InvalidParameter
    [InlineData(1.19e-7f)]                // just below REAL_EPSILON (1.192092896e-7)
    [InlineData(float.NaN)]               // NaN passes every <=/>= comparison
    [InlineData(float.PositiveInfinity)]  // r.Right overflows into AddArc
    public void RoundedRect_tolerates_degenerate_widths(float width)
    {
        var m = typeof(ClusterProcessPanel).GetMethod("RoundedRect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(m);

        var ex = Record.Exception(() =>
        {
            using var path = (System.Drawing.Drawing2D.GraphicsPath)m!.Invoke(
                null, new object[] { new System.Drawing.RectangleF(10f, 10f, width, 4f), 2f })!;
        });
        Assert.Null(ex);
    }

    private static IEnumerable<System.Windows.Forms.Control> AllControls(System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in AllControls(child)) yield return grandchild;
        }
    }
}

public class ClusterProcessPanelProfileTests
{
    [Fact]
    public void ProcessPanelXml_LoadsCleanAndEveryReadingNamesADeviceDeclaredInHwConf()
    {
        var spec = ProcessPanelSpec.Load(APS.SystemControl.Tests.Shared.ConfigsDir.Path("Cluster/ProcessPanel.xml"));
        Assert.Empty(spec.Errors);
        Assert.NotEmpty(spec.Readings);

        var hw = System.Xml.Linq.XDocument.Load(APS.SystemControl.Tests.Shared.ConfigsDir.Path("Cluster/HWConf.xml"));
        var declared = hw.Descendants().Where(e => !e.HasElements).Select(e => e.Value.Trim())
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(spec.Readings, r => Assert.Contains(r.Id.Replace('·', '_'), declared));
    }
}
