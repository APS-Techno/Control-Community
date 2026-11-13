using System.Linq;
using APS.ProcessPanel.Contracts;
using APS.Controls.Generic;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Row semantics for the cluster chamber tiles: the leading dot reflects the
/// magnetron supply (lit = delivering power), NOT the shutter — during a
/// multilayer the sources keep sputtering behind closed shutters (Cluster demo
/// C3 Si/B), so a shutter-keyed dot reads as "magnetron cycling". Shutter state
/// lives in the OPEN/CLOSED tag only.
/// </summary>
public class ClusterTileMetricsTests
{
    private static readonly System.Drawing.Color State = System.Drawing.Color.Lime;

    private static ClusterChamberInfo C3(double rfW, double rfbW, bool siOpen, bool bOpen) => new(
        "C3", 180, SystemStateCode.Processing, 2.0e-3, false,
        Powers: new[]
        {
            ClusterReadingInfo.For("C3_RF",   "C3", rfW),
            ClusterReadingInfo.For("C3_RF_B", "C3", rfbW),
        },
        Valves: new[]
        {
            new ClusterValveInfo("C3_Sh",   "Si shutter", siOpen),
            new ClusterValveInfo("C3_Sh_B", "B shutter",  bOpen),
        },
        Sources: new[]
        {
            new ClusterSourceInfo("Si", "C3_Sh",   "C3_RF"),
            new ClusterSourceInfo("B",  "C3_Sh_B", "C3_RF_B"),
        });

    [Fact]
    public void Dot_follows_power_not_shutter()
    {
        var rows = ClusterProcessPanel.BuildTileMetrics(C3(110, 90, siOpen: false, bOpen: true), State);

        var si = rows.Single(r => r.Name == "Si");
        Assert.True(si.SourceOn);          // energized behind a closed shutter
        Assert.False(si.ShutterOpen);

        var b = rows.Single(r => r.Name == "B");
        Assert.True(b.SourceOn);
        Assert.True(b.ShutterOpen);
    }

    [Fact]
    public void Dot_off_when_supply_reads_zero()
    {
        var rows = ClusterProcessPanel.BuildTileMetrics(C3(0, 0, siOpen: false, bOpen: false), State);

        Assert.False(rows.Single(r => r.Name == "Si").SourceOn);
        Assert.False(rows.Single(r => r.Name == "B").SourceOn);
    }

    [Fact]
    public void Source_without_live_reading_has_no_dot()
    {
        var c = new ClusterChamberInfo("C2", 120, SystemStateCode.Idle, 1e-6, false,
            Sources: new[] { new ClusterSourceInfo("W", "C2_Sh", "C2_RF") });

        var rows = ClusterProcessPanel.BuildTileMetrics(c, State);

        var w = rows.Single(r => r.Name == "W");
        Assert.Null(w.SourceOn);           // supply not reporting — unknown, no dot
        Assert.Equal("—", w.Value);
    }

    [Fact]
    public void Unmatched_supply_row_has_no_dot()
    {
        var c = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 2e-3, false,
            Powers: new[] { ClusterReadingInfo.For("C1_RF", "C1", 100.0) });

        var rows = ClusterProcessPanel.BuildTileMetrics(c, State);

        Assert.Null(rows.Single(r => r.Name == "RF").SourceOn);
    }

    [Fact]
    public void Row_name_uses_effective_source_name_over_material()
    {
        // Named source ("C1_Cu") + unnamed source (material "Ti"): the tile shows
        // the recipe-addressable handle, falling back to material when unnamed.
        var c = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 2e-3, false,
            Powers: new[]
            {
                ClusterReadingInfo.For("C1_RF",   "C1", 100.0),
                ClusterReadingInfo.For("C1_RF_B", "C1",  90.0),
            },
            Sources: new[]
            {
                new ClusterSourceInfo("Cu", "C1_Sh",   "C1_RF",   Name: "C1_Cu"),
                new ClusterSourceInfo("Ti", "C1_Sh_B", "C1_RF_B"),
            });

        var rows = ClusterProcessPanel.BuildTileMetrics(c, State);

        Assert.Contains(rows, r => r.Name == "C1_Cu");   // name wins
        Assert.DoesNotContain(rows, r => r.Name == "Cu"); // material not shown for named source
        Assert.Contains(rows, r => r.Name == "Ti");      // unnamed → material
    }

    [Fact]
    public void Values_UnderRuRuCulture_UseDotDecimals()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            var chamber = new ClusterChamberInfo("C1", 60, SystemStateCode.Processing, 1.2e-3, false,
                Temperature: 250.4,
                Flows: [ClusterReadingInfo.For("C1_Ar", "C1", 12.5)],
                Powers: [ClusterReadingInfo.For("C1_RF", "C1", 150.4)]);

            var rows = ClusterProcessPanel.BuildTileMetrics(chamber, State);

            Assert.Equal("1.2e-3", rows.Single(r => r.Name == "pm").Value);
            Assert.Equal("12.5", rows.Single(r => r.Name == "Ar").Value);
            Assert.All(rows, r => Assert.DoesNotContain(',', r.Value));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }
}
