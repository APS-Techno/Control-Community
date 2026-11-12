namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Fans every log call to a fixed set of sinks. Used in production to
/// send string log lines to the GUI channel AND append the structured
/// event to the hash-chained audit file.
/// </summary>
internal sealed class CompositeOverrideLogWriter : IOverrideLogWriter
{
    private readonly IOverrideLogWriter[] _sinks;

    public CompositeOverrideLogWriter(params IOverrideLogWriter[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = sinks;
    }

    public void Write(string line)
    {
        foreach (var s in _sinks) s.Write(line);
    }

    public void WriteAudit(AuditEvent evt)
    {
        foreach (var s in _sinks) s.WriteAudit(evt);
    }
}
