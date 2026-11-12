using System.Drawing;
using APS.Controls.Theme;
using Xunit;

namespace APS.Controls.Tests.Theme;

public class GlowPlasmaColorTests
{
    [Fact]
    public void GlowPlasma_HasWarmVioletMagentaDefault()
    {
        Assert.Equal(Color.FromArgb(200, 90, 255), VacuumTheme.GlowPlasma);
    }
}
