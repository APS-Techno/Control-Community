using System.IO.Ports;
using System.Reflection;
using System.Runtime.InteropServices;

namespace APS.SystemControl.Core.HWClasses.Base;

/// <summary>
/// Closes a <see cref="SerialPort"/> without the possibility of hanging forever.
///
/// <para>.NET's <see cref="SerialPort"/> always runs an internal event-loop thread
/// (<c>SerialStream._eventRunner</c>) that sits in the native overlapped
/// <c>WaitCommEvent</c>. <see cref="SerialPort.Close"/> / <see cref="SerialPort.Dispose"/>
/// sets <c>endEventLoop</c>, calls <c>SetCommMask(0)</c> to unblock that wait, and then
/// JOINS the event-loop thread. When the port's peer is gone (emulator off, USB adapter
/// pulled, or a virtual COM pair left half-open by a prior unclean close), the
/// <c>WaitCommEvent</c> never returns even after <c>SetCommMask(0)</c>, so the join —
/// and thus <c>Close()</c> — wedges forever. That wedge froze application exit and, worse,
/// left the virtual COM pair claimed so the next launch could not reopen it (reboot-only
/// recovery).</para>
///
/// <para>This helper bounds the close and, if it wedges, forcibly breaks the wait:
/// <list type="number">
///   <item>Run <c>Close()</c> on a worker task (it sets <c>endEventLoop</c> first, then joins).</item>
///   <item>If it completes within the budget — clean close, done. This is the path a
///         healthy port always takes; behaviour is unchanged for working hardware.</item>
///   <item>Otherwise cancel the pending overlapped I/O with <c>CancelIoEx</c>. The wait
///         returns, the loop sees <c>endEventLoop</c>, the thread exits, and the join
///         inside <c>Close()</c> completes. <c>CancelIoEx</c> is issued AFTER <c>Close()</c>
///         has begun on purpose — issuing it before would just let the loop re-arm the wait.</item>
///   <item>If it is still wedged, dispose the underlying <c>SafeFileHandle</c> directly so
///         the OS/driver reclaims the COM port. <c>SerialStream.Dispose</c> then sees the
///         handle closed and skips its own join, so no re-wedge occurs.</item>
/// </list></para>
///
/// <para>Locating the internal handle uses reflection (private field
/// <c>SerialPort._internalSerialStream</c> → <c>SerialStream._handle</c>), with type-based
/// fallbacks so a future <c>System.IO.Ports</c> field rename degrades gracefully to the
/// old abandon-behaviour rather than throwing. This helper can therefore never make a
/// close WORSE than a plain bounded <c>Close()</c>.</para>
/// </summary>
public static class SerialWedgeBreaker
{
    /// <summary>Default per-close budget (ms) allowed before the wedge-break kicks in.</summary>
    public const int DefaultBudgetMs = 1000;

    // Resolved once. FieldInfo lookups are cheap but the miss-path logging is not, and
    // these never change within a process, so cache them lazily on first use.
    private static FieldInfo? _streamField;
    private static bool _streamFieldResolved;

    /// <summary>
    /// Closes <paramref name="port"/>, breaking a wedged native <c>WaitCommEvent</c> if the
    /// managed <see cref="SerialPort.Close"/> does not complete within <paramref name="budgetMs"/>.
    /// </summary>
    /// <param name="port">The port to close. <c>null</c> or an already-closed port is a no-op.</param>
    /// <param name="budgetMs">Milliseconds to wait for a clean close before, and again after,
    /// the wedge-break. Defaults to <see cref="DefaultBudgetMs"/>.</param>
    /// <returns><c>true</c> if the managed close completed cleanly (with or without a break);
    /// <c>false</c> if the close had to be abandoned or the handle force-disposed.</returns>
    public static bool CloseSafely(SerialPort? port, string id, int budgetMs = DefaultBudgetMs)
    {
        if (port is null)
            return true;

        bool isOpen;
        try { isOpen = port.IsOpen; }
        catch { isOpen = false; }
        if (!isOpen)
            return true;

        // Close() sets endEventLoop=true and then joins the event-loop thread. Run it off the
        // caller's thread so we can bound the join and intervene if it wedges.
        var close = Task.Run(() =>
        {
            try { port.Close(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"SerialWedgeBreaker[{id}]: Close() threw: {ex.Message}");
            }
        });

        if (close.Wait(budgetMs))
            return true;

        if (!TryGetPortHandle(port, out var handle) || handle is null)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"SerialWedgeBreaker[{id}]: Close() wedged after {budgetMs} ms and the internal port "
              + "handle could not be located (System.IO.Ports internals changed?) — abandoning the "
              + "close; the OS reclaims the port on process exit.");
            return false;
        }

        // Close() has already signalled endEventLoop; cancelling the pending overlapped
        // WaitCommEvent lets the event-loop thread return, observe the flag, and exit — which
        // releases the join and completes Close().
        System.Diagnostics.Trace.TraceWarning(
            $"SerialWedgeBreaker[{id}]: Close() wedged after {budgetMs} ms — cancelling pending I/O "
          + "to break WaitCommEvent (peer likely gone).");
        try { CancelIoEx(handle, IntPtr.Zero); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"SerialWedgeBreaker[{id}]: CancelIoEx failed: {ex.Message}");
        }

        if (close.Wait(budgetMs))
            return true;

        // Last resort: release the OS handle ourselves so the COM port is reclaimed even
        // though the managed join stays abandoned. SerialStream.Dispose then no-ops its join
        // because the handle is already closed, so the following SerialPort.Dispose is safe.
        System.Diagnostics.Trace.TraceWarning(
            $"SerialWedgeBreaker[{id}]: still wedged after CancelIoEx — disposing the port handle "
          + "directly to force the OS to reclaim the port.");
        try { handle.Dispose(); } catch { }
        return false;
    }

    /// <summary>
    /// Reflects out the native <see cref="SafeHandle"/> backing <paramref name="port"/>'s open
    /// stream. Returns <c>false</c> (with <paramref name="handle"/> = <c>null</c>) if the port is
    /// not open, the stream/handle field can't be found, or the handle is invalid/closed.
    /// </summary>
    internal static bool TryGetPortHandle(SerialPort port, out SafeHandle? handle)
    {
        handle = null;
        if (port is null)
            return false;

        object? stream = GetStreamField()?.GetValue(port)
                      ?? FindFieldValue(port, typeof(SerialPort), f => f.FieldType.Name == "SerialStream");
        if (stream is null)
            return false;

        object? h = stream.GetType()
                          .GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)
                          ?.GetValue(stream)
                 ?? FindFieldValue(stream, stream.GetType(), f => typeof(SafeHandle).IsAssignableFrom(f.FieldType));

        handle = h as SafeHandle;
        return handle is { IsInvalid: false, IsClosed: false };
    }

    private static FieldInfo? GetStreamField()
    {
        if (!_streamFieldResolved)
        {
            _streamField = typeof(SerialPort).GetField(
                "_internalSerialStream", BindingFlags.NonPublic | BindingFlags.Instance);
            _streamFieldResolved = true;
        }
        return _streamField;
    }

    private static object? FindFieldValue(object target, Type type, Func<FieldInfo, bool> match)
    {
        foreach (var f in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (match(f))
                return f.GetValue(target);
        }
        return null;
    }

    // Cancels all outstanding I/O for the handle across every thread (lpOverlapped = NULL),
    // which unblocks the event-loop thread's overlapped WaitCommEvent. Passing the SafeHandle
    // keeps it ref-counted alive for the duration of the call.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeHandle hFile, IntPtr lpOverlapped);
}
