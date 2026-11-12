using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using APS.Controls.Vacuum.Schematic;
using APS.Controls.XML.GUI;
using Xunit;

namespace APS.Controls.Tests.Schematic;

public class PipeOverlaySmokeTests
{
    private sealed class ConfigurableCanvas : VacuumSchematicCanvas
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? Overlay { get; set; } = Color.Cyan;
        protected override Color? GetPipeOverlayColor(XML_Pipe pipe) => Overlay;
        public void PaintInto(Bitmap b)
        {
            SetBounds(0, 0, b.Width, b.Height);
            using var g = Graphics.FromImage(b);
            typeof(Control).GetMethod("OnPaint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(this, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, b.Width, b.Height)) });
        }
    }

    private static XML_Schematic SchematicWithOneFreePipe() => new()
    {
        Pipes = new XML_Pipes
        {
            Items = new List<XML_Pipe>
            {
                new XML_Pipe { Id = "P1", From = "@10,50", To = "@90,50", Gas = "Ar" }
            }
        }
    };

    [Fact]
    public void PipeWithOverlay_WritesGasColorPixels()
    {
        using var c = new ConfigurableCanvas { Schematic = SchematicWithOneFreePipe(), Overlay = Color.FromArgb(0, 200, 255) };
        using var bmp = new Bitmap(400, 100, PixelFormat.Format32bppArgb);
        c.PaintInto(bmp);
        var px = bmp.GetPixel(200, 50);
        Assert.True(px.B > 100, $"Expected blue-rich pixel at centerline, got {px}");
    }

    [Fact]
    public void PipeWithNullOverlay_DoesNotWriteGasColorPixels()
    {
        using var c = new ConfigurableCanvas { Schematic = SchematicWithOneFreePipe(), Overlay = null };
        using var bmp = new Bitmap(400, 100, PixelFormat.Format32bppArgb);
        c.PaintInto(bmp);
        var px = bmp.GetPixel(200, 50);
        Assert.False(px.B > px.R + 50, $"Expected no blue tint without overlay, got {px}");
    }
}
