using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Threading;
using APS.Controls.Vacuum.Schematic;
using APS.Controls.XML.GUI;
using Xunit;

namespace APS.Controls.Tests.Schematic;

/// <summary>
/// Pipe bend waypoints and "@x,y" endpoints are machine data and must parse
/// culture-invariantly: on a comma-decimal locale, culture-sensitive parsing dropped
/// decimal bends from the render while the invariant hit-test path kept them.
/// </summary>
public class PipeBendCultureTests
{
    private sealed class PointsCanvas : VacuumSchematicCanvas
    {
        public List<PointF> Points(XML_Pipe pipe) => BuildPipePoints(pipe);
    }

    private static void WithCulture(CultureInfo culture, Action body)
    {
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        var prevUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            body();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prevCulture;
            Thread.CurrentThread.CurrentUICulture = prevUiCulture;
        }
    }

    [Fact]
    public void BuildPipePoints_DecimalBendAndEndpoints_ParseUnderRussianLocale()
    {
        WithCulture(new CultureInfo("ru-RU"), () =>
        {
            using var c = new PointsCanvas();
            // 1600x900 is exactly 16:9, so CanvasArea fills the whole control: (0,0,1600,900).
            c.SetBounds(0, 0, 1600, 900);

            // Decimal coordinates are the trigger: integers parse in any culture.
            var pipe = new XML_Pipe { Id = "p1", From = "@10.0,50.0", To = "@90.0,50.0", Bends = "50.0,20.0" };

            var pts = c.Points(pipe);

            // start + one bend + end
            Assert.Equal(3, pts.Count);
            // PercentToPixel: X = 50% * 1600 = 800, Y = 20% * 900 = 180.
            Assert.Equal(800f, pts[1].X, 1f);
            Assert.Equal(180f, pts[1].Y, 1f);
        });
    }
}
