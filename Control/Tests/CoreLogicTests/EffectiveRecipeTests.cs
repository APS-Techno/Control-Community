using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class EffectiveRecipeTests
{
    [Fact]
    public void Rcpx_BakesOverrides_IntoContent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-eff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "W_dep.rcpx");
        File.WriteAllText(path,
            "<Recipe><Description type=\"deposition\"><Title>W</Title></Description>" +
            "<Parameters><Group name=\"G\"><Param name=\"pressure\" caption=\"P\" unit=\"\" type=\"float\">2.00</Param></Group></Parameters>" +
            "<Script>pwait pm pressure 60</Script></Recipe>");
        try
        {
            var (name, format, content) = EffectiveRecipe.Resolve(path,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["pressure"] = "5.00" });

            Assert.Equal("W_dep", name);
            Assert.Equal("rcpx", format);
            Assert.Contains("5.00", content);
            Assert.DoesNotContain(">2.00<", content); // the param default was overwritten
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void LegacyRecipe_ReturnedVerbatim()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-eff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "old.recipe");
        File.WriteAllText(path, "valve M1 open\ndelay 5\n");
        try
        {
            var (name, format, content) = EffectiveRecipe.Resolve(path, null);
            Assert.Equal("old", name);
            Assert.Equal("recipe", format);
            Assert.Equal("valve M1 open\ndelay 5\n", content);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
