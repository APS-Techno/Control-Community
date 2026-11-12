using APS.Controls.Theme;
using APS.RecipeModel.Ast;

#pragma warning disable WFO1000

namespace APS.RecipeModel.Blocks;

/// <summary>Face dropdowns are populated from the "Robot Faces" context category,
/// degrading to free-text when no faces are configured.</summary>
public sealed class TransferBlock : BlockBase
{
    private readonly DarkComboBox _src;
    private readonly DarkComboBox _dst;

    public TransferBlock() : base("transfer", BlockTheme.HardwareCategory)
    {
        _src = NewFaceCombo();
        _dst = NewFaceCombo();
        FieldRow.Controls.Add(_src);
        FieldRow.Controls.Add(_dst);
    }

    private static DarkComboBox NewFaceCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDown,
        Width = BlockTheme.Scale(80),
        Margin = new Padding(2),
    };

    public override void BindTo(AstNode node)
    {
        if (node is not TransferStatement t) throw new ArgumentException(nameof(node));
        BoundNode = t;
        _src.Text = t.Src.ToDslToken();
        _dst.Text = t.Dst.ToDslToken();
    }

    public override AstNode ToAst() => new TransferStatement(ToField(_src.Text), ToField(_dst.Text))
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("transfer", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    private static NodeField ToField(string text) =>
        text.StartsWith('$') ? NodeField.Param(text) : NodeField.Literal(text);

    public override void ApplyContext(IBlockContext? context)
    {
        var faces = context?.DeviceNames("Robot Faces") ?? Array.Empty<string>();
        Repopulate(_src, faces);
        Repopulate(_dst, faces);
    }

    private static void Repopulate(DarkComboBox combo, IReadOnlyList<string> faces)
    {
        var prev = combo.Text;
        combo.Items.Clear();
        foreach (var f in faces) combo.Items.Add(f);
        combo.Text = prev;
    }
}
