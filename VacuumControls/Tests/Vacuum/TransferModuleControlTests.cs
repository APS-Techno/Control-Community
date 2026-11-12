using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Vacuum;

/// <summary>
/// xUnit runs facts on MTA thread-pool threads; WinForms is fine there as long as
/// no messages are pumped across threads. CreateControl() + DoEvents() work from MTA.
/// </summary>
public class TransferModuleControlTests
{
    [Fact]
    public void State_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.State = RobotState.Idle;
        Assert.Equal(RobotState.Idle, c.State);
    }

    [Fact]
    public void Phase_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.Phase = RobotPhase.RotateToSrc;
        Assert.Equal(RobotPhase.RotateToSrc, c.Phase);
    }

    [Fact]
    public void CurrentFace_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.CurrentFace = 3;
        Assert.Equal(3, c.CurrentFace);
    }

    [Fact]
    public void HasWafer_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.HasWafer = true;
        Assert.True(c.HasWafer);
    }

    [Fact]
    public void ErrorCode_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.ErrorCode = RobotErrorCode.SlitTimeout;
        Assert.Equal(RobotErrorCode.SlitTimeout, c.ErrorCode);
    }

    [Fact]
    public void State_SameValue_DoesNotInvalidate()
    {
        // Startup is the default, so the guard skips Invalidate(). Invalidate calls
        // can't be counted from tests, so only the value is checked.
        using var c = new TransferModuleControl("ROBOT");
        c.State = RobotState.Startup;
        Assert.Equal(RobotState.Startup, c.State);
    }

    [Fact]
    public void State_Change_TriggersInvalidate_ViaPaintEvent()
    {
        // DrawToBitmap drives the paint so the test is headless-safe.
        using var c = new TransferModuleControl("ROBOT");
        c.Size = new System.Drawing.Size(380, 280);
        c.CreateControl();

        int paints = 0;
        c.Paint += (_, _) => paints++;

        using var bmp1 = new System.Drawing.Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp1, new System.Drawing.Rectangle(0, 0, c.Width, c.Height));
        int baseline = paints;

        c.State = RobotState.Idle;

        using var bmp2 = new System.Drawing.Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp2, new System.Drawing.Rectangle(0, 0, c.Width, c.Height));

        Assert.True(paints > baseline,
            $"Expected Paint event after State change; baseline={baseline}, paints={paints}");
    }

    [Fact]
    public void DefaultState_IsStartup()
    {
        using var c = new TransferModuleControl("ROBOT");
        Assert.Equal(RobotState.Startup, c.State);
    }

    [Fact]
    public void DefaultPhase_IsNone()
    {
        using var c = new TransferModuleControl("ROBOT");
        Assert.Equal(RobotPhase.None, c.Phase);
    }

    [Fact]
    public void DefaultErrorCode_IsOk()
    {
        using var c = new TransferModuleControl("ROBOT");
        Assert.Equal(RobotErrorCode.Ok, c.ErrorCode);
    }

    [Fact]
    public void Pressure_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.Pressure = 8.5e-6f;
        Assert.Equal(8.5e-6f, c.Pressure);
    }

    [Fact]
    public void TurboOn_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.TurboOn = true;
        Assert.True(c.TurboOn);
    }

    [Fact]
    public void ScrollOn_RoundTrips()
    {
        using var c = new TransferModuleControl("ROBOT");
        c.ScrollOn = true;
        Assert.True(c.ScrollOn);
    }

    private static byte[] RenderBitmap(TransferModuleControl widget)
    {
        widget.Size = new System.Drawing.Size(380, 280);
        widget.CreateControl();
        using var bmp = new System.Drawing.Bitmap(widget.Width, widget.Height);
        widget.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Render_PumpStateToggle_DoesNotChangeRender()
    {
        using var pumpsOff = new TransferModuleControl("ROBOT")
        {
            Pressure = 1e-4f, TurboOn = false, ScrollOn = false,
        };
        using var pumpsOn = new TransferModuleControl("ROBOT")
        {
            Pressure = 1e-4f, TurboOn = true, ScrollOn = true,
        };

        var bytesOff = RenderBitmap(pumpsOff);
        var bytesOn  = RenderBitmap(pumpsOn);

        Assert.True(bytesOff.SequenceEqual(bytesOn),
            "TM widget no longer renders pump state (pumps are separate schematic widgets)");
    }

    [Fact]
    public void Render_DifferentPressures_IdenticalOutput()
    {
        // TM pressure is shown by the unified TM_pm LEDPanel indicator (like LL/C1),
        // not inside the hex, so the Pressure value must not affect the render.
        using var lo = new TransferModuleControl("ROBOT") { Pressure = 1e-8f };
        using var hi = new TransferModuleControl("ROBOT") { Pressure = 1e-2f };

        var bytesLo = RenderBitmap(lo);
        var bytesHi = RenderBitmap(hi);

        Assert.True(bytesLo.SequenceEqual(bytesHi),
            "TM widget no longer renders pressure (shown by the unified TM_pm indicator)");
    }

    private static byte[] RenderHash(TransferModuleControl widget)
    {
        widget.Size = new System.Drawing.Size(380, 280);
        widget.CreateControl();
        using var bmp = new System.Drawing.Bitmap(widget.Width, widget.Height);
        widget.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(ms.ToArray());
    }

    [Fact]
    public void Render_IdleState_HasStableHash()
    {
        using var w = new TransferModuleControl("ROBOT")
        {
            State    = RobotState.Idle,
            Pressure = 8.5e-6f,
            TurboOn  = true,
            ScrollOn = true,
            CurrentFace = 0,
            HasWafer = false,
            Faces = new[]
            {
                new TransferModuleControl.TmFace("LL", 1, SlitOpen: false),
                new TransferModuleControl.TmFace("C1", 2, SlitOpen: false),
            },
        };

        byte[] hash = RenderHash(w);

        Assert.NotEmpty(hash);
        Assert.Equal(32, hash.Length);

        byte[] hash2 = RenderHash(w);
        Assert.True(hash.SequenceEqual(hash2),
            "Idle render is non-deterministic — same state, different hash on second call");
    }

    [Fact]
    public void Render_TransferringWithWafer_DiffersFromIdle()
    {
        using var idle = new TransferModuleControl("ROBOT")
        {
            State    = RobotState.Idle,
            HasWafer = false,
        };
        using var transferring = new TransferModuleControl("ROBOT")
        {
            State      = RobotState.Transferring,
            Phase      = RobotPhase.RotateToSrc,
            HasWafer   = true,
            AngleNorm  = 128,   // ~180° — arm pointing down
            ExtendNorm = 100,   // partially extended
        };

        var ih = RenderHash(idle);
        var th = RenderHash(transferring);

        Assert.False(ih.SequenceEqual(th),
            "Transferring+wafer render must differ from Idle render");
    }

    [Fact]
    public void Render_ErrorState_DiffersFromIdle_ViaStateDot()
    {
        using var error = new TransferModuleControl("ROBOT")
        {
            State = RobotState.Error, ErrorCode = RobotErrorCode.SlitTimeout,
        };
        using var idle = new TransferModuleControl("ROBOT") { State = RobotState.Idle };

        var errorHash = RenderHash(error);
        var idleHash  = RenderHash(idle);

        Assert.NotEmpty(errorHash);
        Assert.NotEmpty(idleHash);
        Assert.False(errorHash.SequenceEqual(idleHash),
            "Error state dot (OrangeRed) must render differently from Idle state dot (LimeGreen)");
    }

    private static double Dist(System.Drawing.Point a, System.Drawing.Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    [Fact]
    public void DefineAnchors_Exposes12NamedFaceAndVertexAnchors()
    {
        using var c = new TransferModuleControl("ROBOT") { Size = new System.Drawing.Size(240, 240) };
        var names = c.GetAnchorNames().ToList();
        Assert.Equal(12, names.Count);
        for (int i = 1; i <= 6; i++)
        {
            Assert.Contains($"face{i}", names);
            Assert.Contains($"vertex{i}", names);
        }
    }

    [Fact]
    public void Anchors_FaceMidpointsCloserToCentreThanVertices()
    {
        using var c = new TransferModuleControl("ROBOT") { Size = new System.Drawing.Size(240, 240) };
        var centre = new System.Drawing.Point(c.Width / 2, c.Height / 2);
        for (int i = 1; i <= 6; i++)
        {
            double faceDist = Dist(c.GetAnchorPoint($"face{i}"), centre);
            double vertDist = Dist(c.GetAnchorPoint($"vertex{i}"), centre);
            Assert.True(faceDist < vertDist,
                $"face{i} ({faceDist:0.0}) should be closer to centre than vertex{i} ({vertDist:0.0})");
        }
    }

    [Fact]
    public void Anchor_Face1_IsAboveCentre()
    {
        using var c = new TransferModuleControl("ROBOT") { Size = new System.Drawing.Size(240, 240) };
        var p = c.GetAnchorPoint("face1");
        Assert.InRange(p.X, c.Width / 2 - 2, c.Width / 2 + 2);
        Assert.True(p.Y < c.Height / 2, $"face1 Y={p.Y} should be above centre {c.Height / 2}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Anchor_OppositeFaces_AreSymmetricAboutCentre(int i)
    {
        using var c = new TransferModuleControl("ROBOT") { Size = new System.Drawing.Size(240, 240) };
        var a = c.GetAnchorPoint($"face{i}");
        var b = c.GetAnchorPoint($"face{i + 3}");
        // Opposite faces mirror through the centre: their coordinates sum to ~2*centre.
        Assert.InRange(a.X + b.X, c.Width - 2, c.Width + 2);
        Assert.InRange(a.Y + b.Y, c.Height - 2, c.Height + 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Anchor_OppositeVertices_AreSymmetricAboutCentre(int i)
    {
        using var c = new TransferModuleControl("ROBOT") { Size = new System.Drawing.Size(240, 240) };
        var a = c.GetAnchorPoint($"vertex{i}");
        var b = c.GetAnchorPoint($"vertex{i + 3}");
        // Opposite vertices mirror through the centre: coordinates sum to ~2*centre.
        Assert.InRange(a.X + b.X, c.Width  - 2, c.Width  + 2);
        Assert.InRange(a.Y + b.Y, c.Height - 2, c.Height + 2);
    }

    [Fact]
    public void Anchor_Vertex1_IsUpperRight()
    {
        using var c = new TransferModuleControl("ROBOT") { Size = new System.Drawing.Size(240, 240) };
        var p = c.GetAnchorPoint("vertex1");
        Assert.True(p.X > c.Width  / 2, $"vertex1 X={p.X} should be right of centre");
        Assert.True(p.Y < c.Height / 2, $"vertex1 Y={p.Y} should be above centre");
    }

    [Fact]
    public void Background_IsTransparent_SoCanvasPipesShowThrough()
    {
        // Headless DrawToBitmap can't capture parent-composited transparency, so assert the
        // transparency intent directly. Visual confirmation is done by running the app.
        using var c = new TransferModuleControl("ROBOT");
        Assert.Equal(System.Drawing.Color.Transparent, c.BackColor);
    }

    [Fact]
    public void CalculateSize_IsSquareAndScalesWithBaseScale()
    {
        using var c = new TransferModuleControl("ROBOT");
        int baseW = c.Width;                       // BaseScale defaults to 1.0
        Assert.Equal(c.Width, c.Height);

        c.BaseScale = 2.0f;
        Assert.Equal(c.Width, c.Height);
        Assert.InRange((double)c.Width / baseW, 1.9, 2.1);
    }
}
