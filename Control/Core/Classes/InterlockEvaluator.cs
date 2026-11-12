using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure evaluators for the interlock condition kinds. Each <c>*Fails</c>
/// method returns true when the condition is VIOLATED (the interlock should
/// block the operation).
/// </summary>
public static class InterlockEvaluator
{
    public static bool ValveFails(bool actualIsOpen, bool requiredIsOpen)
        => actualIsOpen != requiredIsOpen;

    /// <summary>
    /// Valve interlock from a tri-state <see cref="ValveState"/>. When the state
    /// is <see cref="ValveState.Undefined"/> (a disconnected/unresponsive PLC),
    /// the actual open/closed state is unknown, so we FAIL SAFE (block the op)
    /// rather than assume closed — assuming closed lets a "require valve closed"
    /// guard pass without confirming it. Known states evaluate exactly as the
    /// bool overload.
    /// </summary>
    public static bool ValveFails(ValveState actualState, bool requiredIsOpen)
        => actualState == ValveState.Undefined
           || ValveFails(actualState == ValveState.Open, requiredIsOpen);

    /// <summary>
    /// The live Status[] array carries a single boolean in element 0 — the
    /// dispatcher passes that element here.
    /// </summary>
    public static bool PumpFails(bool actualOn, bool requiredOn)
        => actualOn != requiredOn;

    /// <summary>Named pump status bit must be true; fails closed when disconnected or the bit is absent.</summary>
    public static bool PumpStatusBitFails(bool connected, bool[] status, int bitIndex)
        => !connected || bitIndex < 0 || bitIndex >= status.Length || !status[bitIndex];

    /// <summary>
    /// Sensor interlock. <paramref name="condition"/> is -1 for "less",
    /// 0 for "equal", 1 for "more" — matching <see cref="ConditionParser.ParseCondition"/>.
    /// <list type="bullet">
    ///   <item><c>-1</c> (less): fails when <paramref name="actual"/> &gt; <paramref name="expected"/>.</item>
    ///   <item><c> 0</c> (equal): fails when <paramref name="actual"/> ≠ <paramref name="expected"/>.</item>
    ///   <item><c> 1</c> (more): fails when <paramref name="actual"/> &lt; <paramref name="expected"/>.</item>
    /// </list>
    /// The 0 branch is exact-equal, unlike the relative tolerance used by the
    /// Alarms / Interpreter equality branches.
    /// A NaN or infinite reading (invalid, over-range or under-range gauge) on
    /// either side always fails: no limit can be shown to hold.
    /// </summary>
    public static bool SensorFails(float actual, int condition, float expected)
    {
        if (!float.IsFinite(actual) || !float.IsFinite(expected))
            return true;
        return condition switch
        {
            -1 => actual > expected,
             0 => actual != expected,
             1 => actual < expected,
             _ => false,
        };
    }

    /// <summary>
    /// Measured-power interlock. Fails closed unless the supply is connected and
    /// answering and the reading is finite. "more" additionally requires the output
    /// to be enabled and the measured power strictly above the threshold; "less" and
    /// "equal" compare like <see cref="SensorFails"/>.
    /// </summary>
    public static bool PowerFails(bool connected, bool responding, bool outputOn,
        float measuredWatts, int condition, float thresholdWatts)
    {
        if (!connected || !responding) return true;
        if (!float.IsFinite(measuredWatts) || !float.IsFinite(thresholdWatts)) return true;
        return condition == 1
            ? !outputOn || !(measuredWatts > thresholdWatts)
            : SensorFails(measuredWatts, condition, thresholdWatts);
    }

    /// <summary>Numeric MFC flow interlock; fails closed when disconnected, the register read was invalid, or non-finite.</summary>
    public static bool MfcFlowFails(bool connected, bool flowValid, float actualFlow, int condition, float expected)
        => !connected || !flowValid || SensorFails(actualFlow, condition, expected);

    /// <summary>
    /// "MFC id stable pct": the setpoint must be positive, the flow non-zero and
    /// within <paramref name="tolerancePercent"/> of the setpoint. Fails closed on
    /// disconnect, non-finite values, a non-positive setpoint or zero flow.
    /// </summary>
    public static bool MfcStableFails(bool connected, bool flowValid, float actualFlow, float setpoint, float tolerancePercent)
    {
        if (!connected || !flowValid) return true;
        if (!float.IsFinite(actualFlow) || !float.IsFinite(setpoint) || !float.IsFinite(tolerancePercent))
            return true;
        if (setpoint <= 0f || actualFlow <= 0f || tolerancePercent <= 0f) return true;
        return MathF.Abs(actualFlow - setpoint) > setpoint * tolerancePercent / 100f;
    }

    /// <summary>
    /// Stage position interlock: fails when the stage is farther than
    /// <paramref name="tolerance"/> from <paramref name="expectedPosition"/>.
    /// Used to keep moving stages away from collision-prone valves (e.g.
    /// GVL must not close unless the HS arm is retracted to home).
    /// </summary>
    public static bool StageFails(float actualPosition, float expectedPosition, float tolerance)
        => Math.Abs(actualPosition - expectedPosition) > tolerance;

    /// <summary>
    /// Robot state interlock. Returns <c>true</c> when the condition is VIOLATED
    /// (the interlock should block the operation). Fails closed on any invalid
    /// input — interlocks are safety-critical.
    /// </summary>
    /// <param name="expectedStateName">
    /// Name of the expected state (case-insensitive). Parsed via
    /// <see cref="Enum.TryParse{RobotState}"/>; if unparseable, returns <c>true</c>
    /// (fail closed).
    /// </param>
    /// <param name="op">
    /// Comparison operator:
    /// <list type="bullet">
    ///   <item><c>"is"</c>    — fails when <paramref name="actual"/> ≠ expected.</item>
    ///   <item><c>"notIn"</c> — fails when <paramref name="actual"/> = expected.</item>
    /// </list>
    /// Unknown operators fail closed.
    /// </param>
    public static bool RobotFails(RobotState actual, string expectedStateName, string op)
    {
        if (!Enum.TryParse<RobotState>(expectedStateName, ignoreCase: true, out var expected))
            return true;

        bool match = actual == expected;
        return op.ToLowerInvariant() switch
        {
            "is"    => !match,
            "notin" =>  match,
            _       =>  true,
        };
    }
}
