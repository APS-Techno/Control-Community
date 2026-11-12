using APS.SystemControl.Core.Evidence;

namespace APS.SystemControl.Tests.CoreLogic;

public class DepositionSnapshotReaderTests
{
    [Fact]
    public void ExtractRecipeToTemp_WritesContent_WithFormatExtension()
    {
        var snap = new DepositionSnapshot
        {
            Conditions = new DepositionConditions
            {
                Recipe = new DepositionRecipe { Name = "W_dep", Format = "rcpx", Content = "<Recipe/>" }
            }
        };
        string path = DepositionSnapshotReader.ExtractRecipeToTemp(snap);
        try
        {
            Assert.EndsWith(".rcpx", path);
            Assert.Equal("<Recipe/>", File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Load_RoundTrips_ConditionsFingerprintAndRecipeContent()
    {
        var snap = new DepositionSnapshot
        {
            Conditions = new DepositionConditions
            {
                Fingerprint = "abc123",
                Recipe = new DepositionRecipe { Name = "Test", Format = "rcpx", Content = "valve M1 open" }
            }
        };
        string json = DepositionSnapshotSerializer.ToJson(snap);
        string tempPath = Path.Combine(Path.GetTempPath(), $"aps-test-roundtrip-{Guid.NewGuid():N}.depo.json");
        try
        {
            File.WriteAllText(tempPath, json);
            var loaded = DepositionSnapshotReader.Load(tempPath);
            Assert.Equal("abc123", loaded.Conditions.Fingerprint);
            Assert.Equal("valve M1 open", loaded.Conditions.Recipe.Content);
        }
        finally { try { File.Delete(tempPath); } catch { } }
    }
}
