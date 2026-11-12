using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// Locks the contract frmSources' Save guard depends on: effective names (Name,
/// falling back to Material) computed across all sources must be duplicate-free.
/// See frmSources.BtnSave_Click.
/// </summary>
public class SourceNameValidationTests
{
    [Fact]
    public void TwoSourcesSameMaterialNoName_AreDuplicates()
    {
        var mgr = new SourceManager(6);
        mgr.GetSource(2)!.Material = "W";
        mgr.GetSource(4)!.Material = "W";
        var eff = mgr.Sources.Values
            .Where(s => !string.IsNullOrWhiteSpace(s.Material) || !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => SourceNames.Effective(s.Name, s.Material));
        Assert.Equal(new[] { "W" }, SourceNames.Duplicates(eff));
    }

    [Fact]
    public void DistinctNames_NoDuplicates()
    {
        var mgr = new SourceManager(6);
        mgr.GetSource(2)!.Material = "W"; mgr.GetSource(2)!.Name = "W_1";
        mgr.GetSource(4)!.Material = "W"; mgr.GetSource(4)!.Name = "W_2";
        var eff = mgr.Sources.Values
            .Where(s => !string.IsNullOrWhiteSpace(s.Material) || !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => SourceNames.Effective(s.Name, s.Material));
        Assert.Empty(SourceNames.Duplicates(eff));
    }
}
