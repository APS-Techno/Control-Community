namespace APS.SystemControl.Plugin;

/// <summary>
/// Themed dialog services exposed to menu contributors. Wraps the existing
/// ThemedInputBox / ThemedMessageBox helpers so contributors don't need a
/// VacuumControls reference (the plug-in lib stays minimal).
/// </summary>
public interface IUiServices
{
    /// <summary>
    /// Themed text input. Returns the entered string, or null on Cancel.
    /// (An empty string is a valid distinct outcome from cancellation.)
    /// </summary>
    string? InputBox(string title, string prompt, string defaultValue = "");

    /// <summary>
    /// Themed integer input. Returns parsed int on OK, null on Cancel or
    /// non-numeric input.
    /// </summary>
    int? InputInt(string title, string prompt, int defaultValue);

    bool Confirm(string title, string message);

    DialogResult ShowMessage(string title, string message);

    /// <summary>
    /// Marshal an action onto the UI thread. Use when a contributor's OnClick
    /// might be invoked from a non-UI thread (e.g. ItemsChanged handler).
    /// </summary>
    void RunOnUi(Action action);
}
