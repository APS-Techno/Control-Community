using APS.Controls.Theme;
using APS.RecipeModel.Ast;

#pragma warning disable WFO1000

namespace APS.RecipeModel.Blocks;

/// <summary>The dropdown degrades to free-text so a <c>$param</c> round-trips.</summary>
public sealed class StateBlock : BlockBase
{
    private readonly DarkComboBox _state;

    public StateBlock() : base("state", BlockTheme.FlowCategory)
    {
        _state = new DarkComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = BlockTheme.Scale(110),
            Margin = new Padding(2),
        };
        _state.Items.AddRange(new object[] { "startup", "idle", "processing" });
        _state.SelectedIndex = 0;
        FieldRow.Controls.Add(_state);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not StateStatement s) throw new ArgumentException(nameof(node));
        BoundNode = s;
        _state.Text = s.State.ToDslToken();
    }

    public override AstNode ToAst() => new StateStatement(ToField(_state.Text))
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("state", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    private static NodeField ToField(string text) =>
        text.StartsWith('$') ? NodeField.Param(text) : NodeField.Literal(text);
}
