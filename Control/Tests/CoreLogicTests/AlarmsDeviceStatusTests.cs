using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class AlarmsDeviceStatusTests
{
    private static Alarms NewAlarms() => new();

    [Fact]
    public void CheckDeviceStatus_FiresWhenBitSet()
    {
        var alarms = NewAlarms();
        alarms.AddDeviceStatusAlarm("TP", statusBit: 3, SystemState.Processing,
            "Turbo pump hardware alarm!");

        bool fired = alarms.CheckDeviceStatus(SystemState.Processing, "TP",
            [false, false, false, true]);

        Assert.True(fired);
        Assert.Equal("Turbo pump hardware alarm!", alarms.AlarmMessage);
    }

    [Fact]
    public void CheckDeviceStatus_DoesNotFireWhenBitClear()
    {
        var alarms = NewAlarms();
        alarms.AddDeviceStatusAlarm("TP", 3, SystemState.Processing, "msg");

        bool fired = alarms.CheckDeviceStatus(SystemState.Processing, "TP",
            [true, true, true, false]);

        Assert.False(fired);
    }

    [Fact]
    public void CheckDeviceStatus_RespectsState()
    {
        var alarms = NewAlarms();
        alarms.AddDeviceStatusAlarm("TP", 3, SystemState.Processing, "msg");

        bool inIdle = alarms.CheckDeviceStatus(SystemState.Idle, "TP",
            [false, false, false, true]);
        bool inProc = alarms.CheckDeviceStatus(SystemState.Processing, "TP",
            [false, false, false, true]);

        Assert.False(inIdle);
        Assert.True(inProc);
    }

    [Fact]
    public void CheckDeviceStatus_IgnoresOutOfRangeStatusBit()
    {
        var alarms = NewAlarms();
        alarms.AddDeviceStatusAlarm("TP", 10, SystemState.Processing, "msg");

        bool fired = alarms.CheckDeviceStatus(SystemState.Processing, "TP",
            [false, false, false, true]);

        Assert.False(fired);
    }

    [Fact]
    public void CheckDeviceStatus_IgnoresUnrelatedDevice()
    {
        var alarms = NewAlarms();
        alarms.AddDeviceStatusAlarm("TP", 3, SystemState.Processing, "msg");

        bool fired = alarms.CheckDeviceStatus(SystemState.Processing, "DifferentPump",
            [false, false, false, true]);

        Assert.False(fired);
    }
}
