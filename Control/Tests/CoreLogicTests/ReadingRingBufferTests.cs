using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

public class ReadingRingBufferTests
{
    private static ReadingRingBuffer Filled(params float[] values)
    {
        var buf = new ReadingRingBuffer(Math.Max(2, values.Length));
        foreach (float v in values) buf.Add(v);
        return buf;
    }

    [Fact]
    public void MinMaxPositive_ignores_zero_and_negative_samples()
    {
        // A pressure gauge parks at exactly 0 while it is off; those samples
        // must not become the minimum of a log axis.
        var buf = Filled(0f, 1e-5f, 0f, 3e-3f, -1f);

        var (min, max) = buf.MinMaxPositive();

        Assert.Equal(1e-5f, min, 9);
        Assert.Equal(3e-3f, max, 9);
    }

    [Fact]
    public void MinMaxPositive_returns_zeroes_when_nothing_is_positive()
    {
        var (min, max) = Filled(0f, 0f, -2f).MinMaxPositive();

        Assert.Equal(0f, min);
        Assert.Equal(0f, max);
    }

    [Fact]
    public void MinMaxPositive_handles_a_single_positive_sample()
    {
        var (min, max) = Filled(0f, 5f, 0f).MinMaxPositive();

        Assert.Equal(5f, min);
        Assert.Equal(5f, max);
    }

    [Fact]
    public void MinMaxPositive_reads_across_the_wrap_point()
    {
        var buf = new ReadingRingBuffer(3);
        buf.Add(100f);           // evicted
        buf.Add(4f);
        buf.Add(0f);
        buf.Add(9f);             // wraps

        var (min, max) = buf.MinMaxPositive();

        Assert.Equal(4f, min);
        Assert.Equal(9f, max);
    }

    [Fact]
    public void MinMaxPositive_is_empty_on_an_empty_buffer()
    {
        var (min, max) = new ReadingRingBuffer(4).MinMaxPositive();

        Assert.Equal(0f, min);
        Assert.Equal(0f, max);
    }
}
