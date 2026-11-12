using APS.SystemControl.Core.Logging;

namespace CoreLogicTests;

public class EventLogBusTests
{
    public EventLogBusTests()
    {
        // The bus is a process-wide static (event + boot gate). Detach any
        // subscriber a prior test failed to clean up so this test's single
        // handler sees only its own publishes — deterministic given the
        // assembly runs non-parallel (see TestCollectionBehavior.cs).
        EventLogBus.ResetForTests();
        // Tests in this class publish Operator-level entries and expect them
        // to arrive at that level. Boot-gating would silently downgrade
        // them, so open the gate at the start of each test.
        EventLogBus.MarkBootComplete();
    }

    [Fact]
    public void Publish_DeliversEntryToSubscriber()
    {
        EventLogEntry? received = null;
        Action<EventLogEntry> handler = e => received = e;
        EventLogBus.Entry += handler;
        try
        {
            EventLogBus.Publish("Core", "hello", LogEventLevel.Operator);

            Assert.NotNull(received);
            Assert.Equal("Core", received!.Source);
            Assert.Equal("hello", received.Message);
            Assert.Equal(LogEventLevel.Operator, received.Level);
            Assert.True((DateTime.Now - received.Timestamp).TotalSeconds < 2);
        }
        finally
        {
            EventLogBus.Entry -= handler;
        }
    }

    [Fact]
    public void Publish_DeliversToAllSubscribersWithoutConsumption()
    {
        int countA = 0, countB = 0;
        Action<EventLogEntry> a = _ => countA++;
        Action<EventLogEntry> b = _ => countB++;
        EventLogBus.Entry += a;
        EventLogBus.Entry += b;
        try
        {
            EventLogBus.Publish("Core", "msg", LogEventLevel.Detail);

            Assert.Equal(1, countA);
            Assert.Equal(1, countB);
        }
        finally
        {
            EventLogBus.Entry -= a;
            EventLogBus.Entry -= b;
        }
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        int count = 0;
        Action<EventLogEntry> h = _ => count++;
        EventLogBus.Entry += h;
        EventLogBus.Publish("S", "m", LogEventLevel.Detail);
        Assert.Equal(1, count);

        EventLogBus.Entry -= h;
        EventLogBus.Publish("S", "m2", LogEventLevel.Detail);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_DuringBoot_DowngradesOperatorToDetail()
    {
        // Re-arm the boot gate (test isolation note: ctor already opened it,
        // but the field is process-global, so simulate the boot window).
        var gateField = typeof(EventLogBus).GetField("_isBooting",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        gateField.SetValue(null, 1);
        try
        {
            EventLogEntry? received = null;
            Action<EventLogEntry> h = e => received = e;
            EventLogBus.Entry += h;
            try
            {
                EventLogBus.Publish("Core", "Hardware loaded", LogEventLevel.Operator);
                Assert.NotNull(received);
                Assert.Equal(LogEventLevel.Detail, received!.Level);
                Assert.Equal("Hardware loaded", received.Message);

                received = null;
                EventLogBus.Publish("PLC", "raw read", LogEventLevel.Detail);
                Assert.NotNull(received);
                Assert.Equal(LogEventLevel.Detail, received!.Level);
            }
            finally { EventLogBus.Entry -= h; }
        }
        finally
        {
            EventLogBus.MarkBootComplete();
        }
    }

    [Fact]
    public void Publish_FromBackgroundThread_StillDelivers()
    {
        EventLogEntry? received = null;
        Action<EventLogEntry> h = e => received = e;
        EventLogBus.Entry += h;
        try
        {
            var t = new System.Threading.Thread(() =>
                EventLogBus.Publish("Bg", "from-bg", LogEventLevel.Detail));
            t.Start();
            t.Join();

            Assert.NotNull(received);
            Assert.Equal("from-bg", received!.Message);
        }
        finally
        {
            EventLogBus.Entry -= h;
        }
    }
}
