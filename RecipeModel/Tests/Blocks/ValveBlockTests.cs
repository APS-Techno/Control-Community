using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class ValveBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_Round_Trips_Device_And_Action()
    {
        var original = new ValveStatement("HV", ValveAction.Close)
        {
            Id = NodeId.FromPosition("valve", "script", 0),
        };
        var block = new ValveBlock();
        block.BindTo(original);
        var result = Assert.IsType<ValveStatement>(block.ToAst());

        Assert.Equal("HV", result.Device);
        Assert.Equal(ValveAction.Close, result.Action);
        Assert.Equal(original.Id, result.Id);
    }

    [StaFact]
    public void Editing_DeviceName_Reflects_In_ToAst()
    {
        var original = new ValveStatement("HV", ValveAction.Open)
        {
            Id = NodeId.FromPosition("valve", "script", 0),
        };
        var block = new ValveBlock();
        block.BindTo(original);
        block.SetDeviceForTesting("M1");

        var result = Assert.IsType<ValveStatement>(block.ToAst());
        Assert.Equal("M1", result.Device);
    }

    [StaFact]
    public void ToAst_Before_BindTo_Emits_Empty_Device_With_Fresh_Id()
    {
        var block = new ValveBlock();
        var result = Assert.IsType<ValveStatement>(block.ToAst());
        Assert.Equal("", result.Device);
    }
}
