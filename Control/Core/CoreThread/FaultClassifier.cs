using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Core;

internal enum FaultPolicy { Pause, Abort }

/// <summary>
/// Decides whether a failed step pauses for the operator or aborts into
/// FAILURE_PROTOCOL. By error TYPE, never by recipe: a lost PLC link or an
/// interlock means the machine's state can no longer be trusted, so the full
/// teardown is right; a supply that did not answer, a pressure wait that ran
/// over, a line that could not be resolved are things an operator can fix in
/// place. Spec: docs/superpowers/specs/2026-09-04-fault-pause-design.md.
/// </summary>
internal static class FaultClassifier
{
    /// <param name="ex">The exception the step raised, or null when it merely reported failure.</param>
    /// <param name="result">The step's returned value when it did not throw (an
    /// <see cref="InterlockCond"/> marks an interlock block).</param>
    /// <param name="continueOnError">True inside FAILURE_PROTOCOL / FAILURE_HOLD / a
    /// tolerant embedded recipe — those never pause.</param>
    internal static FaultPolicy Classify(Exception? ex, object? result, bool continueOnError)
    {
        if (continueOnError) return FaultPolicy.Abort;
        if (ex is OperationCanceledException) return FaultPolicy.Abort;
        if (ex is PlcCommException) return FaultPolicy.Abort;
        if (result is InterlockCond) return FaultPolicy.Abort;
        return FaultPolicy.Pause;
    }
}
