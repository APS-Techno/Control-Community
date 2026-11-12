using System.Linq;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class RegionBalanceTests
{
    private static int Warnings(string src) =>
        new RecipeValidator(RecipeValidator.AllKnownCommands, knownDevices: []).Validate(src).Count(e => e.Severity == RecipeErrorSeverity.Warning
            && e.Message.Contains("region", System.StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Balanced_region_produces_no_region_warning()
    {
        string src = "#region a\nvalve M1 open\n#endregion\n";
        Assert.Equal(0, Warnings(src));
    }

    [Fact]
    public void Unclosed_region_warns()
    {
        string src = "#region a\nvalve M1 open\n";
        Assert.Equal(1, Warnings(src));
    }

    [Fact]
    public void Stray_endregion_warns()
    {
        string src = "valve M1 open\n#endregion\n";
        Assert.Equal(1, Warnings(src));
    }

    [Fact]
    public void Hash_inside_quoted_string_is_not_a_region_marker()
    {
        string src = "message \"see #region docs\"\n";
        Assert.Equal(0, Warnings(src));
    }

    [Fact]
    public void Quoted_hash_on_earlier_line_does_not_break_real_region_balance()
    {
        string src = "message \"a # b\"\n#region x\nvalve M1 open\n#endregion\n";
        Assert.Equal(0, Warnings(src));
    }
}
