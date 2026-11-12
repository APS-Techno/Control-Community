using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class SourceGeometryRoundTripTests
{
    [Fact]
    public void ZeroAndLift_SurviveSaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sources-{Guid.NewGuid():N}.xml");
        try
        {
            var sm = new SourceManager(6);
            var s1 = sm.GetSource(1)!;
            s1.ZeroDistanceMm = 92.5;
            s1.LiftPositionMm = 8.0;
            sm.Save(path);

            var reloaded = new SourceManager(6);
            reloaded.Load(path);

            Assert.Equal(92.5, reloaded.GetSource(1)!.ZeroDistanceMm!.Value, 6);
            Assert.Equal(8.0, reloaded.GetSource(1)!.LiftPositionMm!.Value, 6);
            // Flange with nothing set stays null (not 0).
            Assert.Null(reloaded.GetSource(2)!.ZeroDistanceMm);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
