using APS.SystemControl.Controls.General;
using APS.SystemControl.Core.Classes;
using System.Linq;
using Xunit;

namespace CoreLogicTests;

public class SourceSchematicMapperTests
{
    private static IReadOnlyDictionary<int, Source> Sources(params Source[] s)
        => s.ToDictionary(x => x.FlangeNumber);

    [Fact]
    public void Maps_material_magnetron_label_occupancy_and_angle()
    {
        var inst = new MagnetronInstance("id-1", "Mag1", null, "MAG-4", null, null, null, "Active");
        var sources = Sources(
            new Source { FlangeNumber = 1, Material = "Si", MagnetronInstanceId = "id-1", AngleDegrees = 30 },
            new Source { FlangeNumber = 2, Material = "", MagnetronInstanceId = "" });

        var views = SourceSchematicMapper.Build(sources, [inst], _ => null);

        var f1 = views.Single(v => v.FlangeNumber == 1);
        Assert.True(f1.Occupied);
        Assert.Equal("Si", f1.Material);
        Assert.Equal("Mag1", f1.MagnetronLabel);
        Assert.Equal(30, f1.AngleDegrees);
        Assert.Null(f1.LifeFraction);          // lookup returned null (Standalone)
        Assert.Equal(0.8, f1.WarnFraction);

        var f2 = views.Single(v => v.FlangeNumber == 2);
        Assert.False(f2.Occupied);
        Assert.Null(f2.MagnetronLabel);
    }

    [Fact]
    public void Life_lookup_drives_fraction_and_warn()
    {
        var sources = Sources(new Source { FlangeNumber = 1, Material = "W" });
        var views = SourceSchematicMapper.Build(sources, [], f => (Life: 0.9, Warn: 0.7));
        var f1 = views.Single();
        Assert.Equal(0.9, f1.LifeFraction);
        Assert.Equal(0.7, f1.WarnFraction);
    }

    [Fact]
    public void Unmatched_magnetron_instance_id_yields_null_label_but_occupied()
    {
        var sources = Sources(new Source { FlangeNumber = 1, Material = "", MagnetronInstanceId = "no-such-id" });
        var f = SourceSchematicMapper.Build(sources, [], _ => null).Single();
        Assert.True(f.Occupied);          // occupied via instance id even with empty material
        Assert.Null(f.MagnetronLabel);    // no instance matches the id
    }

    [Fact]
    public void Power_supply_id_passes_through_and_blank_is_null()
    {
        var sources = Sources(
            new Source { FlangeNumber = 1, Material = "Si", PowerSupplyId = "PSU-B" },
            new Source { FlangeNumber = 2, Material = "W", PowerSupplyId = "" });
        var views = SourceSchematicMapper.Build(sources, [], _ => null);

        var f1 = views.Single(v => v.FlangeNumber == 1);
        Assert.Equal("PSU-B", f1.PowerSupplyId);
        Assert.False(f1.PowerSupplyShared);
        Assert.Null(views.Single(v => v.FlangeNumber == 2).PowerSupplyId);
    }

    [Fact]
    public void Power_supply_on_more_than_one_flange_is_flagged_shared()
    {
        // The glow map drops a PSU wired to several sources — the schematic must make that visible.
        var sources = Sources(
            new Source { FlangeNumber = 1, Material = "Gr", PowerSupplyId = "PSU-B" },
            new Source { FlangeNumber = 2, Material = "Ru", PowerSupplyId = "PSU-C" },
            new Source { FlangeNumber = 3, Material = "W",  PowerSupplyId = "PSU-B" },
            new Source { FlangeNumber = 4, Material = "" });
        var views = SourceSchematicMapper.Build(sources, [], _ => null);

        Assert.True(views.Single(v => v.FlangeNumber == 1).PowerSupplyShared);
        Assert.False(views.Single(v => v.FlangeNumber == 2).PowerSupplyShared);
        Assert.True(views.Single(v => v.FlangeNumber == 3).PowerSupplyShared);
        Assert.False(views.Single(v => v.FlangeNumber == 4).PowerSupplyShared);
    }

    [Fact]
    public void Output_is_ordered_by_flange_number()
    {
        var sources = Sources(
            new Source { FlangeNumber = 3, Material = "W" },
            new Source { FlangeNumber = 1, Material = "Si" },
            new Source { FlangeNumber = 2, Material = "BN" });
        var views = SourceSchematicMapper.Build(sources, [], _ => null);
        Assert.Equal(new[] { 1, 2, 3 }, views.Select(v => v.FlangeNumber).ToArray());
    }
}
