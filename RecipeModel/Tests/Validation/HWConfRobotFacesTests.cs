using System.IO;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class HWConfRobotFacesTests
{
    private const string Sample = """
        <hardware>
          <robots lib="RobotLib" hwtype="RobotLib.S7200Robot" host="tm">
            <robot address="220" cmdIndex="0" statusIndex="4" descr="Transfer robot">
              ROBOT
              <face name="LL" idx="1" slit="SV_LL" angle="0"/>
              <face name="C1" idx="2" slit="SV_C1" angle="60"/>
            </robot>
          </robots>
        </hardware>
        """;

    private static string WriteTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hwconf_{System.Guid.NewGuid():N}.xml");
        File.WriteAllText(path, Sample);
        return path;
    }

    [Fact]
    public void ReadRobotFaces_ReturnsConfiguredFaces()
    {
        string path = WriteTemp();
        try
        {
            var faces = HWConfDeviceReader.ReadRobotFaces(path);
            Assert.Contains("LL", faces);
            Assert.Contains("C1", faces);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadDevicesByCategory_IncludesRobotFacesCategory()
    {
        string path = WriteTemp();
        try
        {
            var byCat = HWConfDeviceReader.ReadDevicesByCategory(path);
            Assert.True(byCat.ContainsKey("Robot Faces"));
            Assert.Contains("LL", byCat["Robot Faces"]);
            Assert.Contains("park", byCat["Robot Faces"]);
        }
        finally { File.Delete(path); }
    }
}
