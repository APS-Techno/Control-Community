using APS.Controls.Dialogs;
using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Dialogs;

public class TransferModuleServiceDialogTests
{
    private static TransferModuleServiceDialog MakeDialog() =>
        new("ROBOT", new[] { ("LL", (byte)1), ("C1", (byte)2) });

    [Theory]
    [InlineData(RobotState.Idle,         true)]
    [InlineData(RobotState.NotHomed,     true)]
    [InlineData(RobotState.Error,        true)]
    [InlineData(RobotState.Startup,      true)]
    [InlineData(RobotState.Homing,       false)]
    [InlineData(RobotState.Transferring, false)]
    public void HomeButton_EnabledMatrix(RobotState state, bool expectedEnabled)
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(state, RobotPhase.None, "park", false, RobotErrorCode.Ok);
        var btn = dlg.Controls.OfType<Button>().First(b => b.Text == "Home");
        Assert.Equal(expectedEnabled, btn.Enabled);
    }

    [Theory]
    [InlineData(RobotState.Transferring, true)]
    [InlineData(RobotState.Homing,       true)]
    [InlineData(RobotState.Idle,         false)]
    [InlineData(RobotState.NotHomed,     false)]
    [InlineData(RobotState.Error,        false)]
    [InlineData(RobotState.Startup,      false)]
    public void AbortButton_EnabledMatrix(RobotState state, bool expectedEnabled)
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(state, RobotPhase.None, "park", false, RobotErrorCode.Ok);
        var btn = dlg.Controls.OfType<Button>().First(b => b.Text == "Abort");
        Assert.Equal(expectedEnabled, btn.Enabled);
    }

    [Theory]
    [InlineData(RobotState.Idle,         true)]
    [InlineData(RobotState.Transferring, false)]
    [InlineData(RobotState.NotHomed,     false)]
    [InlineData(RobotState.Error,        false)]
    [InlineData(RobotState.Homing,       false)]
    [InlineData(RobotState.Startup,      false)]
    public void TransferButton_EnabledMatrix(RobotState state, bool expectedEnabled)
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(state, RobotPhase.None, "park", false, RobotErrorCode.Ok);
        var btn = dlg.Controls.OfType<Button>().First(b => b.Text == "Transfer");
        Assert.Equal(expectedEnabled, btn.Enabled);
    }

    [Fact]
    public void HomeButton_DisabledWhenTransferring()
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(RobotState.Transferring, RobotPhase.RotateToSrc, "LL", false, RobotErrorCode.Ok);
        var home = dlg.Controls.OfType<Button>().First(b => b.Text == "Home");
        Assert.False(home.Enabled);
    }

    [Fact]
    public void TransferButton_EnabledOnlyWhenIdle()
    {
        using var dlg = MakeDialog();
        var btn = dlg.Controls.OfType<Button>().First(b => b.Text == "Transfer");

        dlg.UpdateMirror(RobotState.Idle, RobotPhase.None, "park", false, RobotErrorCode.Ok);
        Assert.True(btn.Enabled);

        dlg.UpdateMirror(RobotState.Transferring, RobotPhase.RotateToSrc, "LL", false, RobotErrorCode.Ok);
        Assert.False(btn.Enabled);
    }

    [Fact]
    public void UpdateMirror_SetsStateLabel()
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(RobotState.Error, RobotPhase.None, "C1", true, RobotErrorCode.SlitTimeout);
        var lbl = dlg.Controls.OfType<Label>().First(l => l.Text.StartsWith("State:"));
        Assert.Contains("Error", lbl.Text);
    }

    [Fact]
    public void UpdateMirror_HasWafer_ShowsYes()
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(RobotState.Idle, RobotPhase.None, "park", hasWafer: true, RobotErrorCode.Ok);
        var lbl = dlg.Controls.OfType<Label>().First(l => l.Text.StartsWith("HasWafer:"));
        Assert.Contains("yes", lbl.Text);
    }

    [Fact]
    public void UpdateMirror_NoWafer_ShowsNo()
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(RobotState.Idle, RobotPhase.None, "park", hasWafer: false, RobotErrorCode.Ok);
        var lbl = dlg.Controls.OfType<Label>().First(l => l.Text.StartsWith("HasWafer:"));
        Assert.Contains("no", lbl.Text);
    }

    [Fact]
    public void UpdateMirror_ErrorCode_Ok_ShowsDash()
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(RobotState.Idle, RobotPhase.None, "park", false, RobotErrorCode.Ok);
        var lbl = dlg.Controls.OfType<Label>().First(l => l.Text.StartsWith("Error:"));
        Assert.Contains("-", lbl.Text);
    }

    [Fact]
    public void UpdateMirror_ErrorCode_NonOk_ShowsCodeName()
    {
        using var dlg = MakeDialog();
        dlg.UpdateMirror(RobotState.Error, RobotPhase.None, "LL", false, RobotErrorCode.RotateTimeout);
        var lbl = dlg.Controls.OfType<Label>().First(l => l.Text.StartsWith("Error:"));
        Assert.Contains("RotateTimeout", lbl.Text);
    }

    [Fact]
    public void SrcCombo_ContainsParkAndConfiguredFaces()
    {
        using var dlg = MakeDialog();
        var combo = dlg.Controls.OfType<ComboBox>().First();
        // "park" + "LL" + "C1" = 3 items
        Assert.Equal(3, combo.Items.Count);
    }

    [Fact]
    public void DstCombo_ContainsParkAndConfiguredFaces()
    {
        using var dlg = MakeDialog();
        var combos = dlg.Controls.OfType<ComboBox>().ToList();
        Assert.Equal(2, combos.Count);
        Assert.Equal(combos[0].Items.Count, combos[1].Items.Count);
    }

    [Fact]
    public void Dialog_NoFaces_StillConstructsWithParkOnly()
    {
        using var dlg = new TransferModuleServiceDialog("ROBOT", Array.Empty<(string, byte)>());
        var combos = dlg.Controls.OfType<ComboBox>().ToList();
        foreach (var combo in combos)
            Assert.Equal(1, combo.Items.Count);
    }
}
