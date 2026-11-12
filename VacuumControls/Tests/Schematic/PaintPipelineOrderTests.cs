using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using APS.Controls.Vacuum.Schematic;
using APS.Controls.XML.GUI;
using Xunit;

namespace APS.Controls.Tests.Schematic;

public class PaintPipelineOrderTests
{
    private sealed class OrderRecorder : VacuumSchematicCanvas
    {
        public List<string> Order { get; } = new();
        protected override void OnPaintCanvasBackground(Graphics g) => Order.Add("CanvasBg");
        protected override void OnPaintAdornments(Graphics g) => Order.Add("Adornments");
        protected override Color? GetPipeOverlayColor(XML_Pipe p) { Order.Add("PipeOverlay"); return null; }
        protected override Color GetChamberCavityTint(XML_Chamber ch, Color c) { Order.Add("ChamberTint"); return c; }

        public void Paint()
        {
            SetBounds(0, 0, 400, 300);
            using var bmp = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            typeof(Control).GetMethod("OnPaint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(this, new object[] { new PaintEventArgs(g, ClientRectangle) });
        }
    }

    [Fact]
    public void PaintPipeline_FiresHooksInOrder_BgChambersPipesAdornments()
    {
        var c = new OrderRecorder
        {
            Schematic = new XML_Schematic
            {
                Chambers = new XML_Chambers
                {
                    Items = new List<XML_Chamber>
                    {
                        new XML_Chamber { Id = "C1", Left = 10, Top = 10, Width = 30, Height = 30, Wall = 1 }
                    }
                },
                Pipes = new XML_Pipes
                {
                    Items = new List<XML_Pipe>
                    {
                        new XML_Pipe { Id = "P1", From = "@5,5", To = "@95,95" }
                    }
                }
            }
        };
        c.Paint();
        c.Dispose();
        Assert.Equal(new[] { "CanvasBg", "ChamberTint", "PipeOverlay", "Adornments" }, c.Order);
    }
}
