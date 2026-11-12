using System.Threading.Channels;

namespace APS.SystemControl.Core
{
    public static class ChannelReaderExtensions
    {
        public static async Task StartListening(this ChannelReader<ThreadMsg> channelReader, Action<ThreadMsg> callback)
        {
            if (channelReader != null)
            {
                await foreach (ThreadMsg msg in channelReader.ReadAllAsync())
                {
                    // Isolate each message: a single throwing callback (e.g. an
                    // InvalidCastException from a mismatched payload in the GUI
                    // dispatcher) must NOT terminate the enumeration — that would
                    // permanently stop this consumer draining its channel while
                    // producers keep succeeding via TryWrite on the unbounded
                    // channel, silently dropping alarm/interlock/log delivery.
                    try
                    {
                        callback(msg);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError(
                            $"ChannelReader.StartListening: callback threw for " +
                            $"{msg.Type}/{msg.Device} — message dropped, consumer continues: {ex}");
                    }
                }
            }
        }
    }

    public class SystemChannel
    {
        private readonly Channel<ThreadMsg> _channel;
        private readonly string _name;

        public SystemChannel(string name = "unnamed")
        {
            _name = name;
            _channel = Channel.CreateUnbounded<ThreadMsg>();
        }

        public ChannelWriter<ThreadMsg> Writer => _channel.Writer;
        public ChannelReader<ThreadMsg> Reader => _channel.Reader;

        /// <summary>
        /// Completes the channel so a consumer blocked in WaitToReadAsync /
        /// ReadAllAsync observes the end of the stream and exits its loop.
        /// Used on shutdown to release the dedicated CoreThread. Idempotent.
        /// </summary>
        public void Complete() => Writer.TryComplete();

        public void Write(ThreadMsg msg)
        {
            if (Writer.TryWrite(msg)) return;

            // Unbounded channels only refuse TryWrite when the channel is
            // completed, which shouldn't happen during normal operation.
            // Log once per occurrence so a stuck channel shows up in diagnostics
            // instead of silently blocking the caller on GetAwaiter().GetResult().
            System.Diagnostics.Trace.TraceWarning(
                $"SystemChannel[{_name}]: TryWrite failed (channel full/completed?); falling back to blocking WriteAsync.");
            Writer.WriteAsync(msg).AsTask().GetAwaiter().GetResult();
        }

        public async Task StartListening(Action<ThreadMsg> callback)
        {
            await Reader.StartListening(callback);
        }
    }

    public class ChannelPool
    {
        private readonly SystemChannel? chGUI;
        private readonly SystemChannel? chHW;
        private readonly SystemChannel? chLOG;

        public ChannelPool()
        {
            chGUI = new SystemChannel("GUI");
            chHW = new SystemChannel("HW");
            chLOG = new SystemChannel("LOG");
        }

        public ChannelReader<ThreadMsg>? GUIReader => chGUI?.Reader;
        public ChannelWriter<ThreadMsg>? GUIWriter => chGUI?.Writer;
        public ChannelReader<ThreadMsg>? HWReader => chHW?.Reader;
        public ChannelReader<ThreadMsg>? LOGReader => chLOG?.Reader;

        public void WriteGUI(ThreadMsg msg) => chGUI?.Write(msg);

        public void WriteHW(ThreadMsg msg) => chHW?.Write(msg);

        public void WriteLOG(ThreadMsg msg) => chLOG?.Write(msg);

        /// <summary>
        /// Completes the HW channel so CoreThread.RunThread's blocking read loop
        /// returns false and the dedicated thread exits cleanly on shutdown.
        /// GUI/LOG consumers run on background tasks/threads that die with the
        /// process, so only the HW channel needs explicit completion here.
        /// </summary>
        public void CompleteHW() => chHW?.Complete();
    }
}
