using System.Collections.Immutable;

namespace APS.RecipeModel.Blocks;

#pragma warning disable WFO1000

/// <summary>
/// Items not in the catalog are preserved in <see cref="Value"/> so users can
/// hand-type custom entries before the catalog is wired.
/// </summary>
public sealed class SourceListField : UserControl
{
    private readonly TextBox _display;
    private readonly Button _dropdown;
    private readonly CheckedListBox _popupList;
    private ToolStripDropDown? _popup;
    private ImmutableArray<string> _value = ImmutableArray<string>.Empty;
    private string[] _available = Array.Empty<string>();
    private bool _suppressDisplayEvent;

    public event Action? ValueChanged;

    public SourceListField()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(2);
        BackColor = BlockTheme.BlockBodyBg;

        _display = new TextBox { Width = BlockTheme.Scale(160), Margin = new Padding(0) };
        _display.TextChanged += (_, _) =>
        {
            if (_suppressDisplayEvent) return;
            _value = ParseDisplay(_display.Text);
            ValueChanged?.Invoke();
        };

        _dropdown = new Button
        {
            Text = "▾",
            Width = BlockTheme.Scale(22),
            Height = BlockTheme.Scale(22),
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
        };
        _dropdown.Click += (_, _) => ShowPopup();

        _popupList = new CheckedListBox
        {
            CheckOnClick = true,
            BorderStyle = BorderStyle.None,
            Width = BlockTheme.Scale(160),
            Height = BlockTheme.Scale(140),
        };
        _popupList.ItemCheck += OnItemCheck;

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = BlockTheme.BlockBodyBg,
        };
        row.Controls.Add(_display);
        row.Controls.Add(_dropdown);
        Controls.Add(row);
    }

    public ImmutableArray<string> Value
    {
        get => _value;
        set
        {
            _value = value;
            _suppressDisplayEvent = true;
            _display.Text = string.Join(", ", _value);
            _suppressDisplayEvent = false;
        }
    }

    public string DisplayTextForTest => _display.Text;

    public void SetAvailableSources(IEnumerable<string> names)
    {
        _available = names.ToArray();
        _popupList.Items.Clear();
        foreach (var n in _available)
            _popupList.Items.Add(n, _value.Contains(n));
    }

    public void SetCheckedForTest(string name, bool isChecked)
    {
        int idx = _popupList.Items.IndexOf(name);
        if (idx < 0)
        {
            _popupList.Items.Add(name, isChecked);
            idx = _popupList.Items.Count - 1;
        }
        _popupList.SetItemChecked(idx, isChecked);
        RebuildValueFromChecks(skipIndex: -1);
    }

    private void ShowPopup()
    {
        _popup?.Dispose();
        var host = new ToolStripControlHost(_popupList) { Margin = Padding.Empty, Padding = Padding.Empty };
        _popup = new ToolStripDropDown { Padding = Padding.Empty };
        _popup.Items.Add(host);
        _popup.Show(this, new Point(0, Height));
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        // CheckedItems isn't updated until after this handler returns; pass the
        // pending change via skipIndex/skipState so we compose the correct value.
        RebuildValueFromChecks(skipIndex: e.Index, skipState: e.NewValue == CheckState.Checked);
    }

    private void RebuildValueFromChecks(int skipIndex, bool skipState = false)
    {
        var b = ImmutableArray.CreateBuilder<string>();
        for (int i = 0; i < _popupList.Items.Count; i++)
        {
            string name = _popupList.Items[i].ToString() ?? "";
            bool isChecked = i == skipIndex ? skipState : _popupList.GetItemChecked(i);
            if (isChecked) b.Add(name);
        }
        // Preserve any "custom" items already in _value that the popup doesn't know about.
        foreach (var v in _value)
            if (!_available.Contains(v) && !b.Contains(v)) b.Add(v);

        _value = b.ToImmutable();
        _suppressDisplayEvent = true;
        _display.Text = string.Join(", ", _value);
        _suppressDisplayEvent = false;
        ValueChanged?.Invoke();
    }

    private static ImmutableArray<string> ParseDisplay(string text) => text
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToImmutableArray();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup?.Dispose();
            _popup = null;
            // _popupList is hosted inside _popup via ToolStripControlHost; the host
            // disposes its hosted control, so disposing _popup also disposes _popupList.
        }
        base.Dispose(disposing);
    }
}
