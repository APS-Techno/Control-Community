using APS.SystemControl.Core.Classes;
using DriveSnapshot = APS.SystemControl.Core.Classes.HomingCheck.DriveSnapshot;

namespace CoreLogicTests;

/// <summary>
/// Two-snapshot homing guard. The recipe must refuse to
/// start whenever any drive is moving, unhomed, off-home, or drifting
/// between the two samples.
/// </summary>
public class HomingCheckTests
{
    private static DriveSnapshot Homed(string id = "VS", float pos = 0f) =>
        new(id, IsHomed: true, IsMoving: false, Position: pos);

    [Fact]
    public void Two_stable_at_zero_homed_drives_produce_no_offenders()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS"), Homed("HS")],
            second: [Homed("VS"), Homed("HS")]);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Unhomed_on_first_sample_reports_not_homed()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [new DriveSnapshot("VS", IsHomed: false, IsMoving: false, Position: 0)],
            second: [Homed("VS")]);
        Assert.Single(offenders);
        Assert.Equal("VS", offenders[0].Id);
        Assert.Contains("not homed", offenders[0].Reason);
    }

    [Fact]
    public void Moving_on_second_sample_reports_still_moving()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS")],
            second: [new DriveSnapshot("VS", IsHomed: true, IsMoving: true, Position: 0)]);
        Assert.Single(offenders);
        Assert.Contains("still moving", offenders[0].Reason);
    }

    [Fact]
    public void Off_home_position_reports_not_at_home_with_step_number()
    {
        // Both samples read 10 steps — drive is homed and stable but not at 0.
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS", 10f)],
            second: [Homed("VS", 10f)]);
        Assert.Single(offenders);
        Assert.Contains("not at home", offenders[0].Reason);
        Assert.Contains("10", offenders[0].Reason);
    }

    [Fact]
    public void Drifting_between_samples_reports_drift()
    {
        // Both within ±0.1 of 0 individually, but differ by 0.15 > tolerance
        // — the off-home check passes, so only the drift check fires.
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS", 0.05f)],
            second: [Homed("VS", -0.1f)],
            positionTolerance: 0.1f);
        Assert.Single(offenders);
        Assert.Contains("drifting", offenders[0].Reason);
    }

    [Fact]
    public void Within_default_tolerance_is_not_drift()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS", 0.1f)],
            second: [Homed("VS", 0.3f)]);
        // 0.2 apart, default tolerance 0.5 — should pass.
        Assert.Empty(offenders);
    }

    [Fact]
    public void Drive_missing_from_second_sample_is_reported()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS"), Homed("HS")],
            second: [Homed("VS")]);
        Assert.Contains(offenders, o => o.Id == "HS" && o.Reason.Contains("missing from second"));
    }

    [Fact]
    public void Drive_missing_from_first_sample_is_reported()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [Homed("VS")],
            second: [Homed("VS"), Homed("HS")]);
        Assert.Contains(offenders, o => o.Id == "HS" && o.Reason.Contains("missing from first"));
    }

    [Fact]
    public void Multiple_offenders_all_reported()
    {
        var offenders = HomingCheck.FindOffenders(
            first:  [
                new DriveSnapshot("VS", false, false, 0),
                new DriveSnapshot("HS", true,  true,  0),
            ],
            second: [
                new DriveSnapshot("VS", true,  false, 0),
                new DriveSnapshot("HS", true,  false, 0),
            ]);
        Assert.Equal(2, offenders.Count);
        Assert.Contains(offenders, o => o.Id == "VS" && o.Reason.Contains("not homed"));
        Assert.Contains(offenders, o => o.Id == "HS" && o.Reason.Contains("still moving"));
    }

    [Fact]
    public void Empty_inputs_produce_no_offenders()
    {
        Assert.Empty(HomingCheck.FindOffenders([], []));
    }

    [Fact]
    public void FormatError_builds_operator_message_with_all_ids()
    {
        var offenders = new[]
        {
            new HomingCheck.Offender("VS", "not homed"),
            new HomingCheck.Offender("HS", "still moving"),
        };
        string msg = HomingCheck.FormatError(offenders);
        Assert.Contains("VS", msg);
        Assert.Contains("HS", msg);
        Assert.Contains("not homed", msg);
        Assert.Contains("still moving", msg);
        Assert.Contains("Home all drives manually", msg);
    }

    [Fact]
    public void FormatError_empty_offenders_returns_empty_string()
    {
        Assert.Equal(string.Empty, HomingCheck.FormatError([]));
    }

    [Fact]
    public void Null_inputs_throw_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HomingCheck.FindOffenders(null!, []));
        Assert.Throws<ArgumentNullException>(() => HomingCheck.FindOffenders([], null!));
        Assert.Throws<ArgumentNullException>(() => HomingCheck.FormatError(null!));
    }
}
