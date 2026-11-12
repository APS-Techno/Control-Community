using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class DepositionRecordServiceTests
{
    private static DepositionConditions Conditions() => new()
    {
        Specimen = "Sample 42",
        Signature = new DepositionSignature { Name = "Alex", Intent = "authorized" },
        Sources = [ new DepositionSource { Flange = 2, Material = "W", Format = "x", MagnetronModel = "MAG-4" } ],
        Recipe = new DepositionRecipe { Name = "W_dep", Format = "rcpx", Content = "<Recipe/>" }
    };

    [Fact]
    public void Create_WritesDatedFile_AndSetsFingerprint()
    {
        string root = Path.Combine(Path.GetTempPath(), "aps-depo", Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new DepositionRecordService(root);
            var (snap, path) = svc.Create(Conditions(), basedOn: null);

            Assert.True(File.Exists(path));
            Assert.EndsWith(".depo.json", path);
            Assert.DoesNotContain(' ', Path.GetFileName(path));        // specimen sanitized
            Assert.StartsWith("sha256:", snap.Conditions.Fingerprint);
            Assert.Contains("sha256:", File.ReadAllText(path));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Finalize_AppendsOutcome_WithoutChangingFingerprint()
    {
        string root = Path.Combine(Path.GetTempPath(), "aps-depo", Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new DepositionRecordService(root);
            var (snap, path) = svc.Create(Conditions(), null);
            string fp = snap.Conditions.Fingerprint;

            svc.Finalize(path, new DepositionOutcome { Result = "completed" });

            var back = DepositionSnapshotSerializer.FromJson(File.ReadAllText(path));
            Assert.Equal("completed", back.Outcome!.Result);
            Assert.Equal(fp, back.Conditions.Fingerprint);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
