namespace APS.SystemControl.Core.HWClasses.Base;

/// <summary>
/// Implemented by devices whose <c>bool[] Status</c> array is alarm-rule
/// addressable by name. Names appear in array order (index 0 first).
///
/// Adding a name to this list adds a new bit at the end; reordering the
/// list is a breaking change to the alarm config and requires updating
/// every <c>&lt;alarm device="..." status="..."&gt;</c> entry in
/// <c>Alarms.xml</c> that references the affected device.
/// </summary>
public interface IStatusBitNamed
{
    IReadOnlyList<string> StatusBitNames { get; }
}
