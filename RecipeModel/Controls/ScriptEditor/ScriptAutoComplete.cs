using APS.Controls.Theme;

namespace APS.RecipeModel.Controls.ScriptEditor;

public sealed class ScriptAutoComplete : IDisposable
{
    /// <summary>Commands not listed here don't trigger autocomplete.</summary>
    private static readonly Dictionary<string, string> CommandToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["valve"]    = "Valves",
        ["mfc"]      = "MFCs",
        ["pump"]     = "Pumps",
        ["stage"]    = "Stages",
        ["power"]    = "Power Supplies",
        ["ramp"]     = "Power Supplies",
        ["set"]      = "Power Supplies",
        ["warmup"]   = "Power Supplies",
        ["sensor"]   = "Sensors",
        ["pwait"]    = "Sensors",
        ["swait"]    = "Sensors",
        ["check"]    = "Sensors",
        ["shutter"]  = "Drives",
        ["holder"]   = "Drives",
        ["scan"]     = "Drives",
        ["scanR"]    = "Drives",
        ["rotate"]   = "Drives",
        ["bias"]     = "Power Supplies",
        ["biasArb"]  = "Power Supplies",
        ["call"]     = "Recipes",
        ["transfer"] = "Robot Faces",
    };

    private readonly RichTextBox _editor;
    private readonly ListBox _popup;
    private readonly List<string> _allDevices;
    private readonly Dictionary<string, List<string>> _devicesByCategory;
    private string _filter = "";
    private int _insertStart;
    private bool _active;
    private List<string> _activeDevices = [];
    private bool _variablesMode;
    private List<VariableItem> _activeVariables = [];

    public ScriptAutoComplete(RichTextBox editor, IEnumerable<string> devices,
        Dictionary<string, List<string>>? devicesByCategory = null)
    {
        _editor = editor;
        _allDevices = new List<string>(devices);
        _allDevices.Sort(StringComparer.OrdinalIgnoreCase);
        _devicesByCategory = devicesByCategory ?? [];

        _popup = new ListBox
        {
            Visible = false,
            Font = editor.Font,
            IntegralHeight = false,
            Size = new Size(DpiScale.Px(180), DpiScale.Px(120)),
            BorderStyle = BorderStyle.FixedSingle
        };

        _popup.Click += (_, _) => Accept();
        _editor.Controls.Add(_popup);

        _editor.KeyDown += OnKeyDown;
        _editor.KeyPress += OnKeyPress;
        _editor.MouseDown += (_, _) => Dismiss();
    }

    public void InsertDevice(string name)
    {
        int pos = _editor.SelectionStart;
        _editor.SelectionLength = 0;
        _editor.SelectedText = name;
    }

    public void ShowVariables(IEnumerable<VariableItem> items)
    {
        if (_active) Dismiss();

        _activeVariables = items.ToList();
        if (_activeVariables.Count == 0) return;

        _variablesMode = true;
        _insertStart = _editor.SelectionStart;
        _filter = "";

        var formatted = _activeVariables.Select(VariableProvider.Format).ToList();
        ShowPopup(formatted);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_active) return;

        switch (e.KeyCode)
        {
            case Keys.Down:
                if (_popup.SelectedIndex < _popup.Items.Count - 1)
                    _popup.SelectedIndex++;
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Up:
                if (_popup.SelectedIndex > 0)
                    _popup.SelectedIndex--;
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Tab:
            case Keys.Enter:
                Accept();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Escape:
                Dismiss();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void OnKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_active)
        {
            // '$' means the variable autocomplete will take over — dismiss
            // the device popup synchronously so it doesn't interfere.
            if (e.KeyChar == '$')
            {
                Dismiss();
                return;
            }

            // Let the character be typed first, then update filter on next tick
            _editor.BeginInvoke(UpdateFilter);
            return;
        }

        if (e.KeyChar == ' ')
        {
            _editor.BeginInvoke(TryActivate);
        }
    }

    private void TryActivate()
    {
        string cmd = GetCurrentLineCommand();
        if (!CommandToCategory.TryGetValue(cmd, out string? category))
            return;

        // "Robot Faces" is a synthetic category (not a real HW device list); when no
        // robot is configured it is simply absent, and we must NOT fall back to the
        // full device list — there are no faces to suggest.
        if (_devicesByCategory.TryGetValue(category, out var catDevices))
            _activeDevices = catDevices;
        else if (category == "Robot Faces")
            return;
        else
            _activeDevices = _allDevices;

        if (_activeDevices.Count == 0)
            return;

        _insertStart = _editor.SelectionStart;
        _filter = "";
        ShowPopup(_activeDevices);
    }

    private void UpdateFilter()
    {
        if (!_active) return;

        int pos = _editor.SelectionStart;
        if (pos < _insertStart)
        {
            Dismiss();
            return;
        }

        string text = _editor.Text;
        _filter = text.Substring(_insertStart, pos - _insertStart);

        if (_filter.Contains(' ') || _filter.Contains('\n'))
        {
            Dismiss();
            return;
        }

        if (_variablesMode)
        {
            var filtered = VariableProvider.Filter(_activeVariables, _filter).ToList();
            if (filtered.Count == 0) { Dismiss(); return; }
            ShowPopup(filtered.Select(VariableProvider.Format).ToList());
        }
        else
        {
            var filtered = _activeDevices
                .Where(d => d.StartsWith(_filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count == 0) { Dismiss(); return; }
            ShowPopup(filtered);
        }
    }

    private void ShowPopup(List<string> items)
    {
        _popup.BeginUpdate();
        _popup.Items.Clear();
        foreach (var item in items)
            _popup.Items.Add(item);
        _popup.EndUpdate();

        if (_popup.Items.Count > 0)
            _popup.SelectedIndex = 0;

        Point caretPos = _editor.GetPositionFromCharIndex(_editor.SelectionStart);
        int lineHeight = _editor.Font.Height;
        _popup.Location = new Point(caretPos.X, caretPos.Y + lineHeight + 2);

        if (_popup.Bottom > _editor.ClientSize.Height)
            _popup.Top = caretPos.Y - _popup.Height - 2;
        if (_popup.Right > _editor.ClientSize.Width)
            _popup.Left = _editor.ClientSize.Width - _popup.Width;

        _popup.Visible = true;
        _popup.BringToFront();
        _active = true;
    }

    private void Accept()
    {
        if (!_active || _popup.SelectedItem is not string selected) return;

        string toInsert;
        if (_variablesMode)
        {
            // Selected is "$NAME  :type  — default"; extract just NAME
            int spaceIdx = selected.IndexOf(' ');
            string display = spaceIdx > 0 ? selected[..spaceIdx] : selected;
            toInsert = display.StartsWith('$') ? display[1..] : display;
        }
        else
        {
            toInsert = selected;
        }

        _editor.Select(_insertStart, _editor.SelectionStart - _insertStart);
        _editor.SelectedText = toInsert;
        Dismiss();
    }

    private void Dismiss()
    {
        _active = false;
        _variablesMode = false;
        _popup.Visible = false;
    }

    private string GetCurrentLineCommand()
    {
        int pos = _editor.SelectionStart;
        if (pos <= 0) return "";

        string text = _editor.Text;
        int lineStart = text.LastIndexOf('\n', pos - 1) + 1;
        string lineText = text[lineStart..pos].TrimStart();

        int spaceIdx = lineText.IndexOf(' ');
        return spaceIdx > 0 ? lineText[..spaceIdx] : "";
    }

    public void Dispose()
    {
        _editor.KeyDown -= OnKeyDown;
        _editor.KeyPress -= OnKeyPress;
        _popup.Dispose();
    }
}
