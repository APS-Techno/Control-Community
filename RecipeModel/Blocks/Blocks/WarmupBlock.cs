using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

#pragma warning disable WFO1000

public sealed class WarmupBlock : BlockBase
{
    private readonly ParamOrLiteralEditor _time;
    private readonly SourceListField _sources;

    public WarmupBlock() : base("warmup", BlockTheme.ProcessCategory)
    {
        _time = new ParamOrLiteralEditor();
        _sources = new SourceListField();

        FieldRow.Controls.Add(NewLabel("Time"));
        FieldRow.Controls.Add(_time);
        FieldRow.Controls.Add(NewLabel("Sources"));
        FieldRow.Controls.Add(_sources);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not WarmupStatement w) throw new ArgumentException(nameof(node));
        BoundNode = w;
        _time.Value = w.Time;
        _sources.Value = w.Sources;
    }

    public override AstNode ToAst() => new WarmupStatement(_time.Value, _sources.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("warmup", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    public override void ApplyContext(IBlockContext? context)
    {
        _time.SetAvailableParams(context?.ParameterNames ?? Array.Empty<string>());
        _sources.SetAvailableSources(context?.SourceNames ?? Array.Empty<string>());
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = BlockTheme.LabelForeground,
        Margin = new Padding(6, 5, 2, 0),
        Font = new Font("Segoe UI", 8.5f),
    };

    public void ToggleSourceForTest(string name, bool isChecked) =>
        _sources.SetCheckedForTest(name, isChecked);

    public void SetSourcesByTextForTest(string text) => _sources.Value =
        System.Collections.Immutable.ImmutableArray.CreateRange(
            text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
}
