using System.Collections.Immutable;
using System.ComponentModel;

namespace APS.RecipeModel.Blocks;

public sealed class CsvListField : UserControl
{
    private readonly TextBox _box;

    public event Action? ValueChanged;

    public CsvListField()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(2);
        BackColor = BlockTheme.BlockBodyBg;

        _box = new TextBox { Width = BlockTheme.Scale(180), Margin = new Padding(0) };
        _box.TextChanged += (_, _) => ValueChanged?.Invoke();

        Controls.Add(_box);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ImmutableArray<string> Value
    {
        get => _box.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToImmutableArray();
        set => _box.Text = string.Join(", ", value);
    }
}
