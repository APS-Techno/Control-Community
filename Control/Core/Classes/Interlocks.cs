using APS.SystemControl.Core.HWClasses.Base;


namespace APS.SystemControl.Core.Classes;

public struct InterlockCond
{
    public IBaseComponent? Target { get; set; }
    public object? Value { get; set; }
    /// <summary>
    /// Numeric comparison code for Sensor conditions: -1 (less), 0 (equal), 1 (more).
    /// </summary>
    public int Condition { get; set; }
    /// <summary>
    /// String-valued operator for device-type conditions that cannot be expressed
    /// as a tri-state int (e.g. Robot state "is" / "notIn").
    /// </summary>
    public string? StringCondition { get; set; }

    /// <summary>
    /// Every device currently failing this interlock, comma-separated, set by
    /// <c>Core.CheckInterlocks</c> when it rejects an operation. Null on a
    /// condition that did not come from a failed check. Reporting the full set
    /// lets the operator clear all blockers in one pass instead of serially.
    /// </summary>
    public string? Blockers { get; set; }
}

public class Interlock
{
    public List<InterlockCond> Conditions { get; set; } = [];
}

public class Interlocks
{
    private readonly Dictionary<string, Interlock> interlockDictionary = [];

    public void Add(string key, Interlock interlock)
    {
        interlockDictionary[key] = interlock;
    }

    public Interlock? Get(string key)
    {
        if (interlockDictionary.TryGetValue(key, out Interlock interlock))
        {
            return interlock;
        }
        return null;
    }

    public IReadOnlyDictionary<string, Interlock> GetAllInterlocks()
    {
        return interlockDictionary;
    }
}
