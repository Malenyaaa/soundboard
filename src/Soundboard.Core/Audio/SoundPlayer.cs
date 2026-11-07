using System.Collections.Concurrent;
using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>What happens when a sound starts while another one is playing.</summary>
public enum OverlapMode
{
    /// <summary>The new sound stops all others (default).</summary>
    StopPrevious,

    /// <summary>Sounds play on top of each other.</summary>
    Overlay,
}

/// <summary>
/// Plays pre-decoded <see cref="SoundClip"/>s and mixes them to one float stream.
/// Control methods may be called from any thread; they only enqueue commands.
/// <see cref="Read"/> runs on the audio thread and neither allocates nor blocks.
/// </summary>
public sealed class SoundPlayer : ISampleProvider
{
    public const int MaxVoices = 32;

    /// <summary>Gain changes and stops are ramped over this many frames to avoid clicks.</summary>
    internal const int RampFrames = 256;

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<Command> _commands = new();
    private float _masterVolume = 1f;
    private int _overlap = (int)OverlapMode.StopPrevious;
    private long _startCounter;

    public SoundPlayer(TargetFormat format)
    {
        Format = format;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
    }

    public TargetFormat Format { get; }

    public WaveFormat WaveFormat { get; }

    /// <summary>Master volume for all sounds, 0..1 (values above 1 are allowed but limited later).</summary>
    public float MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set => Volatile.Write(ref _masterVolume, Math.Clamp(value, 0f, 2f));
    }

    public OverlapMode Overlap
    {
        get => (OverlapMode)Volatile.Read(ref _overlap);
        set => Volatile.Write(ref _overlap, (int)value);
    }

    /// <summary>
    /// Starts a clip, from the beginning or at <paramref name="startAt"/> (0..1 of its length). The clip
    /// must be in this player's format.
    /// </summary>
    public void Play(SoundClip clip, float volume, double startAt = 0)
    {
        if (clip.Format != Format)
            throw new ArgumentException($"Clip format {clip.Format} does not match the player format {Format}.", nameof(clip));
        _commands.Enqueue(new Command(CommandKind.Play, clip, clip.Handle, Math.Clamp(volume, 0f, 2f), Math.Clamp(startAt, 0, 1)));
    }

    /// <summary>
    /// Jumps every voice playing this clip to <paramref name="position"/> (0..1 of its length). The sound
    /// comes in with the short ramp, so a jump does not click.
    /// </summary>
    public void Seek(int clipHandle, double position) =>
        _commands.Enqueue(new Command(CommandKind.Seek, null, clipHandle, 0f, Math.Clamp(position, 0, 1)));

    /// <summary>Stops every voice playing this clip.</summary>
    public void Stop(int clipHandle) => _commands.Enqueue(new Command(CommandKind.Stop, null, clipHandle, 0f));

    public void StopAll() => _commands.Enqueue(new Command(CommandKind.StopAll, null, 0, 0f));

    /// <summary>Changes the volume of a clip, also while it plays.</summary>
    public void SetVolume(int clipHandle, float volume) =>
        _commands.Enqueue(new Command(CommandKind.SetVolume, null, clipHandle, Math.Clamp(volume, 0f, 2f)));

    /// <summary>
    /// Clears all voices and pending commands. Only call while no audio thread is reading
    /// (output stopped), e.g. before restarting the output.
    /// </summary>
    public void Reset()
    {
        while (_commands.TryDequeue(out _)) { }
        foreach (var v in _voices) v.Clear();
    }

    /// <summary>
    /// Progress 0..1 of the most recently started, not-stopping voice of this clip, or null if
    /// the clip is not playing. Safe to call from the UI thread (values may be one block old).
    /// </summary>
    public double? GetProgress(int clipHandle)
    {
        double? best = null;
        long bestStart = -1;
        foreach (var v in _voices)
        {
            if (Volatile.Read(ref v.PublishedHandle) != clipHandle) continue;
            long start = Volatile.Read(ref v.PublishedStart);
            if (start <= bestStart) continue;
            int len = Volatile.Read(ref v.PublishedLength);
            int pos = Volatile.Read(ref v.PublishedPosition);
            if (len <= 0) continue;
            bestStart = start;
            best = Math.Clamp((double)pos / len, 0, 1);
        }
        return best;
    }

    /// <summary>Number of voices currently producing sound (UI/diagnostics).</summary>
    public int ActiveVoiceCount
    {
        get
        {
            int n = 0;
            foreach (var v in _voices) if (Volatile.Read(ref v.PublishedHandle) != 0) n++;
            return n;
        }
    }

    /// <summary>Audio thread: fills <paramref name="count"/> samples. Never allocates.</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        DrainCommands();
        Array.Clear(buffer, offset, count);

        int channels = Format.Channels;
        int frames = count / channels;
        float master = MasterVolume;

        for (int vi = 0; vi < _voices.Length; vi++)
        {
            var v = _voices[vi];
            var clip = v.Clip;
            if (clip is null) continue;

            float[] src = clip.Samples;
            int remainingFrames = (src.Length - v.Position) / channels;
            int n = Math.Min(frames, remainingFrames);
            if (v.Stopping) n = Math.Min(n, RampFrames);

            float target = v.Stopping ? 0f : v.TargetGain * master;
            float gain = v.CurrentGain;
            int rampLen = Math.Min(n, RampFrames);
            float step = rampLen > 0 ? (target - gain) / rampLen : 0f;

            int s = v.Position;
            int d = offset;
            for (int f = 0; f < n; f++)
            {
                if (f < rampLen) gain += step;
                else gain = target;
                for (int c = 0; c < channels; c++)
                {
                    buffer[d++] += src[s++] * gain;
                }
            }
            v.Position = s;
            v.CurrentGain = n > 0 ? gain : target;

            bool finished = v.Stopping || v.Position >= src.Length;
            if (finished) v.Clear();
            else v.Publish(clip.FrameCount);
        }

        SoftClip(buffer, offset, count);
        return count;
    }

    /// <summary>Linear up to 0.8, then a smooth knee that never exceeds 1.0.</summary>
    internal static void SoftClip(float[] buffer, int offset, int count)
    {
        const float knee = 0.8f;
        const float range = 1f - knee;
        int end = offset + count;
        for (int i = offset; i < end; i++)
        {
            float x = buffer[i];
            float a = x < 0 ? -x : x;
            if (a <= knee) continue;
            float y = knee + range * MathF.Tanh((a - knee) / range);
            buffer[i] = x < 0 ? -y : y;
        }
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out var cmd))
        {
            switch (cmd.Kind)
            {
                case CommandKind.Play:
                    if (Overlap == OverlapMode.StopPrevious)
                    {
                        foreach (var v in _voices) if (v.Clip is not null) v.BeginStop();
                    }
                    var slot = FindFreeVoice();
                    slot.Start(cmd.Clip!, cmd.Value, ++_startCounter, MasterVolume);
                    if (cmd.Position > 0) slot.JumpTo(cmd.Position);
                    break;

                case CommandKind.Seek:
                    foreach (var v in _voices)
                        if (v.Clip is not null && v.Handle == cmd.Handle && !v.Stopping) v.JumpTo(cmd.Position);
                    break;

                case CommandKind.Stop:
                    foreach (var v in _voices) if (v.Clip is not null && v.Handle == cmd.Handle) v.BeginStop();
                    break;

                case CommandKind.StopAll:
                    foreach (var v in _voices) if (v.Clip is not null) v.BeginStop();
                    break;

                case CommandKind.SetVolume:
                    foreach (var v in _voices) if (v.Clip is not null && v.Handle == cmd.Handle) v.TargetGain = cmd.Value;
                    break;
            }
        }
    }

    private Voice FindFreeVoice()
    {
        Voice? oldest = null;
        foreach (var v in _voices)
        {
            if (v.Clip is null) return v;
            if (oldest is null || v.StartOrder < oldest.StartOrder) oldest = v;
        }
        // All slots busy: reuse the oldest (hard cut, rare).
        oldest!.Clear();
        return oldest;
    }

    private enum CommandKind
    {
        Play,
        Stop,
        StopAll,
        SetVolume,
        Seek,
    }

    private readonly record struct Command(CommandKind Kind, SoundClip? Clip, int Handle, float Value, double Position = 0);

    /// <summary>One playing instance. Owned by the audio thread; Published* fields are for the UI.</summary>
    private sealed class Voice
    {
        public SoundClip? Clip;
        public int Handle;
        public int Position;
        public float TargetGain;
        public float CurrentGain;
        public bool Stopping;
        public long StartOrder;

        public int PublishedHandle;
        public int PublishedPosition;
        public int PublishedLength;
        public long PublishedStart;

        public void Start(SoundClip clip, float gain, long order, float master)
        {
            Clip = clip;
            Handle = clip.Handle;
            Position = 0;
            TargetGain = gain;
            Stopping = false;
            StartOrder = order;
            CurrentGain = gain * master; // no fade-in: the sound starts at full level
            Publish(clip.FrameCount);
        }

        /// <summary>Continues at <paramref name="position"/> (0..1), on a whole frame, fading in from silence.</summary>
        public void JumpTo(double position)
        {
            var clip = Clip!;
            int frame = Math.Min((int)(position * clip.FrameCount), Math.Max(clip.FrameCount - 1, 0));
            Position = frame * clip.Format.Channels;
            CurrentGain = 0f;
            Publish(clip.FrameCount);
        }

        public void BeginStop()
        {
            Stopping = true;
            Volatile.Write(ref PublishedHandle, 0);
        }

        public void Publish(int lengthFrames)
        {
            Volatile.Write(ref PublishedLength, lengthFrames);
            Volatile.Write(ref PublishedPosition, Clip is null ? 0 : Position / Clip.Format.Channels);
            Volatile.Write(ref PublishedStart, StartOrder);
            Volatile.Write(ref PublishedHandle, Stopping ? 0 : Handle);
        }

        public void Clear()
        {
            Volatile.Write(ref PublishedHandle, 0);
            Clip = null;
            Handle = 0;
            Position = 0;
            Stopping = false;
            CurrentGain = 0f;
        }
    }
}
