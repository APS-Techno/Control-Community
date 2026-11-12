using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public static class AstPatcher
{
    /// <summary>Preserves existing block UserControls where kind matches at the same index.</summary>
    public static void Patch(
        BlockStackControl stack,
        IReadOnlyList<AstNode> oldBody,
        IReadOnlyList<AstNode> newBody)
    {
        // One StackChanged for every mutation below, including the recursion into child mouths.
        using var _ = stack.BeginBatch();

        var ops = AstDiffer.DiffBody(oldBody, newBody);

        // Container blocks use BindHeaderTo so their mouth stacks are left intact for recursion.
        foreach (var op in ops.OfType<BindInPlaceOp>())
        {
            var block = stack.Blocks[op.Index];
            if (block is IContainerBlock cb)
                cb.BindHeaderTo(op.NewNode);
            else
                block.BindTo(op.NewNode);
        }

        // Removes and replacements: descending index so earlier indices stay valid.
        foreach (var op in ops.Where(o => o is ReplaceOp or RemoveOp).OrderByDescending(o => o.Index))
        {
            switch (op)
            {
                case ReplaceOp r:
                    var oldR = stack.Blocks[r.Index];
                    stack.Remove(oldR);
                    oldR.Dispose();
                    stack.Insert(r.Index, BlockFactory.CreateFor(r.NewNode));
                    break;
                case RemoveOp rm:
                    var oldRm = stack.Blocks[rm.Index];
                    stack.Remove(oldRm);
                    oldRm.Dispose();
                    break;
            }
        }

        // Inserts: ascending index so each insertion lands at the correct position.
        foreach (var op in ops.OfType<InsertOp>().OrderBy(o => o.Index))
            stack.Insert(op.Index, BlockFactory.CreateFor(op.NewNode));

        // Recurse into container children that were preserved (BindInPlace).
        for (int i = 0; i < stack.Blocks.Count && i < newBody.Count; i++)
        {
            if (stack.Blocks[i] is IContainerBlock container)
            {
                var mouths = container.Mouths;
                // For an existing container, diff old AST vs new AST.
                // For a freshly-INSERTED container, BlockFactory.CreateFor already called
                // BindTo which populated the mouths from newBody — treat oldBodies as newBodies
                // so recursion computes a no-op diff instead of double-inserting.
                var oldBodies = i < oldBody.Count && oldBody[i].GetType() == newBody[i].GetType()
                    ? container.ExtractBodies(oldBody[i])
                    : container.ExtractBodies(newBody[i]);
                var newBodies = container.ExtractBodies(newBody[i]);

                for (int m = 0; m < mouths.Count; m++)
                {
                    var oldChildren = m < oldBodies.Count ? oldBodies[m] : ImmutableArray<AstNode>.Empty;
                    var newChildren = m < newBodies.Count ? newBodies[m] : ImmutableArray<AstNode>.Empty;
                    if (oldChildren.IsDefault) oldChildren = ImmutableArray<AstNode>.Empty;
                    if (newChildren.IsDefault) newChildren = ImmutableArray<AstNode>.Empty;
                    Patch(mouths[m], oldChildren, newChildren);
                }
            }
        }
    }
}
