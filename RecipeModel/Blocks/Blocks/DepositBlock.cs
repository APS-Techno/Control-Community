using APS.Controls.Theme;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

#pragma warning disable WFO1000

/// <summary>
/// Retract: editable combo seeded with "Home" + sources.
/// All combos accept free text; values save as <see cref="NodeField.Literal"/>.
/// </summary>
public sealed class DepositBlock : BlockBase
{
    private readonly DarkComboBox _source;
    private readonly ParamOrLiteralEditor _time;
    private readonly DarkComboBox _retract;

    public DepositBlock() : base("deposit", BlockTheme.ProcessCategory)
    {
        _source = NewCombo(width: 90);
        _time = new ParamOrLiteralEditor();
        _retract = NewCombo(width: 90);

        FieldRow.Controls.Add(NewLabel("Source"));
        FieldRow.Controls.Add(_source);
        FieldRow.Controls.Add(NewLabel("Time"));
        FieldRow.Controls.Add(_time);
        FieldRow.Controls.Add(NewLabel("Retract"));
        FieldRow.Controls.Add(_retract);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not DepositStatement d) throw new ArgumentException(nameof(node));
        BoundNode = d;
        _source.Text  = FieldDisplay(d.Position);
        _time.Value   = d.Time;
        _retract.Text = FieldDisplay(d.Retract);
    }

    public override AstNode ToAst() => new DepositStatement(
        NodeField.Literal(_source.Text),
        _time.Value,
        NodeField.Literal(_retract.Text))
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("deposit", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    public override void ApplyContext(IBlockContext? context)
    {
        var sources = context?.SourceNames ?? Array.Empty<string>();
        var prevSrc = _source.Text;
        var prevRet = _retract.Text;

        _source.Items.Clear();
        foreach (var s in sources) _source.Items.Add(s);
        _source.Text = prevSrc;

        _retract.Items.Clear();
        _retract.Items.Add("Home");
        foreach (var s in sources) _retract.Items.Add(s);
        _retract.Text = prevRet;

        _time.SetAvailableParams(context?.ParameterNames ?? Array.Empty<string>());
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = BlockTheme.LabelForeground,
        Margin = new Padding(6, 5, 2, 0),
        Font = new Font("Segoe UI", 8.5f),
    };

    private static DarkComboBox NewCombo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDown,
        Width = width,
        Margin = new Padding(0, 2, 4, 0),
    };

    private static string FieldDisplay(NodeField f)
        => f.IsParamRef ? "$" + f.Value : f.Value;

    // Test seams, public for test simplicity.

    public IEnumerable<string> SourceItemsForTest =>
        _source.Items.Cast<object>().Select(o => o?.ToString() ?? "");

    public IEnumerable<string> RetractItemsForTest =>
        _retract.Items.Cast<object>().Select(o => o?.ToString() ?? "");

    public void SetSourceForTest(string value) => _source.Text = value;
}
