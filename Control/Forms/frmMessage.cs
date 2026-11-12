using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.Forms;

/// <summary>
/// Modal dialog for displaying a message to the operator during recipe execution.
/// Used by the 'message' recipe command. Recipe execution pauses until the user
/// clicks OK.
/// </summary>
public class frmMessage : Form
{
    private readonly Label lblMessage;
    private readonly Button[] _buttons;

    public frmMessage() : this(MessageBoxButtons.OK) { }

    public frmMessage(MessageBoxButtons buttons)
    {
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "Recipe Message";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(DpiScale.Px(400), DpiScale.Px(200));
        Padding = new Padding(DpiScale.Px(20));

        lblMessage = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(DpiScale.Px(350), 0),
            Font = ScaledFont.Create(10f),
            Location = new Point(DpiScale.Px(20), DpiScale.Px(20)),
            Text = ""
        };

        _buttons = BuildButtons(buttons);
        foreach (var b in _buttons)
            b.Anchor = AnchorStyles.Bottom;

        AcceptButton = _buttons[0];
        if (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.OKCancel)
            CancelButton = _buttons[^1];

        Controls.Add(lblMessage);
        foreach (var b in _buttons) Controls.Add(b);
        LayoutButtons();

        Resize += (_, _) => LayoutButtons();

        DarkModeHelper.FixControls(this);
    }

    private static Button[] BuildButtons(MessageBoxButtons kind)
    {
        Button Make(string text, DialogResult dr) => new()
        {
            Text = text,
            Size = new Size(DpiScale.Px(90), DpiScale.Px(30)),
            DialogResult = dr
        };
        return kind switch
        {
            MessageBoxButtons.YesNo    => [Make("Yes", DialogResult.Yes), Make("No", DialogResult.No)],
            MessageBoxButtons.OKCancel => [Make("OK", DialogResult.OK),   Make("Cancel", DialogResult.Cancel)],
            _                          => [Make("OK", DialogResult.OK)],
        };
    }

    private void LayoutButtons()
    {
        int gap = DpiScale.Px(12);
        int totalWidth = _buttons.Sum(b => b.Width) + gap * (_buttons.Length - 1);
        int x = (ClientSize.Width - totalWidth) / 2;
        int y = ClientSize.Height - _buttons[0].Height - DpiScale.Px(20);
        foreach (var b in _buttons)
        {
            b.Location = new Point(x, y);
            x += b.Width + gap;
        }
    }

    private void SetMessage(string message)
    {
        lblMessage.Text = message;

        lblMessage.PerformLayout();
        int requiredHeight = lblMessage.Bottom + DpiScale.Px(80);
        int minHeight = DpiScale.Px(200);
        ClientSize = new Size(ClientSize.Width, Math.Max(requiredHeight, minHeight));
        LayoutButtons();
    }

    public static void ShowMessage(string message)
    {
        using var form = new frmMessage();
        form.SetMessage(message);
        form.ShowDialog();
    }

    public static void ShowMessage(IWin32Window owner, string message)
    {
        using var form = new frmMessage();
        form.SetMessage(message);
        form.ShowDialog(owner);
    }

    /// <summary>
    /// Themed Yes/No confirm dialog. Drop-in for
    /// <c>MessageBox.Show(..., MessageBoxButtons.YesNo, ...)</c>.
    /// <paramref name="defaultNo"/> makes No the Enter/focused button, so a stray
    /// keypress cannot confirm.
    /// </summary>
    public static DialogResult Confirm(IWin32Window owner, string message, string title, bool defaultNo = false)
    {
        using var form = new frmMessage(MessageBoxButtons.YesNo) { Text = title };
        form.SetMessage(message);
        if (defaultNo)
        {
            form.AcceptButton = form._buttons[^1];
            form.ActiveControl = form._buttons[^1];
        }
        return form.ShowDialog(owner);
    }
}
