using APS.Wire;

public class BackoffPolicyTests
{
    [Fact]
    public void Doubles_per_failure_and_caps()
    {
        var b = new BackoffPolicy(1000, 30000);
        Assert.Equal(1000, b.CurrentDelayMs);
        Assert.False(b.IsFailing);
        b.OnFailure(); Assert.Equal(2000, b.CurrentDelayMs);
        b.OnFailure(); b.OnFailure(); Assert.Equal(8000, b.CurrentDelayMs);
        for (int i = 0; i < 10; i++) b.OnFailure();
        Assert.Equal(30000, b.CurrentDelayMs);
        Assert.True(b.IsFailing);
    }

    [Fact]
    public void Success_resets()
    {
        var b = new BackoffPolicy(10, 50);
        b.OnFailure(); b.OnFailure();
        b.OnSuccess();
        Assert.Equal(10, b.CurrentDelayMs);
        Assert.False(b.IsFailing);
    }
}
