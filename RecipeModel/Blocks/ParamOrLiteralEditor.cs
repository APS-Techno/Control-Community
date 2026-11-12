using System.ComponentModel;
using APS.Controls.Theme;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

#pragma warning disable WFO1000

/// <summary>Shows a literal textbox by default; a small "$" toggle switches to a dropdown of
/// available parameter names.</summary>
[DesignerCategory("code")]
public sealed class ParamOrLiteralEditor : UserControl
{
    private readonly TextBox _literalBox;
    private readonly APS.Controls.Theme.DarkComboBox _paramBox;
    private readonly Label _toggle;
    private bool _isParam;

    public event Action? ValueChanged;

    public ParamOrLiteralEditor()
    {
        SuspendLayout();
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(2);
        BackColor = BlockTheme.BlockBodyBg;

        _literalBox = new TextBox { Width = BlockTheme.Scale(60), Margin = new Padding(0) };
        _literalBox.TextChanged += (_, _) => ValueChanged?.Invoke();

        _paramBox = new APS.Controls.Theme.DarkComboBox { Width = BlockTheme.Scale(90), Margin = new Padding(0), Visible = false,
            DropDownStyle = ComboBoxStyle.DropDownList };
        _paramBox.SelectedIndexChanged += (_, _) => RaiseValueChanged();

        _toggle = new Label
        {
            Text = "$",
            Width = BlockTheme.Scale(22),
            Height = BlockTheme.Scale(22),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(2, 0, 0, 0),
            BackColor = Color.FromArgb(30, 44, 66),
            ForeColor = Color.FromArgb(200, 214, 229),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _toggle.Click += (_, _) => SetIsParam(!_isParam);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = BlockTheme.BlockBodyBg,
        };
        row.Controls.Add(_literalBox);
        row.Controls.Add(_paramBox);
        row.Controls.Add(_toggle);
        Controls.Add(row);
        ResumeLayout();
    }

    /// <summary>The currently selected parameter is preserved (re-added if it is no longer in
    /// the list) so re-applying a <see cref="BlockContext"/> never blanks a bound <c>$param</c>;
    /// <see cref="ValueChanged"/> is not raised because the value does not change.</summary>
    public void SetAvailableParams(IEnumerable<string> names)
    {
        string? selected = _paramBox.SelectedItem?.ToString();
        _suppressValueChanged = true;
        try
        {
            _paramBox.Items.Clear();
            foreach (var n in names) _paramBox.Items.Add(n);
            if (!string.IsNullOrEmpty(selected))
            {
                int idx = _paramBox.Items.IndexOf(selected);
                if (idx < 0) { _paramBox.Items.Add(selected); idx = _paramBox.Items.Count - 1; }
                _paramBox.SelectedIndex = idx;
            }
        }
        finally { _suppressValueChanged = false; }
    }

    private bool _suppressValueChanged;

    private void RaiseValueChanged()
    {
        if (!_suppressValueChanged) ValueChanged?.Invoke();
    }

    public NodeField Value
    {
        get => _isParam
            ? NodeField.Param(_paramBox.SelectedItem?.ToString() ?? "")
            : NodeField.Literal(_literalBox.Text);
        set
        {
            SetIsParam(value.IsParamRef);
            if (value.IsParamRef)
            {
                int idx = _paramBox.Items.IndexOf(value.Value);
                if (idx < 0) { _paramBox.Items.Add(value.Value); idx = _paramBox.Items.Count - 1; }
                _paramBox.SelectedIndex = idx;
            }
            else
            {
                _literalBox.Text = value.Value;
            }
        }
    }

    private void SetIsParam(bool isParam)
    {
        _isParam = isParam;
        _literalBox.Visible = !isParam;
        _paramBox.Visible = isParam;
        _toggle.Text = isParam ? "#" : "$";
        ValueChanged?.Invoke();
    }
}
