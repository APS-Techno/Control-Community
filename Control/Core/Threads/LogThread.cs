using System.IO;

namespace APS.SystemControl.Core;

class LogThread(ChannelPool pool) : BaseThread(pool)
{
    private StreamWriter? _logWriter;
    private string _currentLogDate = "";

    private void WriteMessageToLog(string msg)
    {
        try
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            if (_logWriter == null || _currentLogDate != today)
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();

                string logDir = APS.SystemControl.Core.CoreClass.AppPaths.LogsDir;
                Directory.CreateDirectory(logDir);
                string filePath = Path.Combine(logDir, $"events_{today}.log");
                _logWriter = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8)
                {
                    AutoFlush = true
                };
                _currentLogDate = today;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _logWriter.WriteLine($"[{timestamp}] {msg}");
        }
        catch (IOException)
        {
            // Silently ignore file write errors to avoid crashing the log thread
        }
    }

    protected override void RunThread()
    {
        if (pool.LOGReader == null)
            return;

        // Synchronous (NOT async void): an async-void method on a ThreadStart
        // resumes its continuations on thread-pool threads with no enclosing
        // handler, so an escaping exception would be unobserved and reach
        // AppDomain.UnhandledException — the pattern CoreThread.RunThread bans.
        // Block on the LOG channel on this dedicated thread and isolate faults.
        // (audit L-2)
        try
        {
            pool.LOGReader.StartListening(HandleThreadMessage).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"LogThread.RunThread terminated: {ex}");
        }
    }

    protected void HandleThreadMessage(ThreadMsg msg)
    {
        string sender = msg.Device ?? "Unknown";
        string message;

        if (msg is ValMsg<string> valMsg)
            message = $"[{sender}] {valMsg.Value}";
        else
            message = $"[{sender}] {msg.Type}";

        WriteMessageToLog(message);
    }
}
