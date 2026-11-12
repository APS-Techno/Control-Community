using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// The stepper-drive panel labels each flange with the operator-facing source handle:
/// the <c>name</c> when the source has one, falling back to the material only when it
/// does not.
/// </summary>
public class DriveSourceInfoBuilderTests
{
    private static (string Label, double Seconds) Flange(object[] info, int flange)
    {
        var entry = Assert.IsType<object[]>(info[flange - 1]);
        return ((string)entry[0], (double)entry[1]);
    }

    [Fact]
    public void Label_prefers_source_name_over_material()
    {
        var sm = new SourceManager(6);
        var src = sm.GetSource(1)!;
        src.Name = "Cu_top";
        src.Material = "Cu";

        var info = DriveSourceInfoBuilder.Build(sm);

        Assert.Equal("Cu_top", Flange(info, 1).Label);
    }

    [Fact]
    public void Label_falls_back_to_material_when_name_is_empty_or_blank()
    {
        var sm = new SourceManager(6);
        sm.GetSource(1)!.Material = "W";          // no name at all
        sm.GetSource(2)!.Name = "   ";            // whitespace-only name
        sm.GetSource(2)!.Material = "Ti";

        var info = DriveSourceInfoBuilder.Build(sm);

        Assert.Equal("W", Flange(info, 1).Label);
        Assert.Equal("Ti", Flange(info, 2).Label);
    }

    [Fact]
    public void Empty_flange_yields_empty_label_and_zero_time()
    {
        var info = DriveSourceInfoBuilder.Build(new SourceManager(6));

        var (label, seconds) = Flange(info, 3);
        Assert.Equal("", label);
        Assert.Equal(0.0, seconds);
    }

    [Fact]
    public void Emits_one_entry_per_flange_carrying_total_deposition_seconds()
    {
        var sm = new SourceManager(6);
        sm.GetSource(4)!.Name = "Ar_gun";
        sm.GetSource(4)!.TotalDepositionTime = TimeSpan.FromSeconds(90);

        var info = DriveSourceInfoBuilder.Build(sm);

        Assert.Equal(6, info.Length);
        Assert.Equal(("Ar_gun", 90.0), Flange(info, 4));
    }
}
