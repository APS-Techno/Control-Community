using System.Windows.Forms;
using APS.Controls.General;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class FineSpinButtonsTests
{
    [Fact]
    public void StepUp_AddsStep()
    {
        var nud = new NumericTextBox { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 5.0m };
        var spin = new FineSpinButtons { Target = nud, Step = 0.1m };
        spin.StepUp();
        Assert.Equal(5.1m, nud.Value);
    }

    [Fact]
    public void StepDown_SubtractsStep()
    {
        var nud = new NumericTextBox { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 5.0m };
        var spin = new FineSpinButtons { Target = nud, Step = 0.1m };
        spin.StepDown();
        Assert.Equal(4.9m, nud.Value);
    }

    [Fact]
    public void StepUp_ClampsAtMaximum()
    {
        var nud = new NumericTextBox { Minimum = 0, Maximum = 5, DecimalPlaces = 1, Value = 4.95m };
        var spin = new FineSpinButtons { Target = nud, Step = 0.1m };
        spin.StepUp();
        Assert.Equal(5m, nud.Value);
    }

    [Fact]
    public void StepDown_ClampsAtMinimum()
    {
        var nud = new NumericTextBox { Minimum = 0, Maximum = 5, DecimalPlaces = 1, Value = 0.05m };
        var spin = new FineSpinButtons { Target = nud, Step = 0.1m };
        spin.StepDown();
        Assert.Equal(0m, nud.Value);
    }

    [Fact]
    public void StepUp_FiresValueChangedExactlyOnce()
    {
        var nud = new NumericTextBox { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 5m };
        var spin = new FineSpinButtons { Target = nud, Step = 0.1m };
        int count = 0;
        nud.ValueChanged += (_, _) => count++;
        spin.StepUp();
        Assert.Equal(1, count);
    }
}
