namespace APS.RecipeModel.Controls.ScriptEditor;

public sealed class ScriptEditorControl : UserControl
{
    private readonly BufferedRichTextBox _editor;
    private readonly LineNumberGutter _gutter;
    private readonly System.Windows.Forms.Timer _highlightTimer;
    private bool _isHighlighting;

    private ScriptAutoComplete? _autoComplete;
    private readonly HashSet<string> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _devicesByCategory = new(StringComparer.Ordinal);
    private readonly List<string> _embeddedRecipes = new();
    private List<VariableItem> _extraVariables = new();

    public RichTextBox InnerEditor => _editor;

    internal int GutterWidthForTest => _gutter.Width;

    internal IReadOnlyCollection<string> KnownDevicesForTest => _devices;
    internal IReadOnlyCollection<string> KnownEmbeddedRecipesForTest => _embeddedRecipes;
    internal IReadOnlyList<VariableItem> ExtraVariablesForTest => _extraVariables;

    /// <summary>Rebuilds the autocomplete popup; safe to call repeatedly.</summary>
    public void SetDevices(
        IEnumerable<string> all,
        IReadOnlyDictionary<string, IReadOnlyList<string>> byCategory)
    {
        _devices.Clear();
        foreach (var d in all) _devices.Add(d);

        _devicesByCategory.Clear();
        foreach (var (cat, list) in byCategory)
            _devicesByCategory[cat] = list.ToList();
        if (_embeddedRecipes.Count > 0)
            _devicesByCategory["Recipes"] = _embeddedRecipes.ToList();

        RebuildAutocomplete();
    }

    /// <summary>
    /// Push caller-supplied variables (e.g. recipe parameters) that should appear
    /// in the $-trigger popup alongside the built-in <c>_i</c> loop index.
    /// </summary>
    public void SetVariables(IEnumerable<VariableItem> extras)
    {
        _extraVariables = extras.ToList();
    }

    /// <summary>Embedded recipe names, offered after `call `.</summary>
    public void SetEmbeddedRecipes(IEnumerable<string> recipes)
    {
        _embeddedRecipes.Clear();
        _embeddedRecipes.AddRange(recipes);
        _devicesByCategory["Recipes"] = _embeddedRecipes.ToList();
        RebuildAutocomplete();
    }

    private void RebuildAutocomplete()
    {
        _autoComplete?.Dispose();
        _autoComplete = new ScriptAutoComplete(_editor, _devices, _devicesByCategory);
    }

    internal void ApplyHighlightingNow()
    {
        _highlightTimer.Stop();
        DoHighlight();
    }

    internal void StepFontSizeForTest(int direction)
    {
        SetFontSize(EditorFontSizeHelper.Step(_editor.Font.Size, direction));
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _editor.Text;
        set => _editor.Text = value ?? string.Empty;
    }

    public new event EventHandler? TextChanged;

    public ScriptEditorControl()
    {
        _editor = new BufferedRichTextBox
        {
            Dock        = DockStyle.Fill,
            Font        = new Font("Consolas", EditorFontSizeHelper.Default),
            AcceptsTab  = true,
            WordWrap    = false,
            DetectUrls  = false,
            BorderStyle = BorderStyle.None,
            BackColor   = Color.FromArgb(13, 27, 42),
            ForeColor   = Color.FromArgb(200, 214, 229),
        };
        _editor.TextChanged += (s, e) => TextChanged?.Invoke(this, e);

        _gutter = new LineNumberGutter(_editor) { Dock = DockStyle.Left };

        _editor.VScroll      += (_, _) => InvalidateGutter();
        _editor.TextChanged  += (_, _) => InvalidateGutter();
        _editor.Resize       += (_, _) => InvalidateGutter();
        _editor.FontChanged  += (_, _) => InvalidateGutter();

        _highlightTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            DoHighlight();
        };
        _editor.TextChanged += (_, _) =>
        {
            if (_isHighlighting) return;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        };

        _editor.MouseWheel += OnEditorWheel;
        _editor.KeyDown    += OnEditorKey;

        // WinForms dock layout processes children in REVERSE add order:
        // add Fill first, then Left, so the gutter sits to the left of the editor.
        Controls.Add(_editor);
        Controls.Add(_gutter);
        BackColor = _editor.BackColor;

        // Force handle creation so RichTextBox raises TextChanged on Text-setter
        // even when the control is used headless (e.g. in unit tests).
        _ = _editor.Handle;

        _autoComplete = new ScriptAutoComplete(_editor, _devices, _devicesByCategory);

        // $-trigger: show the variable suggestions popup. KeyPress fires BEFORE
        // the '$' is inserted into the buffer, so defer the popup via BeginInvoke.
        _editor.KeyPress += (_, e) =>
        {
            if (e.KeyChar != '$') return;
            _editor.BeginInvoke(new Action(() =>
            {
                var items = _extraVariables
                    .Concat(new[] { VariableProvider.LoopIndex })
                    .ToList();
                _autoComplete?.ShowVariables(items);
            }));
        };
    }

    private void InvalidateGutter()
    {
        if (_gutter.EnsureWidth()) PerformLayout();
        _gutter.Invalidate();
    }

    private void DoHighlight()
    {
        if (_isHighlighting) return;
        _isHighlighting = true;
        _editor.BeginUpdate();
        try
        {
            ScriptHighlighter.Apply(_editor);
        }
        finally
        {
            _editor.EndUpdate();
            _isHighlighting = false;
        }
    }

    private void OnEditorWheel(object? sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != Keys.Control) return;
        int direction = e.Delta > 0 ? +1 : -1;
        SetFontSize(EditorFontSizeHelper.Step(_editor.Font.Size, direction));
        if (e is HandledMouseEventArgs hme) hme.Handled = true;
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        if (!e.Control) return;
        float? next = e.KeyCode switch
        {
            Keys.Oemplus or Keys.Add        => EditorFontSizeHelper.Step(_editor.Font.Size, +1),
            Keys.OemMinus or Keys.Subtract  => EditorFontSizeHelper.Step(_editor.Font.Size, -1),
            Keys.D0 or Keys.NumPad0         => EditorFontSizeHelper.Default,
            _ => null
        };
        if (next.HasValue)
        {
            SetFontSize(next.Value);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void SetFontSize(float size)
    {
        size = EditorFontSizeHelper.Clamp(size);
        if (Math.Abs(_editor.Font.Size - size) < 0.01f) return;
        var old = _editor.Font;
        // Do NOT dispose old Font — other controls (e.g. ScriptAutoComplete popup)
        // may still reference it. GC finalizer releases the GDI handle.
        _editor.Font = new Font(old.FontFamily, size, old.Style);
        // Font replacement drops current char formatting — re-highlight.
        ApplyHighlightingNow();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoComplete?.Dispose();
            _highlightTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
