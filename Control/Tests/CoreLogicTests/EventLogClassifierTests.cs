using APS.SystemControl.Core.Logging;

namespace CoreLogicTests;

public class EventLogClassifierTests
{
    [Theory]
    // Real operator-level events flowing through the bus.
    [InlineData("Core",      "System is ready",                       LogEventLevel.Operator)]
    [InlineData("Recipe",    "Recipe started: AlN.rcpx",              LogEventLevel.Operator)]
    [InlineData("Recipe",    "Recipe complete: AlN.rcpx (12m 34s)",   LogEventLevel.Operator)]
    [InlineData("Recipe",    "Recipe aborted: AlN.rcpx (3m 02s)",     LogEventLevel.Operator)]
    [InlineData("Recipe",    "Recipe failed: SYSTEM_AIR_RELEASE (4.7s) — interpreter returned false", LogEventLevel.Operator)]
    // Itemised teardown/protocol failure lines must stay Operator-visible: a
    // leading indent would downgrade them to Detail and strand them off the
    // floating events overlay.
    [InlineData("Recipe",    "INCOMPLETE step: power RF301 false — supply not responding", LogEventLevel.Operator)]
    [InlineData("Core",      "INCOMPLETE step: valve A1 true — Command valve not found",   LogEventLevel.Operator)]
    [InlineData("recipe",    "Base pressure: 0.05 mTorr",             LogEventLevel.Operator)]
    [InlineData("Execute",   "V1 open: OK",                LogEventLevel.Operator)]
    [InlineData("Isolate",   "System isolated",            LogEventLevel.Operator)]
    [InlineData("Operation", "Holder home: OK",            LogEventLevel.Operator)]
    [InlineData("Interlock", "TP fault",                   LogEventLevel.Operator)]
    [InlineData("pwait",     "pm reached target",          LogEventLevel.Operator)]
    [InlineData("check",     "pm = 0.05 < 0.1: PASS",      LogEventLevel.Operator)]
    [InlineData("rotation",  "Rotation ON, speed=10",      LogEventLevel.Operator)]
    [InlineData("message",   "Operator acknowledged",      LogEventLevel.Operator)]
    [InlineData("State",     "Idle -> Processing",         LogEventLevel.Operator)]
    [InlineData("Override",  "Override active",            LogEventLevel.Operator)]
    [InlineData("Drive",     "Holder offset set to 12.5",  LogEventLevel.Operator)]
    [InlineData("Alarm",     "Raised: vacuum_low",         LogEventLevel.Operator)]
    [InlineData("V1",        "open",                       LogEventLevel.Operator)]

    // Noisy sources stay at Detail (filtered out of the overlay).
    [InlineData("OpDelay",  "tick",                        LogEventLevel.Detail)]
    [InlineData("Watchdog", "heartbeat",                   LogEventLevel.Detail)]
    [InlineData("Poll",     "cycle 12345",                 LogEventLevel.Detail)]
    [InlineData("Sensor",   "raw=12345 mantissa=0.082",    LogEventLevel.Detail)]
    [InlineData("PLC",      "Read DB1 byte 4 = 0x01",      LogEventLevel.Detail)]
    [InlineData("MFC",      "raw setpoint=128",            LogEventLevel.Detail)]
    [InlineData("Menus",    "Loading menu entry",          LogEventLevel.Detail)]

    // Case-insensitive denylist.
    [InlineData("watchdog", "heartbeat",                   LogEventLevel.Detail)]
    [InlineData("WATCHDOG", "heartbeat",                   LogEventLevel.Detail)]
    public void Classify_ReturnsExpectedLevel(string source, string message, LogEventLevel expected)
    {
        Assert.Equal(expected, EventLogClassifier.Classify(source, message));
    }

    [Fact]
    public void Classify_NullSourceTreatedAsEmpty_DefaultsToOperator()
    {
        Assert.Equal(LogEventLevel.Operator, EventLogClassifier.Classify(null!, "hello"));
    }

    [Theory]
    [InlineData("Execute", "  exec: pump SP1 true")]
    [InlineData("Execute", "  target=Pump, argTypes=[IBaseComponent,String,Boolean]")]
    [InlineData("Execute", "  FindElement(Pump, SP1) => S7200Pump")]
    [InlineData("Execute", "  result: System.Boolean[]")]
    [InlineData("Execute", "\tindented with tab")]
    public void Classify_IndentedMessages_DowngradedToDetail(string source, string message)
    {
        Assert.Equal(LogEventLevel.Detail, EventLogClassifier.Classify(source, message));
    }
}
