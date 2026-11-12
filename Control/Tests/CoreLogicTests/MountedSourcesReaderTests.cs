using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class MountedSourcesReaderTests
{
    [Fact]
    public void Mounted_TakesMaterialAndErosion_FromTarget()
    {
        var g = Guid.NewGuid();
        var placements = new[] { new MagnetronPlacement(g.ToString(), "MAG-4", "4\" round", 2) };
        var targets = new Dictionary<Guid, MountedTarget> { [g] = new("W", "4\" round", 3.2, 6.0, 0.0) };

        var result = MountedSourcesReader.Build(placements, targets);

        var s = Assert.Single(result);
        Assert.Equal(2, s.Flange);
        Assert.Equal("W", s.Material);
        Assert.Equal("MAG-4", s.MagnetronModel);
        Assert.Equal(g.ToString(), s.MagnetronInstance);
        Assert.Equal(3.2, s.ErosionDepthMm);
    }

    [Fact]
    public void Empty_Magnetron_YieldsUnknownMaterial_NoErosion()
    {
        var placements = new[] { new MagnetronPlacement(Guid.NewGuid().ToString(), "MAG-4", "4\" round", 5) };
        var result = MountedSourcesReader.Build(placements, new Dictionary<Guid, MountedTarget>());

        var s = Assert.Single(result);
        Assert.Equal("unknown", s.Material);
        Assert.Null(s.ErosionDepthMm);
        Assert.Equal("4\" round", s.Format); // falls back to the magnetron format
    }

    // Lot / serial let a film be traced back to the physical target that was mounted.

    [Fact]
    public void Mounted_CarriesTargetLotAndSerial()
    {
        var g = Guid.NewGuid();
        var placements = new[] { new MagnetronPlacement(g.ToString(), "MAG-4", "4\" round", 2) };
        var targets = new Dictionary<Guid, MountedTarget>
        {
            [g] = new("Cr", "4\" round", 3.2, 6.0, 0.0, Lot: "L-2026-03", Serial: "CR-2419"),
        };

        var s = Assert.Single(MountedSourcesReader.Build(placements, targets));

        Assert.Equal("L-2026-03", s.TargetLot);
        Assert.Equal("CR-2419", s.TargetSerial);
    }

    [Fact]
    public void Empty_Magnetron_HasNoLotOrSerial()
    {
        var placements = new[] { new MagnetronPlacement(Guid.NewGuid().ToString(), "MAG-4", "4\" round", 5) };

        var s = Assert.Single(MountedSourcesReader.Build(placements, new Dictionary<Guid, MountedTarget>()));

        Assert.Null(s.TargetLot);
        Assert.Null(s.TargetSerial);
    }

    [Fact]
    public void Target_WithoutLotOrSerial_YieldsNulls_NotEmptyStrings()
    {
        // Lot and Serial are optional in inventory; an unlabelled target must not
        // fabricate identity in the evidence record.
        var g = Guid.NewGuid();
        var placements = new[] { new MagnetronPlacement(g.ToString(), "MAG-4", "4\" round", 1) };
        var targets = new Dictionary<Guid, MountedTarget> { [g] = new("Si", "4\" round", 0.0, 3.0, 0.0) };

        var s = Assert.Single(MountedSourcesReader.Build(placements, targets));

        Assert.Null(s.TargetLot);
        Assert.Null(s.TargetSerial);
    }

    [Fact]
    public void Lot_And_Serial_DoNotChangeTheFingerprint()
    {
        // THE invariant behind "provenance only". Two runs identical except for
        // which physical target is mounted must fingerprint the same, so swapping
        // in a fresh target of the same material still groups as one condition —
        // consistent with magnetron instance and erosion depth being excluded.
        var g = Guid.NewGuid();
        var placements = new[] { new MagnetronPlacement(g.ToString(), "MAG-4", "4\" round", 2) };

        static string Fingerprint(IEnumerable<DepositionSource> sources) =>
            DepositionFingerprint.Compute("valve M1 open\ndeposit Cr 60 1",
                sources.Select(s => new FingerprintSource(
                    s.Flange ?? -1, s.Material, s.Format, s.MagnetronModel,
                    s.PowerSupply?.Id, s.PowerSupply?.Model)));

        var first = MountedSourcesReader.Build(placements, new Dictionary<Guid, MountedTarget>
        {
            [g] = new("Cr", "4\" round", 3.2, 6.0, 0.0, Lot: "L-2026-03", Serial: "CR-2419"),
        });

        var second = MountedSourcesReader.Build(placements, new Dictionary<Guid, MountedTarget>
        {
            [g] = new("Cr", "4\" round", 0.0, 6.0, 0.0, Lot: "L-2026-11", Serial: "CR-9987"),
        });

        Assert.NotEqual(first[0].TargetSerial, second[0].TargetSerial);
        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }
}
