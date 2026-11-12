using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

/// <summary>Which step failures pause for the operator and which go straight to
/// FAILURE_PROTOCOL. Spec: docs/superpowers/specs/2026-09-04-fault-pause-design.md.</summary>
public class FaultClassifierTests
{
    [Fact]
    public void Continue_on_error_never_pauses()
        => Assert.Equal(FaultPolicy.Abort,
            FaultClassifier.Classify(new InvalidOperationException("x"), null, continueOnError: true));

    [Fact]
    public void Cancellation_never_pauses()
        => Assert.Equal(FaultPolicy.Abort,
            FaultClassifier.Classify(new OperationCanceledException(), null, false));

    [Fact]
    public void Plc_comm_failure_never_pauses()
        => Assert.Equal(FaultPolicy.Abort,
            FaultClassifier.Classify(new PlcCommException(0x01, "read"), null, false));

    [Fact]
    public void Interlock_block_never_pauses()
        => Assert.Equal(FaultPolicy.Abort,
            FaultClassifier.Classify(null, new InterlockCond(), false));

    [Fact]
    public void Supply_not_responding_pauses()
        => Assert.Equal(FaultPolicy.Pause,
            FaultClassifier.Classify(
                new InvalidOperationException("power PSU-A on: supply not responding"), null, false));

    [Fact]
    public void Timeout_pauses()
        => Assert.Equal(FaultPolicy.Pause,
            FaultClassifier.Classify(new TimeoutException("pwait timed out"), null, false));

    [Fact]
    public void Null_result_without_exception_pauses()
        => Assert.Equal(FaultPolicy.Pause, FaultClassifier.Classify(null, null, false));
}
