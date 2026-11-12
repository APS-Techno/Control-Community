using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class DepositBlockTests
{
    private static DepositStatement Make(string pos, string time, string retract, bool timeIsParam = false)
        => new(
            NodeField.Literal(pos),
            timeIsParam ? NodeField.Param(time) : NodeField.Literal(time),
            NodeField.Literal(retract))
        { Id = NodeId.FromPosition("deposit", "script", 0) };

    [StaFact]
    public void BindTo_Then_ToAst_Roundtrips_Plain_Literals()
    {
        var src = Make("Ion", "CleaningTime", "1");
        var block = new DepositBlock();
        block.BindTo(src);
        var r = Assert.IsType<DepositStatement>(block.ToAst());
        Assert.Equal("Ion", r.Position.Value);
        Assert.Equal("CleaningTime", r.Time.Value);
        Assert.Equal("1", r.Retract.Value);
    }

    [StaFact]
    public void BindTo_Then_ToAst_Preserves_Param_Time()
    {
        var src = Make("BN", "timeBN", "Home", timeIsParam: true);
        var block = new DepositBlock();
        block.BindTo(src);
        var r = Assert.IsType<DepositStatement>(block.ToAst());
        Assert.True(r.Time.IsParamRef);
        Assert.Equal("timeBN", r.Time.Value);
        Assert.Equal("BN", r.Position.Value);
        Assert.Equal("Home", r.Retract.Value);
    }

    [StaFact]
    public void ApplyContext_Populates_Source_Dropdown_Items()
    {
        var block = new DepositBlock();
        block.BindTo(Make("Ion", "1", "Home"));
        block.ApplyContext(new BlockContext { SourceNames = new[] { "Si", "BN", "Ion" } });

        Assert.Equal(new[] { "Si", "BN", "Ion" }, block.SourceItemsForTest.ToArray());
    }

    [StaFact]
    public void ApplyContext_Retract_Items_Begin_With_Home_Then_Sources()
    {
        var block = new DepositBlock();
        block.BindTo(Make("Ion", "1", "Home"));
        block.ApplyContext(new BlockContext { SourceNames = new[] { "Si", "BN", "Ion" } });

        Assert.Equal(new[] { "Home", "Si", "BN", "Ion" }, block.RetractItemsForTest.ToArray());
    }

    [StaFact]
    public void ApplyContext_Null_Leaves_Block_Functional_As_Free_Text()
    {
        var block = new DepositBlock();
        block.BindTo(Make("CustomSrc", "5", "CustomDest"));
        block.ApplyContext(null);

        var r = Assert.IsType<DepositStatement>(block.ToAst());
        Assert.Equal("CustomSrc", r.Position.Value);
        Assert.Equal("5", r.Time.Value);
        Assert.Equal("CustomDest", r.Retract.Value);
    }

    [StaFact]
    public void ApplyContext_Then_Edit_Source_Returns_New_Value_From_ToAst()
    {
        var block = new DepositBlock();
        block.BindTo(Make("Ion", "1", "Home"));
        block.ApplyContext(new BlockContext { SourceNames = new[] { "Si", "BN", "Ion" } });

        block.SetSourceForTest("BN");

        var r = Assert.IsType<DepositStatement>(block.ToAst());
        Assert.Equal("BN", r.Position.Value);
    }

    [StaFact]
    public void Typing_Free_Text_In_Source_Raises_FieldChanged()
    {
        var block = new DepositBlock();
        block.BindTo(Make("Ion", "1", "Home"));
        // Force the BodyPanel handle creation so WalkAndHook runs.
        block.CreateControl();

        int fired = 0;
        block.FieldChanged += () => fired++;

        block.SetSourceForTest("CustomMaterialName");

        Assert.True(fired > 0, "FieldChanged should fire when free text is typed into the Source combo.");
    }
}
