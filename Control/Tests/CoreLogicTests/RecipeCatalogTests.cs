using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>The recipe chooser lists recipes in folder groups with Name and Modified
/// columns, sortable by either; folders are never re-ordered by a sort.</summary>
public class RecipeCatalogTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 10, 0, 0);

    private static IReadOnlyList<RecipeEntry> Sample() => RecipeCatalog.Build(
    [
        ("si-single-layer.rcpx",       T0.AddDays(3)),
        (@"Tests\Deposition_test.rcpx", T0.AddDays(9)),
        ("Cleaning.rcpx",              T0.AddDays(1)),
        (@"arm-a\ML.rcpx",             T0.AddDays(5)),
        ("DEMO-Showcase.rcpx",         T0.AddDays(2)),
        (@"arm-a\Probe.rcpx",          T0.AddDays(4)),
    ]);

    [Fact]
    public void Build_strips_the_extension_and_records_the_folder()
    {
        var e = Sample().Single(x => x.RelativePath == @"Tests\Deposition_test.rcpx");
        Assert.Equal("Deposition_test", e.Name);
        Assert.Equal("Tests", e.Folder);
        Assert.Equal(T0.AddDays(9), e.Modified);
        Assert.Equal("", Sample().Single(x => x.Name == "Cleaning").Folder);
        Assert.Equal("Runs\\ArmA", RecipeCatalog.Build([("Runs/ArmA/x.rcpx", T0)]).Single().Folder);
    }

    [Fact]
    public void Folders_put_the_root_first_then_sub_folders_alphabetically()
    {
        Assert.Equal(["", "arm-a", "Tests"], RecipeCatalog.Folders(Sample()));
    }

    [Fact]
    public void Sort_by_name_orders_within_each_folder_and_keeps_folder_order()
    {
        var sorted = RecipeCatalog.Sort(Sample(), RecipeSortKey.Name, ascending: true);
        Assert.Equal(["Cleaning", "DEMO-Showcase", "si-single-layer", "ML", "Probe", "Deposition_test"],
                     sorted.Select(e => e.Name).ToArray());

        var desc = RecipeCatalog.Sort(Sample(), RecipeSortKey.Name, ascending: false);
        Assert.Equal(["si-single-layer", "DEMO-Showcase", "Cleaning", "Probe", "ML", "Deposition_test"],
                     desc.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_by_date_orders_within_each_folder_and_keeps_folder_order()
    {
        var newestFirst = RecipeCatalog.Sort(Sample(), RecipeSortKey.Modified, ascending: false);
        Assert.Equal(["si-single-layer", "DEMO-Showcase", "Cleaning", "ML", "Probe", "Deposition_test"],
                     newestFirst.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Equal_dates_fall_back_to_name_so_the_order_is_deterministic()
    {
        var entries = RecipeCatalog.Build([("b.rcpx", T0), ("a.rcpx", T0), ("c.rcpx", T0)]);
        Assert.Equal(["a", "b", "c"],
                     RecipeCatalog.Sort(entries, RecipeSortKey.Modified, ascending: false).Select(e => e.Name).ToArray());
    }
}
