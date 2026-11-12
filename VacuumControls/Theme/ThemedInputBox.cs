namespace APS.Controls.Theme;

/// <summary>
/// Themed equivalent of <c>Microsoft.VisualBasic.Interaction.InputBox</c>.
/// Returns <c>null</c> when the operator cancels; an empty string is a valid
/// distinct outcome.
/// </summary>
public static class ThemedInputBox
{
    public static string? Show(
        IWin32Window? owner,
        string prompt,
        string title = "",
        string defaultValue = "")
    {
        using var form = new ThemedInputForm(prompt, title, defaultValue);
        var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
        return result == DialogResult.OK ? form.Value : null;
    }

    public static string? Show(
        string prompt,
        string title = "",
        string defaultValue = "")
        => Show(null, prompt, title, defaultValue);
}

internal sealed class ThemedInputForm : Form
{
    private readonly Label _lblPrompt;
    private readonly TextBox _txtInput;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    public string Value => _txtInput.Text;

    public ThemedInputForm(string prompt, string title, string defaultValue)
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

        _lblPrompt = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(DpiScale.Px(380), 0),
            Location = new Point(DpiScale.Px(20), DpiScale.Px(20)),
            Text = prompt ?? string.Empty,
        };

        _txtInput = new TextBox
        {
            Location = new Point(DpiScale.Px(20), DpiScale.Px(60)),
            Width = DpiScale.Px(380),
            Text = defaultValue ?? string.Empty,
        };
        _txtInput.SelectAll();

        _btnOk     = new Button { Text = "OK",     Size = new Size(DpiScale.Px(90), DpiScale.Px(30)), DialogResult = DialogResult.OK     };
        _btnCancel = new Button { Text = "Cancel", Size = new Size(DpiScale.Px(90), DpiScale.Px(30)), DialogResult = DialogResult.Cancel };

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        Controls.Add(_lblPrompt);
        Controls.Add(_txtInput);
        Controls.Add(_btnOk);
        Controls.Add(_btnCancel);

        AdjustHeight();
        LayoutControls();
        Resize += (_, _) => LayoutControls();

        DarkModeHelper.FixControls(this);

        Shown += (_, _) => _txtInput.Focus();
    }

    private void AdjustHeight()
    {
        _lblPrompt.PerformLayout();
        _txtInput.Top = _lblPrompt.Bottom + DpiScale.Px(12);
        int required = _txtInput.Bottom + DpiScale.Px(80);
        ClientSize = new Size(ClientSize.Width, Math.Max(required, DpiScale.Px(160)));
    }

    private void LayoutControls()
    {
        int gap = DpiScale.Px(12);
        int totalWidth = _btnOk.Width + _btnCancel.Width + gap;
        int x = (ClientSize.Width - totalWidth) / 2;
        int y = ClientSize.Height - _btnOk.Height - DpiScale.Px(20);
        _btnOk.Location     = new Point(x, y);
        _btnCancel.Location = new Point(x + _btnOk.Width + gap, y);
    }
}
