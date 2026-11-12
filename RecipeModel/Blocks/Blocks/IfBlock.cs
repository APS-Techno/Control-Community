using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class IfBlock : ContainerBlockBase, IContainerBlock
{
    private readonly TextBox _lhs;
    private readonly APS.Controls.Theme.DarkComboBox _op;
    private readonly ParamOrLiteralEditor _rhs;

    public BlockStackControl ThenStack => (BlockStackControl)Mouths[0];
    public BlockStackControl ElseStack => (BlockStackControl)Mouths[1];

    public IfBlock() : base("if", BlockTheme.FlowCategory)
    {
        _lhs = new TextBox { Width = BlockTheme.Scale(60), Margin = new Padding(2) };
        _op = new APS.Controls.Theme.DarkComboBox
        {
            Width = BlockTheme.Scale(50),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(2),
        };
        _op.Items.AddRange(new object[] { "=", "!=", "<", "<=", ">", ">=" });
        _op.SelectedIndex = 0;
        _rhs = new ParamOrLiteralEditor();

        FieldRow.Controls.Add(_lhs);
        FieldRow.Controls.Add(_op);
        FieldRow.Controls.Add(_rhs);

        AddMouth();

        var elseLabel = new Label
        {
            Text = "else",
            Dock = DockStyle.Top,
            Height = BlockTheme.Scale(18),
            ForeColor = BlockTheme.BlockTextOnCat,
            BackColor = BlockTheme.FlowCategory,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(BlockTheme.MouthIndent + BlockTheme.Scale(4), 0, 0, 0),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        };
        BodyPanel.Controls.Add(elseLabel);

        AddMouth();
    }

    public override void BindTo(AstNode node)
    {
        if (node is not IfElseContainer ie) throw new ArgumentException("expected IfElseContainer", nameof(node));
        BoundNode = ie;
        _lhs.Text = ie.Condition.Lhs;
        int idx = _op.Items.IndexOf(ie.Condition.Op);
        _op.SelectedIndex = idx >= 0 ? idx : 0;
        _rhs.Value = ie.Condition.Rhs;

        ThenStack.Clear();
        foreach (var n in ie.Body)
            ThenStack.Insert(ThenStack.Blocks.Count, BlockFactory.CreateFor(n));

        ElseStack.Clear();
        if (ie.ElseBody.HasValue)
            foreach (var n in ie.ElseBody.Value)
                ElseStack.Insert(ElseStack.Blocks.Count, BlockFactory.CreateFor(n));

        RestoreCollapsedFromTrivia();
    }

    public override AstNode ToAst()
    {
        var id = BoundNode?.Id ?? NodeId.FromPosition("if", "unbound", 0);
        var bn = BoundNode as IfElseContainer;

        var thenBuilder = ImmutableArray.CreateBuilder<AstNode>();
        foreach (var b in ThenStack.Blocks) thenBuilder.Add(b.ToAst());

        ImmutableArray<AstNode>? elseBody = null;
        if (ElseStack.Blocks.Count > 0)
        {
            var elseBuilder = ImmutableArray.CreateBuilder<AstNode>();
            foreach (var b in ElseStack.Blocks) elseBuilder.Add(b.ToAst());
            elseBody = elseBuilder.ToImmutable();
        }

        return new IfElseContainer
        {
            Id = id,
            Body = thenBuilder.ToImmutable(),
            ElseBody = elseBody,
            Condition = new ConditionExpr(
                _lhs.Text,
                _op.SelectedItem?.ToString() ?? "=",
                _rhs.Value),
            LeadingTrivia = bn?.LeadingTrivia ?? Trivia.Empty,
            TrailingTrivia = bn?.TrailingTrivia ?? Trivia.Empty,
            ElseTrivia = bn?.ElseTrivia ?? Trivia.Empty,
            EndTrivia = bn?.EndTrivia ?? Trivia.Empty,
        };
    }

    internal ConditionExpr ConditionForTesting => new ConditionExpr(
        _lhs.Text,
        _op.SelectedItem?.ToString() ?? "=",
        _rhs.Value);

    IReadOnlyList<BlockStackControl> IContainerBlock.Mouths =>
        new[] { ThenStack, ElseStack };

    IReadOnlyList<System.Collections.Immutable.ImmutableArray<APS.RecipeModel.Ast.AstNode>>
        IContainerBlock.ExtractBodies(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not IfElseContainer ie)
            throw new ArgumentException("expected IfElseContainer", nameof(node));
        return new[]
        {
            ie.Body,
            ie.ElseBody ?? System.Collections.Immutable.ImmutableArray<APS.RecipeModel.Ast.AstNode>.Empty,
        };
    }

    void IContainerBlock.BindHeaderTo(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not IfElseContainer ie) throw new ArgumentException("expected IfElseContainer", nameof(node));
        BoundNode = ie;
        _lhs.Text = ie.Condition.Lhs;
        int idx = _op.Items.IndexOf(ie.Condition.Op);
        _op.SelectedIndex = idx >= 0 ? idx : 0;
        _rhs.Value = ie.Condition.Rhs;
    }
}
