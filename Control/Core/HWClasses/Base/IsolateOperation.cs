namespace APS.SystemControl.BaseHWClasses;

/// <summary>
/// Pure async helper used by the embedded <c>isolate</c> recipe keyword
/// (<c>Core.OpIsolate</c>) and its unit tests. Closes every connected
/// valve in <paramref name="valves"/> in order, then zeroes every connected
/// MFC in <paramref name="mfcs"/> in order, with a 100 ms wait between
/// every step so the PLC has time to apply each bit. Mirrors Delphi
/// unit_PLC7200.pas:250-266.
/// </summary>
public static class IsolateOperation
{
    public static async Task RunAsync(
        IEnumerable<BaseValve> valves,
        IEnumerable<BaseMFC> mfcs,
        CancellationToken ct)
    {
        foreach (var v in valves)
        {
            if (!v.Connected) continue;
            v.Close();
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        foreach (var m in mfcs)
        {
            if (!m.Connected) continue;
            m.SetFlow(0f);
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }
}
