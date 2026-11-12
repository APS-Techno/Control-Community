using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using APS.Controls.Vacuum.Schematic;
using APS.Controls.XML.GUI;
using Xunit;

namespace APS.Controls.Tests.Schematic;

public class ChamberDrawingTests
{
    private sealed class RecordingCanvas : VacuumSchematicCanvas
    {
        public List<(string Id, Color Base)> TintCalls { get; } = new();
        protected override Color GetChamberCavityTint(XML_Chamber ch, Color baseColor)
        {
            TintCalls.Add((ch.Id ?? "", baseColor));
            return Color.FromArgb(255, 0, 0);
        }
        public void PaintInto(Bitmap b)
        {
            SetBounds(0, 0, b.Width, b.Height);
            using var g = Graphics.FromImage(b);
            typeof(Control).GetMethod("OnPaint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(this, new object[] { new PaintEventArgs(g, ClientRectangle) });
        }
    }

    private static XML_Schematic OneChamberSchematic() => new()
    {
        Chambers = new XML_Chambers
        {
            Items = new List<XML_Chamber>
            {
                new XML_Chamber { Id = "C1", Label = "Main",
                    Left = 25, Top = 25, Width = 50, Height = 50, Wall = 2 }
            }
        }
    };

    [Fact]
    public void DrawChambers_InvokesTintHookOncePerChamber()
    {
        using var c = new RecordingCanvas { Schematic = OneChamberSchematic() };
        using var bmp = new Bitmap(400, 400, PixelFormat.Format32bppArgb);
        c.PaintInto(bmp);
        Assert.Single(c.TintCalls);
        Assert.Equal("C1", c.TintCalls[0].Id);
    }

    [Fact]
    public void DrawChambers_TintHookReturnValueUsedForCavityFill()
    {
        using var c = new RecordingCanvas { Schematic = OneChamberSchematic() };
        using var bmp = new Bitmap(400, 400, PixelFormat.Format32bppArgb);
        c.PaintInto(bmp);
        var px = bmp.GetPixel(220, 180);
        Assert.Equal(255, px.R);
        Assert.Equal(0, px.G);
        Assert.Equal(0, px.B);
    }
}
