using APS.Controls.Theme;
using APS.SystemControl.Plugin;

namespace APS.SystemControl.Core.Menus.Services;

/// <summary>
/// Wraps ThemedInputBox / ThemedMessageBox; marshals RunOnUi calls onto the
/// owner form. Concrete impl lives here because ThemedInputBox is in
/// VacuumControls — keeping the plug-in lib WinForms-free except for Form.
/// </summary>
internal sealed class UiServicesAdapter : IUiServices
{
    private readonly Form _owner;
    public UiServicesAdapter(Form owner) { _owner = owner; }

    public string? InputBox(string title, string prompt, string defaultValue = "")
        => ThemedInputBox.Show(_owner, prompt, title, defaultValue);

    public int? InputInt(string title, string prompt, int defaultValue)
    {
        string? raw = ThemedInputBox.Show(_owner, prompt, title, defaultValue.ToString());
        if (raw == null) return null;
        return int.TryParse(raw, out int v) ? v : null;
    }

    public bool Confirm(string title, string message)
        => ThemedMessageBox.Show(_owner, message, title,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public DialogResult ShowMessage(string title, string message)
        => ThemedMessageBox.Show(_owner, message, title);

    public void RunOnUi(Action action)
    {
        if (_owner.InvokeRequired) _owner.BeginInvoke(action);
        else action();
    }
}
