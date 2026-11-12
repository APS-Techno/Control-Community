using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public static class BlockFactory
{
    public static BlockBase CreateFor(AstNode node, IBlockContext? context = null)
    {
        BlockBase block = node switch
        {
            ValveStatement    => new ValveBlock(),
            MfcStatement      => new MfcBlock(),
            PumpStatement     => new PumpBlock(),
            StageStatement    => new StageBlock(),
            ShutterStatement  => new ShutterBlock(),
            HolderStatement   => new HolderBlock(),
            ScanStatement     => new ScanBlock(),
            MessageStatement  => new MessageBlock(),
            TransferStatement => new TransferBlock(),
            HomeStatement     => new HomeBlock(),
            AbortStatement    => new AbortBlock(),
            BiasStatement     => new BiasBlock(),
            BiasArbStatement  => new BiasArbBlock(),
            PowerStatement    => new PowerBlock(),
            RampStatement     => new RampBlock(),
            SetStatement      => new SetBlock(),
            WarmupStatement   => new WarmupBlock(),
            DepositStatement  => new DepositBlock(),
            RotationStatement => new RotationBlock(),
            RotateStatement   => new RotateBlock(),
            DelayStatement    => new DelayBlock(),
            WaitStatement     => new WaitBlock(),
            PressureStatement => new PressureBlock(),
            SwaitStatement    => new SwaitBlock(),
            CheckStatement    => new CheckBlock(),
            LoopContainer     => new LoopBlock(),
            IfElseContainer   => new IfBlock(),
            BeginEndContainer => new BeginEndBlock(),
            LabelStatement    => new LabelBlock(),
            GotoStatement     => new GotoBlock(),
            StateStatement    => new StateBlock(),
            FunctionContainer => new FunctionBlock(),
            CallStatement     => new CallBlock(),
            VarStatement      => new VarBlock(),
            // A content-less InvalidStatement is the parser's trailing-trivia carrier
            // (a dangling comment / #endregion kept for round-tripping) — not a real
            // error. Render it invisibly so it doesn't surface as a bogus "Invalid" block.
            InvalidStatement iv when string.IsNullOrWhiteSpace(iv.OriginalText) => new TriviaCarrierBlock(),
            _                 => new UnknownBlock(node.GetType().Name.Replace("Statement", "").Replace("Container", "")),
        };
        block.BindTo(node);
        block.ApplyContext(context);
        return block;
    }
}

/// <summary>Renders as a gray block with the keyword; ToAst returns the bound node unchanged.</summary>
internal sealed class UnknownBlock : BlockBase
{
    public UnknownBlock(string keyword) : base(keyword + " (todo)", BlockTheme.StructureCategory) { }

    public override void BindTo(AstNode node) => BoundNode = node;

    public override AstNode ToAst()
        => BoundNode ?? throw new InvalidOperationException("UnknownBlock.ToAst called before BindTo");
}

/// <summary>Invisible carrier for a content-less <see cref="InvalidStatement"/> — the
/// parser's placeholder for dangling trailing trivia (a comment or <c>#endregion</c>
/// that ends the block, e.g. just before <c>finish</c>). It stays in the stack so
/// <see cref="RegionModel"/> can read its leading trivia to close a trailing region and
/// <see cref="ToAst"/> reproduces the node for round-tripping, but it is never laid out
/// or painted (<see cref="BlockBase.SuppressInLayout"/>).</summary>
internal sealed class TriviaCarrierBlock : BlockBase
{
    public TriviaCarrierBlock() : base("", BlockTheme.StructureCategory) => Visible = false;

    internal override bool SuppressInLayout => true;

    public override void BindTo(AstNode node) => BoundNode = node;

    public override AstNode ToAst()
        => BoundNode ?? throw new InvalidOperationException("TriviaCarrierBlock.ToAst called before BindTo");
}
