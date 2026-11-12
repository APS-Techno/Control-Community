using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

/// <summary>
/// The runtime supports several <c>bias</c> forms
/// (<c>bias VALUE</c>, <c>bias set CH PARAM VALUE</c>, <c>bias out CH Arb VALUE</c>),
/// so the block edits the argument list as raw, space-separated text and preserves
/// it verbatim through the AST. Tokens beginning with <c>$</c> round-trip as
/// parameter references.
/// </summary>
public sealed class BiasBlock : BlockBase
{
    private readonly TextBox _args;

    public BiasBlock() : base("bias", BlockTheme.PowerCategory)
    {
        _args = new TextBox { Width = BlockTheme.Scale(220), Margin = new Padding(2) };
        FieldRow.Controls.Add(_args);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not BiasStatement b) throw new ArgumentException(nameof(node));
        BoundNode = b;
        _args.Text = string.Join(' ', b.Args.Select(a => a.ToDslToken()));
    }

    public override AstNode ToAst() => new BiasStatement(ParseArgs(_args.Text))
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("bias", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    private static ImmutableArray<NodeField> ParseArgs(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return ImmutableArray<NodeField>.Empty;
        var b = ImmutableArray.CreateBuilder<NodeField>(tokens.Length);
        foreach (var tok in tokens)
            b.Add(tok.StartsWith('$') && tok.Length > 1
                ? NodeField.Param(tok[1..])
                : NodeField.Literal(tok));
        return b.ToImmutable();
    }
}
