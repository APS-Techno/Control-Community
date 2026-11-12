using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

public class SparklineScaleTests
{
    [Theory]
    [InlineData(0.9,   1.0)]
    [InlineData(1.0,   1.0)]
    [InlineData(1.1,   2.0)]
    [InlineData(2.0,   2.0)]
    [InlineData(4.9,   5.0)]
    [InlineData(6.0,  10.0)]
    [InlineData(23.0, 50.0)]
    [InlineData(0.011, 0.02)]
    public void NiceStep_rounds_up_to_1_2_5_decade(double raw, double expected)
        => Assert.Equal(expected, SparklineScale.NiceStep(raw), 12);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NiceStep_falls_back_to_one_on_degenerate_input(double raw)
        => Assert.Equal(1.0, SparklineScale.NiceStep(raw));

    [Fact]
    public void Linear_axis_rounds_outward_to_whole_steps()
    {
        var axis = SparklineScale.Build(12f, 87f, ReadingScale.Linear);

        Assert.False(axis.IsLog);
        Assert.True(axis.Min <= 12, $"Min {axis.Min} must contain the data");
        Assert.True(axis.Max >= 87, $"Max {axis.Max} must contain the data");
        // 12..87 padded to a 20 %-of-plot margin is -13..112; non-negative data
        // pins the floor at 0 and 50-unit steps round the top out to 150.
        Assert.Equal(0, axis.Min, 6);
        Assert.Equal(150, axis.Max, 6);
        Assert.Equal(
            new double[] { 0, 50, 100, 150 },
            axis.Ticks.Select(t => Math.Round(t, 6)));
    }

    [Fact]
    public void Linear_axis_leaves_a_fifth_of_the_plot_free_above_and_below()
    {
        // 100..200: the swing must fill at most 60 % of the axis, so the axis
        // reaches at least 66.7 and 233.3 before rounding outward — the trace
        // sits between the 20 % and 80 % lines, never on the frame.
        var axis = SparklineScale.Build(100f, 200f, ReadingScale.Linear);

        Assert.True(axis.Min <= 100 - 100 / 3.0, $"Min {axis.Min} leaves no headroom below");
        Assert.True(axis.Max >= 200 + 100 / 3.0, $"Max {axis.Max} leaves no headroom above");
        Assert.InRange(axis.Fraction(100), 0.2f - Tol, 1.0f);
        Assert.InRange(axis.Fraction(200), 0.0f, 0.8f + Tol);
    }

    /// <summary>Tolerance on a fraction assertion: the axis is built in double
    /// and <see cref="SparkAxis.Fraction"/> returns float.</summary>
    private const float Tol = 1e-5f;

    [Theory]
    // A swing that is tiny beside its magnitude, where the nice-step rounding
    // adds almost nothing of its own (mass flow, PSU current, PSU voltage,
    // GAUGE-B chuck temperature).
    [InlineData(39.8f, 40.0f)]     // Ar2, sccm
    [InlineData(299f,  300f)]      // PSU-B I, mA
    [InlineData(524f,  528f)]      // PSU-B V, V
    [InlineData(36.2f, 36.4f)]     // GAUGE-B, °C
    public void Linear_headroom_holds_when_the_swing_is_tiny_beside_its_magnitude(
        float dataMin, float dataMax)
    {
        var axis = SparklineScale.Build(dataMin, dataMax, ReadingScale.Linear);

        // Free plot below the trace = Fraction(dataMin); free above =
        // 1 - Fraction(dataMax). Rounding outward only adds to both.
        Assert.InRange(axis.Fraction(dataMin), 0.2f - Tol, 1.0f);
        Assert.InRange(axis.Fraction(dataMax), 0.0f, 0.8f + Tol);
        // Distinguishable corner labels fall out of the wider span: an axis
        // whose ends both print "300" tells the operator nothing.
        Assert.NotEqual(SparklineScale.Format(axis.Min), SparklineScale.Format(axis.Max));
    }

    [Fact]
    public void Linear_headroom_never_pushes_a_non_negative_reading_below_zero()
    {
        // A flow idling at 0 with a 30 sccm step: headroom would put -6 on the
        // axis; the zero floor wins because the data itself never went negative.
        var axis = SparklineScale.Build(0f, 30f, ReadingScale.Linear);

        Assert.Equal(0, axis.Min, 6);
        Assert.True(axis.Max >= 36, $"Max {axis.Max} leaves no headroom above");
    }

    [Fact]
    public void Linear_axis_opens_a_band_around_a_flat_signal()
    {
        // A steady 25 °C must not collapse onto the frame — the trace should sit
        // inside the plot with room above and below.
        var axis = SparklineScale.Build(25f, 25f, ReadingScale.Linear);

        Assert.True(axis.Min < 25, $"Min {axis.Min} should sit below the value");
        Assert.True(axis.Max > 25, $"Max {axis.Max} should sit above the value");
        float f = axis.Fraction(25);
        Assert.InRange(f, 0.05f, 0.95f);
    }

    [Fact]
    public void Linear_axis_survives_an_all_zero_history()
    {
        var axis = SparklineScale.Build(0f, 0f, ReadingScale.Linear);

        Assert.True(axis.Max > axis.Min);
        Assert.InRange(axis.Fraction(0), 0.05f, 0.95f);
    }

    [Fact]
    public void Fraction_maps_bottom_to_zero_and_top_to_one()
    {
        var axis = SparklineScale.Build(0f, 100f, ReadingScale.Linear);

        Assert.Equal(0f, axis.Fraction(axis.Min), 4);
        Assert.Equal(1f, axis.Fraction(axis.Max), 4);
        Assert.Equal(0.5f, axis.Fraction((axis.Min + axis.Max) / 2), 4);
    }

    [Fact]
    public void Fraction_clamps_out_of_range_samples_into_the_plot()
    {
        var axis = SparklineScale.Build(0f, 100f, ReadingScale.Linear);

        Assert.Equal(0f, axis.Fraction(-500));
        Assert.Equal(1f, axis.Fraction(5000));
        Assert.Equal(0f, axis.Fraction(double.NaN));
    }

    [Fact]
    public void Log_axis_snaps_to_whole_decades()
    {
        // A pump-down slice: 3e-6 .. 4e-2 Torr.
        var axis = SparklineScale.Build(3e-6f, 4e-2f, ReadingScale.Log);

        Assert.True(axis.IsLog);
        Assert.Equal(1e-6, axis.Min, 12);
        Assert.Equal(1e-1, axis.Max, 12);
        Assert.All(axis.Ticks, t =>
            Assert.Equal(Math.Round(Math.Log10(t)), Math.Log10(t), 6));
    }

    [Fact]
    public void Log_axis_spreads_decades_evenly()
    {
        var axis = SparklineScale.Build(1e-6f, 1e-2f, ReadingScale.Log);

        // 4 decades: 1e-4 must land exactly half way up the plot.
        Assert.Equal(0.5f, axis.Fraction(1e-4), 4);
        Assert.Equal(0.25f, axis.Fraction(1e-5), 4);
    }

    [Fact]
    public void Log_axis_stays_positive_and_finite_if_a_zero_min_reaches_it()
    {
        // The renderer feeds MinMaxPositive so this shouldn't happen, but the
        // axis must not produce a non-finite bound if it ever does.
        var axis = SparklineScale.Build(0f, 1e-3f, ReadingScale.Log);

        Assert.True(axis.Min > 0, $"Min {axis.Min} must stay positive");
        Assert.True(double.IsFinite(axis.Min) && double.IsFinite(axis.Max));
        Assert.Equal(0f, axis.Fraction(0));
    }

    [Fact]
    public void Log_axis_caps_a_glitched_span_instead_of_flattening_the_window()
    {
        // One bogus 1e-30 sample must not open a 30-decade axis that presses
        // every real reading onto the top frame.
        var axis = SparklineScale.Build(1e-30f, 1e-3f, ReadingScale.Log);

        double decades = Math.Log10(axis.Max) - Math.Log10(axis.Min);
        Assert.True(decades <= 12.5, $"axis spans {decades:0.#} decades");
    }

    [Fact]
    public void Log_axis_fits_the_full_ADS_pressure_range()
    {
        // A pump-down window really can run atmosphere → high vacuum, so the
        // glitch cap must not clip the machine's own 1e-8 .. 750 Torr range.
        var axis = SparklineScale.Build(1e-8f, 750f, ReadingScale.Log);

        Assert.True(axis.Min <= 1e-8, $"Min {axis.Min} clipped the data");
        Assert.True(axis.Max >= 750, $"Max {axis.Max} clipped the data");
    }

    [Fact]
    public void Log_axis_thins_the_grid_on_a_wide_span()
    {
        // 1e-8 .. 1 Torr is 8 decades. One line per decade would fill a 100 px
        // plot with grid.
        var axis = SparklineScale.Build(1e-8f, 1f, ReadingScale.Log);

        Assert.True(axis.Min <= 1e-8, $"Min {axis.Min} must contain the data");
        Assert.True(axis.Max >= 1, $"Max {axis.Max} must contain the data");
        Assert.True(axis.Ticks.Length <= 8,
            $"expected a thinned decade grid, got {axis.Ticks.Length} lines");
        Assert.True(axis.Ticks.Length >= 2);
    }

    [Fact]
    public void Log_request_falls_back_to_linear_when_nothing_is_positive()
    {
        var axis = SparklineScale.Build(-5f, 0f, ReadingScale.Log);

        Assert.False(axis.IsLog);
        Assert.True(axis.Max > axis.Min);
    }

    [Theory]
    [InlineData(0.0,      "0")]
    [InlineData(25.0,     "25")]
    [InlineData(1.5,      "1.5")]
    [InlineData(0.126,    "0.13")]
    [InlineData(750.0,    "750")]
    [InlineData(1e-6,     "1e-6")]
    [InlineData(5e-5,     "5e-5")]
    [InlineData(2.5e-7,   "2.5e-7")]
    [InlineData(1e6,      "1e6")]
    public void Format_stays_short_enough_for_a_corner_label(double v, string expected)
        => Assert.Equal(expected, SparklineScale.Format(v));

    [Fact]
    public void Format_uses_invariant_culture()
    {
        // The operator stations run ru-RU; a decimal comma in machine data is a
        // long-standing trap in this codebase.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("ru-RU");
            Assert.Equal("1.5", SparklineScale.Format(1.5));
            Assert.Equal("2.5e-7", SparklineScale.Format(2.5e-7));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
