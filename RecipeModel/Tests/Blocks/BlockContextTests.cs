using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockContextTests
{
    private sealed class CountingBlock : BlockBase
    {
        public int ApplyCount;
        public IBlockContext? LastContext;
        public CountingBlock() : base("counting", BlockTheme.StructureCategory) { }
        public override void BindTo(AstNode node) => BoundNode = node;
        public override AstNode ToAst() => BoundNode!;
        public override void ApplyContext(IBlockContext? context)
        {
            ApplyCount++;
            LastContext = context;
        }
    }

    [StaFact]
    public void Setting_Context_Fans_Out_To_Existing_Blocks()
    {
        var stack = new BlockStackControl();
        var node = new ValveStatement("M1", ValveAction.Open) { Id = NodeId.FromPosition("v", "s", 0) };
        var b1 = new CountingBlock();  b1.BindTo(node);  stack.Insert(0, b1);
        var b2 = new CountingBlock();  b2.BindTo(node);  stack.Insert(1, b2);

        var ctx = new BlockContext { SourceNames = new[] { "Si" } };
        stack.Context = ctx;

        Assert.Equal(1, b1.ApplyCount);
        Assert.Same(ctx, b1.LastContext);
        Assert.Equal(1, b2.ApplyCount);
        Assert.Same(ctx, b2.LastContext);
    }

    [StaFact]
    public void Inserting_After_Context_Set_Applies_Context_Once()
    {
        var stack = new BlockStackControl();
        var ctx = new BlockContext { SourceNames = new[] { "BN" } };
        stack.Context = ctx;

        var b = new CountingBlock();
        b.BindTo(new ValveStatement("M1", ValveAction.Open) { Id = NodeId.FromPosition("v", "s", 0) });
        stack.Insert(0, b);

        Assert.Equal(1, b.ApplyCount);
        Assert.Same(ctx, b.LastContext);
    }

    [StaFact]
    public void Setting_Context_To_Null_Fans_Out_Null()
    {
        var stack = new BlockStackControl();
        var b = new CountingBlock();
        b.BindTo(new ValveStatement("M1", ValveAction.Open) { Id = NodeId.FromPosition("v", "s", 0) });
        stack.Insert(0, b);

        stack.Context = new BlockContext { SourceNames = new[] { "Si" } };
        stack.Context = null;

        Assert.Equal(2, b.ApplyCount);
        Assert.Null(b.LastContext);
    }

    [Fact]
    public void BlockFactory_Applies_Context_When_Provided()
    {
        var node = new ValveStatement("HV", ValveAction.Close) { Id = NodeId.FromPosition("v", "s", 0) };
        var ctx = new BlockContext { SourceNames = new[] { "Ion" } };
        var block = BlockFactory.CreateFor(node, ctx);
        Assert.NotNull(block);
        // The default ApplyContext is a no-op for ValveBlock; this test only
        // proves the factory accepts and forwards the context without throwing.
    }
}
