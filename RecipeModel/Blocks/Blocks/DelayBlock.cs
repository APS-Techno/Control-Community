using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class DelayBlock : BlockBase
{
    private readonly ParamOrLiteralEditor _seconds;
    private readonly CheckBox _hasCaption;
    private readonly TextBox _caption;

    public DelayBlock() : base("delay", BlockTheme.TimingCategory)
    {
        _seconds = new ParamOrLiteralEditor();
        _hasCaption = new CheckBox
        {
            Text = "caption?",
            AutoSize = true,
            Margin = new Padding(4, 4, 2, 2),
            ForeColor = BlockTheme.BlockTextOnCat,
        };
        _caption = new TextBox { Width = BlockTheme.Scale(120), Margin = new Padding(2), Enabled = false };
        _hasCaption.CheckedChanged += (_, _) => _caption.Enabled = _hasCaption.Checked;

        FieldRow.Controls.Add(_seconds);
        FieldRow.Controls.Add(_hasCaption);
        FieldRow.Controls.Add(_caption);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not DelayStatement d) throw new ArgumentException(nameof(node));
        BoundNode = d;
        _seconds.Value = d.Seconds;
        _hasCaption.Checked = d.Caption is not null;
        _caption.Text = d.Caption ?? "";
    }

    public override AstNode ToAst() => new DelayStatement(
        _seconds.Value,
        _hasCaption.Checked ? _caption.Text : null)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("delay", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
