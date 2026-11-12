using APS.RecipeModel.Controls.ScriptEditor;

namespace APS.RecipeModel.Tests.Controls;

public class LineNumberGutterWidthTests
{
    [Fact]
    public void DigitCount_Zero_IsOne()
    {
        Assert.Equal(1, LineNumberGutter.DigitCount(0));
    }

    [Fact]
    public void DigitCount_One_IsOne()
    {
        Assert.Equal(1, LineNumberGutter.DigitCount(1));
    }

    [Fact]
    public void DigitCount_Nine_IsOne()
    {
        Assert.Equal(1, LineNumberGutter.DigitCount(9));
    }

    [Fact]
    public void DigitCount_Ten_IsTwo()
    {
        Assert.Equal(2, LineNumberGutter.DigitCount(10));
    }

    [Fact]
    public void DigitCount_NinetyNine_IsTwo()
    {
        Assert.Equal(2, LineNumberGutter.DigitCount(99));
    }

    [Fact]
    public void DigitCount_Hundred_IsThree()
    {
        Assert.Equal(3, LineNumberGutter.DigitCount(100));
    }

    [Fact]
    public void DigitCount_Thousand_IsFour()
    {
        Assert.Equal(4, LineNumberGutter.DigitCount(1000));
    }

    [Fact]
    public void DigitCount_ManyLines_IsCorrect()
    {
        Assert.Equal(5, LineNumberGutter.DigitCount(12345));
    }
}
