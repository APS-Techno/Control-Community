namespace APS.Controls.Theme;

/// <summary>
/// The OS shell <c>MessageBox.Show</c> ignores our palette and shows a light
/// dialog on top of dark forms.
/// </summary>
public static class ThemedMessageBox
{
    public static DialogResult Show(
        IWin32Window? owner,
        string message,
        string title = "",
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        using var form = new ThemedMessageForm(message, title, buttons);
        return owner is null ? form.ShowDialog() : form.ShowDialog(owner);
    }

    public static DialogResult Show(
        string message,
        string title = "",
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
        => Show(null, message, title, buttons, icon);
}

internal sealed class ThemedMessageForm : Form
{
    private readonly Label _lblMessage;
    private readonly Button[] _buttons;

    public ThemedMessageForm(string message, string title, MessageBoxButtons buttons)
    {
        Text = title ?? string.Empty;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        Padding = new Padding(DpiScale.Px(20));
        ClientSize = new Size(DpiScale.Px(420), DpiScale.Px(160));

        _lblMessage = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(DpiScale.Px(380), 0),
            Location = new Point(DpiScale.Px(20), DpiScale.Px(20)),
            Text = message ?? string.Empty,
        };

        _buttons = BuildButtons(buttons);
        foreach (var b in _buttons) b.Anchor = AnchorStyles.Bottom;

        AcceptButton = _buttons[0];
        if (buttons is MessageBoxButtons.YesNo
                    or MessageBoxButtons.OKCancel
                    or MessageBoxButtons.YesNoCancel
                    or MessageBoxButtons.RetryCancel)
            CancelButton = _buttons[^1];

        Controls.Add(_lblMessage);
        foreach (var b in _buttons) Controls.Add(b);

        AdjustHeight();
        LayoutButtons();
        Resize += (_, _) => LayoutButtons();

        DarkModeHelper.FixControls(this);
    }

    private void AdjustHeight()
    {
        _lblMessage.PerformLayout();
        int required = _lblMessage.Bottom + DpiScale.Px(80);
        ClientSize = new Size(ClientSize.Width, Math.Max(required, DpiScale.Px(160)));
    }

    private static Button[] BuildButtons(MessageBoxButtons kind)
    {
        static Button Make(string text, DialogResult dr) => new()
        {
            Text = text,
            Size = new Size(DpiScale.Px(90), DpiScale.Px(30)),
            DialogResult = dr,
        };
        return kind switch
        {
            MessageBoxButtons.YesNo            => [Make("Yes", DialogResult.Yes),    Make("No", DialogResult.No)],
            MessageBoxButtons.OKCancel         => [Make("OK", DialogResult.OK),      Make("Cancel", DialogResult.Cancel)],
            MessageBoxButtons.YesNoCancel      => [Make("Yes", DialogResult.Yes),    Make("No", DialogResult.No), Make("Cancel", DialogResult.Cancel)],
            MessageBoxButtons.RetryCancel      => [Make("Retry", DialogResult.Retry), Make("Cancel", DialogResult.Cancel)],
            MessageBoxButtons.AbortRetryIgnore => [Make("Abort", DialogResult.Abort), Make("Retry", DialogResult.Retry), Make("Ignore", DialogResult.Ignore)],
            _                                  => [Make("OK", DialogResult.OK)],
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
}
