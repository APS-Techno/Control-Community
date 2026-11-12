using APS.Controls.Theme;
using APS.RecipeModel.Ast;

#pragma warning disable WFO1000

namespace APS.RecipeModel.Blocks;

public sealed class ShutterBlock : BlockBase
{
    private readonly DarkComboBox _position;

    public ShutterBlock() : base("shutter", BlockTheme.HardwareCategory)
    {
        _position = new DarkComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = BlockTheme.Scale(90),
            Margin = new Padding(2),
        };
        FieldRow.Controls.Add(_position);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not ShutterStatement s) throw new ArgumentException(nameof(node));
        BoundNode = s;
        _position.Text = s.Position;
    }

    public override AstNode ToAst() => new ShutterStatement(_position.Text)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("shutter", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    public override void ApplyContext(IBlockContext? context)
    {
        var sources = context?.SourceNames ?? Array.Empty<string>();
        var prev = _position.Text;
        _position.Items.Clear();
        foreach (var s in sources) _position.Items.Add(s);
        _position.Text = prev;
    }
}
