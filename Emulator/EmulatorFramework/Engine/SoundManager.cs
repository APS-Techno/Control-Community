using System.Media;
using System.Text;
using EmulatorCore.Logging;

namespace APS.SystemEmulator.Engine;

/// <summary>
/// Plays short synthesized sounds on device state changes (valve open/close, pump on/off).
/// Subscribes to <see cref="EmulatorLog.EntryAdded"/>.
/// </summary>
public static class SoundManager
{
    private static bool _enabled;
    private static SoundPlayer? _valveOpen;
    private static SoundPlayer? _valveClose;
    private static SoundPlayer? _pumpOn;
    private static SoundPlayer? _pumpOff;
    private static SoundPlayer? _motorLoop;
    private static readonly MotionLoopTracker _motionTracker = new();
    private static System.Windows.Forms.Timer? _pollTimer;
    private const int PollIntervalMs = 200;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
            {
                EnsureLoaded();
                EmulatorLog.EntryAdded += OnLogEntry;
                StartPollTimer();
            }
            else
            {
                EmulatorLog.EntryAdded -= OnLogEntry;
                StopPollTimer();
                _motorLoop?.Stop();
                _motionTracker.Deactivate();
            }
        }
    }

    /// <summary>
    /// Replace the set of motion sources driving the looping motor sound.
    /// Called by ProjectLifecycleController after devices load. Cheap and
    /// audio-free, so safe to call even when sound is disabled (e.g. tests).
    /// </summary>
    public static void SetMotionSources(IEnumerable<Func<bool>> sources)
    {
        _motorLoop?.Stop();
        _motionTracker.Clear();
        _motionTracker.SetSources(sources);
    }

    /// <summary>Drop all motion sources and silence the loop (project teardown).</summary>
    public static void ClearMotionSources()
    {
        _motionTracker.Clear();
        _motorLoop?.Stop();
    }

    private static void EnsureLoaded()
    {
        if (_valveOpen != null) return;
        _valveOpen = CreatePneumaticValve(100, 18f);  // longer open
        _valveClose = CreatePneumaticValve(55, 25f);  // shorter, snappier close
        _pumpOn = CreateTone(280, 180, rising: true);
        _pumpOff = CreateTone(220, 150, rising: false);
        _motorLoop = CreateMotorLoop();
    }

    private static void StartPollTimer()
    {
        if (_pollTimer != null) return;
        _pollTimer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _pollTimer.Tick += (_, _) => PollMotion();
        _pollTimer.Start();
    }

    private static void StopPollTimer()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private static void PollMotion()
    {
        switch (_motionTracker.Tick(PollIntervalMs))
        {
            case MotionLoopAction.Start: _motorLoop?.PlayLooping(); break;
            case MotionLoopAction.Stop: _motorLoop?.Stop(); break;
        }
    }

    private static void OnLogEntry(LogEntry entry)
    {
        if (entry.Source.StartsWith("Valve:"))
        {
            if (entry.Message.Contains("Opened") || entry.Message.Contains("open"))
                PlayOneShot(_valveOpen);
            else if (entry.Message.Contains("Closed"))
                PlayOneShot(_valveClose);
        }
        else if (entry.Source.StartsWith("Pump:"))
        {
            if (entry.Message.Contains("Started"))
                PlayOneShot(_pumpOn);
            else if (entry.Message.Contains("Stopped"))
                PlayOneShot(_pumpOff);
        }
        else if (entry.Source.StartsWith("Turbo:"))
        {
            if (entry.Message.Contains("Commanded ON") || entry.Message.Contains("Spinning up"))
                PlayOneShot(_pumpOn);
            else if (entry.Message.Contains("Stopped"))
                PlayOneShot(_pumpOff);
        }
    }

    private static void PlayOneShot(SoundPlayer? player)
    {
        player?.Play();
        _motionTracker.NotifyOneShot();
    }

    /// <summary>
    /// Pneumatic valve "chpok": low-frequency thump + decaying air-burst noise.
    /// </summary>
    private static SoundPlayer CreatePneumaticValve(int durationMs, float decayRate)
    {
        const int sampleRate = 22050;
        int sampleCount = sampleRate * durationMs / 1000;
        var rng = new Random(7); // deterministic seed for reproducible sound

        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;

            // Envelope: instant attack, exponential decay
            float env = MathF.Exp(-t * decayRate);

            // Impact thump: damped low-frequency sine (~120 Hz)
            float thump = MathF.Sin(2f * MathF.PI * 120f * t) * MathF.Exp(-t * 40f);

            // Air burst: band-limited noise, decays slower than thump
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            // Simple one-pole low-pass (~3 kHz) to remove harsh high frequencies
            if (i > 0) noise = samples[i - 1] * 0.4f + noise * 0.6f;

            samples[i] = env * (thump * 0.7f + noise * 0.5f);
        }

        return BuildWav(samples, sampleRate);
    }

    /// <summary>
    /// Pump on/off: short tonal sweep.
    /// </summary>
    private static SoundPlayer CreateTone(int frequencyHz, int durationMs, bool rising)
    {
        const int sampleRate = 22050;
        int sampleCount = sampleRate * durationMs / 1000;

        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float progress = (float)i / sampleCount;

            float env = 1f;
            int fadeLen = sampleCount / 5;
            if (i < fadeLen) env = (float)i / fadeLen;
            else if (i > sampleCount - fadeLen) env = (float)(sampleCount - i) / fadeLen;

            float freqMult = rising ? (0.7f + 0.6f * progress) : (1.3f - 0.6f * progress);
            float freq = frequencyHz * freqMult;

            samples[i] = MathF.Sin(2f * MathF.PI * freq * t) * env * 0.5f;
        }

        return BuildWav(samples, sampleRate);
    }

    /// <summary>
    /// Stepper-motor running whine: seamless 300 ms loop. Square-ish 600 Hz buzz
    /// (odd harmonics) with a 50 Hz amplitude tremor. Every component frequency
    /// completes an integer number of cycles in the buffer (180 / 540 / 900 / 15),
    /// so PlayLooping is click-free without fades.
    /// </summary>
    private static SoundPlayer CreateMotorLoop()
    {
        const int sampleRate = 22050;
        const float durationSec = 0.3f;
        int sampleCount = (int)(sampleRate * durationSec);

        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;

            // Fundamental + odd harmonics → square-ish buzz (classic stepper whine)
            float buzz = MathF.Sin(2f * MathF.PI * 600f * t)
                       + 0.35f * MathF.Sin(2f * MathF.PI * 1800f * t)
                       + 0.20f * MathF.Sin(2f * MathF.PI * 3000f * t);

            // Slow amplitude tremor for the "grinding" character
            float tremor = 0.8f + 0.2f * MathF.Sin(2f * MathF.PI * 50f * t);

            samples[i] = buzz * tremor * 0.04f; // quiet — below the one-shots
        }

        return BuildWav(samples, sampleRate);
    }

    /// <summary>
    /// Pack float samples (±1.0) into a 16-bit mono WAV and load into SoundPlayer.
    /// </summary>
    private static SoundPlayer BuildWav(float[] samples, int sampleRate)
    {
        int dataSize = samples.Length * 2;
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataSize);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);            // PCM
            w.Write((short)1);            // mono
            w.Write(sampleRate);
            w.Write(sampleRate * 2);      // byte rate
            w.Write((short)2);            // block align
            w.Write((short)16);           // bits per sample
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                short pcm = (short)Math.Clamp(samples[i] * 24000f, short.MinValue, short.MaxValue);
                w.Write(pcm);
            }
        }

        ms.Position = 0;
        var player = new SoundPlayer(ms);
        player.Load();
        return player;
    }
}
