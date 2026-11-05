using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Soundboard.Core.Audio;

/// <summary>
/// Reads the microphone ring buffer as a sample stream. Waits until the buffer holds the
/// target fill before it starts (pre-roll), outputs silence and counts an underrun when the
/// microphone falls behind, and skips ahead when it runs ahead (clock drift between devices).
/// </summary>
public sealed class RingBufferSampleProvider : ISampleProvider
{
    private readonly FloatRingBuffer _ring;
    private readonly int _targetFill;
    private bool _primed;
    private long _underruns;

    public RingBufferSampleProvider(FloatRingBuffer ring, TargetFormat format, int targetMs)
    {
        _ring = ring;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        _targetFill = Math.Max(format.Channels, format.SampleRate * format.Channels / 1000 * targetMs);
    }

    public WaveFormat WaveFormat { get; }

    public long Underruns => Volatile.Read(ref _underruns);

    public int Read(float[] buffer, int offset, int count)
    {
        int available = _ring.Available;
        if (!_primed)
        {
            if (available < _targetFill)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            _primed = true;
        }

        // Far ahead: drop the surplus so the delay stays near the target.
        int ceiling = _targetFill * 2 + count;
        if (available > ceiling)
        {
            int surplus = available - _targetFill - count;
            surplus -= surplus % WaveFormat.Channels;
            _ring.Skip(surplus);
        }

        int read = _ring.Read(buffer.AsSpan(offset, count));
        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
            Volatile.Write(ref _underruns, _underruns + 1);
            _primed = false; // build the reserve up again
        }
        return count;
    }
}

/// <summary>Maps channels of a float stream without allocating per call.</summary>
public sealed class ChannelMapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _srcCh;
    private readonly int _dstCh;
    private float[] _scratch;

    public ChannelMapSampleProvider(ISampleProvider source, int channels)
    {
        _source = source;
        _srcCh = source.WaveFormat.Channels;
        _dstCh = channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);
        _scratch = new float[source.WaveFormat.SampleRate * _srcCh];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / _dstCh;
        int need = frames * _srcCh;
        if (need > _scratch.Length) _scratch = new float[need]; // only for > 1 s blocks
        int got = _source.Read(_scratch, 0, need) / _srcCh;
        for (int f = 0; f < got; f++)
        {
            int si = f * _srcCh;
            int di = offset + f * _dstCh;
            if (_dstCh == 1)
            {
                float sum = 0;
                for (int c = 0; c < _srcCh; c++) sum += _scratch[si + c];
                buffer[di] = sum / _srcCh;
            }
            else if (_srcCh == 1)
            {
                float v = _scratch[si];
                buffer[di] = v;
                buffer[di + 1] = v;
                for (int c = 2; c < _dstCh; c++) buffer[di + c] = 0f;
            }
            else
            {
                int n = Math.Min(_srcCh, _dstCh);
                for (int c = 0; c < n; c++) buffer[di + c] = _scratch[si + c];
                for (int c = n; c < _dstCh; c++) buffer[di + c] = 0f;
            }
        }
        return got * _dstCh;
    }
}

/// <summary>
/// Mixes microphone and sounds into one float stream in the output format: per input
/// resampling and channel mapping, microphone gain, optional "block voice while a sound
/// plays", a peak limiter and soft clipping. <see cref="Read"/> runs on the audio thread and
/// neither allocates nor blocks once warmed up.
/// </summary>
public sealed class Mixer : ISampleProvider
{
    /// <summary>Limiter ceiling before the soft clip.</summary>
    internal const float Threshold = 0.9f;

    private const int VoiceRampFrames = 480;

    private readonly ISampleProvider? _voice;
    private readonly ISampleProvider? _sounds;
    private readonly Func<bool>? _soundsActive;
    private readonly int _channels;
    private readonly float _release;
    private float[] _voiceBuf;
    private float[] _soundBuf;
    private float _limiterGain = 1f;
    private float _voiceGainNow = 1f;
    private float _voiceVolume = 1f;
    private float _soundVolume = 1f;
    private int _blockVoice;
    private float _peak;
    private int _toneFramesLeft; // audio thread only
    private int _toneRequest;    // set by the UI, taken by the audio thread
    private double _tonePhase;
    private readonly double _toneStep;

    /// <param name="format">Output format (the sink's).</param>
    /// <param name="voice">Microphone stream in any rate/channel count, or null.</param>
    /// <param name="sounds">Sound player stream in any rate/channel count, or null.</param>
    /// <param name="soundsActive">True while a sound plays (for "block voice"). Must not allocate.</param>
    public Mixer(TargetFormat format, ISampleProvider? voice, ISampleProvider? sounds, Func<bool>? soundsActive = null)
    {
        Format = format;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        _channels = format.Channels;
        _voice = voice is null ? null : Adapt(voice, format);
        _sounds = sounds is null ? null : Adapt(sounds, format);
        _soundsActive = soundsActive;
        _voiceBuf = new float[format.SampleRate * format.Channels];
        _soundBuf = new float[format.SampleRate * format.Channels];
        _release = 1f - MathF.Exp(-1f / (0.15f * format.SampleRate)); // ~150 ms back to unity
        _toneStep = 2 * Math.PI * 440.0 / format.SampleRate;
    }

    public TargetFormat Format { get; }

    public WaveFormat WaveFormat { get; }

    /// <summary>Microphone level 0..2 (internal, not the Windows volume).</summary>
    public float VoiceVolume
    {
        get => Volatile.Read(ref _voiceVolume);
        set => Volatile.Write(ref _voiceVolume, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>
    /// Level of the sounds in this mix, 0..2, on top of the player's master volume. The monitor does not
    /// get it: what is fine on one's own speakers (turned down in Windows) is often far too loud for
    /// the others next to the voice.
    /// </summary>
    public float SoundVolume
    {
        get => Volatile.Read(ref _soundVolume);
        set => Volatile.Write(ref _soundVolume, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>Mute the microphone while a sound plays ("Stimme blockieren").</summary>
    public bool BlockVoiceWhileSound
    {
        get => Volatile.Read(ref _blockVoice) != 0;
        set => Volatile.Write(ref _blockVoice, value ? 1 : 0);
    }

    /// <summary>Highest absolute sample since the last <see cref="TakePeak"/> (for level meters).</summary>
    public float TakePeak() => Interlocked.Exchange(ref _peak, 0f);

    /// <summary>Adds a one-second 440 Hz test tone to the output (not to the monitor).</summary>
    public void PlayTestTone() => Interlocked.Exchange(ref _toneRequest, 1);

    /// <summary>Resample first (fewer channels is cheaper), then map channels.</summary>
    private static ISampleProvider Adapt(ISampleProvider source, TargetFormat target)
    {
        var s = source;
        if (s.WaveFormat.SampleRate != target.SampleRate) s = new WdlResamplingSampleProvider(s, target.SampleRate);
        if (s.WaveFormat.Channels != target.Channels) s = new ChannelMapSampleProvider(s, target.Channels);
        return s;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        count -= count % _channels;
        if (count > _voiceBuf.Length)
        {
            _voiceBuf = new float[count]; // only if a sink asks for > 1 s at once
            _soundBuf = new float[count];
        }

        int voiceRead = _voice is null ? 0 : FillFrom(_voice, _voiceBuf, count);
        int soundRead = _sounds is null ? 0 : FillFrom(_sounds, _soundBuf, count);

        bool block = BlockVoiceWhileSound && _soundsActive is not null && _soundsActive();
        float voiceTarget = block ? 0f : VoiceVolume;
        float voiceStep = (voiceTarget - _voiceGainNow) / VoiceRampFrames;
        float voiceGain = _voiceGainNow;
        float soundGain = SoundVolume;

        if (Interlocked.Exchange(ref _toneRequest, 0) != 0) _toneFramesLeft = Format.SampleRate;
        int toneLeft = _toneFramesLeft;
        float peak = 0f;
        float gain = _limiterGain;
        int frames = count / _channels;
        for (int f = 0; f < frames; f++)
        {
            if (voiceGain != voiceTarget)
            {
                voiceGain += voiceStep;
                if ((voiceStep > 0 && voiceGain > voiceTarget) || (voiceStep < 0 && voiceGain < voiceTarget)) voiceGain = voiceTarget;
            }

            float tone = 0f;
            if (toneLeft > 0)
            {
                // Short fade in/out against clicks.
                float env = Math.Min(1f, Math.Min((Format.SampleRate - toneLeft) / 480f, toneLeft / 480f));
                tone = 0.25f * env * (float)Math.Sin(_tonePhase);
                _tonePhase += _toneStep;
                if (_tonePhase > 2 * Math.PI) _tonePhase -= 2 * Math.PI;
                toneLeft--;
            }

            int i0 = f * _channels;
            float framePeak = 0f;
            for (int c = 0; c < _channels; c++)
            {
                int i = i0 + c;
                float v = (i < voiceRead ? _voiceBuf[i] * voiceGain : 0f) + (i < soundRead ? _soundBuf[i] * soundGain : 0f) + tone;
                _soundBuf[i] = v; // reuse as output scratch
                float a = v < 0 ? -v : v;
                if (a > framePeak) framePeak = a;
            }

            // Peak limiter: instant attack, smooth release.
            float wanted = framePeak * gain > Threshold ? Threshold / framePeak : 1f;
            if (wanted < gain) gain = wanted;
            else gain += (1f - gain) * _release;

            for (int c = 0; c < _channels; c++)
            {
                float v = _soundBuf[i0 + c] * gain;
                buffer[offset + i0 + c] = v;
                float a = v < 0 ? -v : v;
                if (a > peak) peak = a;
            }
        }
        _limiterGain = gain;
        _voiceGainNow = voiceGain;
        _toneFramesLeft = toneLeft;

        SoundPlayer.SoftClip(buffer, offset, count);
        if (peak > 1f) peak = 1f;
        float old = Volatile.Read(ref _peak);
        if (peak > old) Volatile.Write(ref _peak, peak);
        return count;
    }

    /// <summary>Reads until <paramref name="count"/> samples are there or the source has no more.</summary>
    private static int FillFrom(ISampleProvider source, float[] buf, int count)
    {
        int filled = 0;
        int guard = 0;
        while (filled < count && guard++ < 8)
        {
            int n = source.Read(buf, filled, count - filled);
            if (n <= 0) break;
            filled += n;
        }
        if (filled < count) Array.Clear(buf, filled, count - filled);
        return count;
    }
}
