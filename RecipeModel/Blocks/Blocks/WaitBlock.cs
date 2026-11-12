using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class WaitBlock : BlockBase
{
    private readonly ParamOrLiteralEditor _seconds;
    private readonly CheckBox _hasCaption;
    private readonly TextBox _caption;

    public WaitBlock() : base("wait", BlockTheme.TimingCategory)
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
        if (node is not WaitStatement w) throw new ArgumentException(nameof(node));
        BoundNode = w;
        _seconds.Value = w.Seconds;
        _hasCaption.Checked = w.Caption is not null;
        _caption.Text = w.Caption ?? "";
    }

    public override AstNode ToAst() => new WaitStatement(
        _seconds.Value,
        _hasCaption.Checked ? _caption.Text : null)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("wait", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
