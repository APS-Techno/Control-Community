using APS.Controls.General;
using Xunit;

namespace APS.Controls.Tests.General;

public class LEDPanelTests
{
    [Theory]
    [InlineData(PanelStyle.Horiz)]
    [InlineData(PanelStyle.Vert)]
    public void Rescaling_within_font_floor_keeps_live_fonts(PanelStyle style)
    {
        using var panel = new LEDPanel("pm", style) { Caption = "PM", Units = "Torr", Value = "7.6e2" };

        panel.BaseScale = 0.40f;
        panel.BaseScale = 0.35f;

        foreach (Control child in panel.Controls)
            Assert.True(child.Font.SizeInPoints > 0f, $"{child.GetType().Name} holds a disposed font");
    }
}
