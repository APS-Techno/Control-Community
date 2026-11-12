using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

/// <summary>
/// Re-applying a <see cref="BlockContext"/> calls <see cref="ParamOrLiteralEditor.SetAvailableParams"/>
/// on every bound block. That must never blank a bound <c>$param</c>, or editing any
/// field rewrites <c>deposit Cr $depTime Home</c> as <c>deposit Cr $ Home</c>.
/// </summary>
public class ParamOrLiteralEditorTests
{
    [StaFact]
    public void SetAvailableParams_Preserves_Selected_Param()
    {
        var ed = new ParamOrLiteralEditor();
        ed.Value = NodeField.Param("depTime");

        ed.SetAvailableParams(["thickness", "depTime", "power"]);

        Assert.True(ed.Value.IsParamRef);
        Assert.Equal("depTime", ed.Value.Value);
    }

    [StaFact]
    public void SetAvailableParams_Keeps_Param_Missing_From_New_List()
    {
        var ed = new ParamOrLiteralEditor();
        ed.Value = NodeField.Param("depTime");

        ed.SetAvailableParams(["thickness"]);

        Assert.Equal("depTime", ed.Value.Value);
    }

    [StaFact]
    public void SetAvailableParams_Does_Not_Raise_ValueChanged()
    {
        var ed = new ParamOrLiteralEditor();
        ed.Value = NodeField.Param("depTime");
        int raised = 0;
        ed.ValueChanged += () => raised++;

        ed.SetAvailableParams(["a", "depTime", "b"]);

        Assert.Equal(0, raised);
    }

    [StaFact]
    public void Deposit_Block_Keeps_Param_Time_After_Context_Applied()
    {
        var block = new DepositBlock();
        block.BindTo(new DepositStatement(NodeField.Literal("Cr"), NodeField.Param("depTime"), NodeField.Literal("Home"))
            { Id = NodeId.FromPosition("deposit", "script", 0) });

        block.ApplyContext(new BlockContext
        {
            SourceNames = ["Cr", "Ti"],
            ParameterNames = ["thickness", "depTime"],
            DevicesByCategory = new Dictionary<string, IReadOnlyList<string>>(),
        });

        var r = Assert.IsType<DepositStatement>(block.ToAst());
        Assert.True(r.Time.IsParamRef);
        Assert.Equal("depTime", r.Time.Value);
    }
}
