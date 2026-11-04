using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace Soundboard.Core.Audio;

/// <summary>Sample rate and channel count the sounds are decoded to (the output mix format).</summary>
public readonly record struct TargetFormat(int SampleRate, int Channels)
{
    public static readonly TargetFormat Fallback = new(48000, 2);
    public override string ToString() => Text.L.F("Format.HzChannels", SampleRate, Channels);
}

/// <summary>A fully decoded sound: interleaved float samples in the target format.</summary>
public sealed class SoundClip
{
    private static int _nextHandle;

    public SoundClip(float[] samples, TargetFormat format)
    {
        Samples = samples;
        Format = format;
        Handle = Interlocked.Increment(ref _nextHandle);
    }

    /// <summary>Process-wide unique number; used by the player instead of references across threads.</summary>
    public int Handle { get; }
    public float[] Samples { get; }
    public TargetFormat Format { get; }
    public int FrameCount => Samples.Length / Format.Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / Format.SampleRate);
}

/// <summary>A sound file could not be used. Message is a user-facing hint in the language of the interface.</summary>
public sealed class SoundLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Decodes sound files to <see cref="SoundClip"/>s in a given target format.</summary>
public static class SoundDecoder
{
    /// <summary>File extensions accepted by drag &amp; drop.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".mp3", ".wav", ".ogg", ".flac"];

    /// <summary>Longest sound accepted; everything is held in memory.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(15);

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Decodes a whole file. Throws <see cref="SoundLoadException"/> with a user hint.</summary>
    public static SoundClip Decode(string path, TargetFormat target)
    {
        if (!File.Exists(path))
            throw new SoundLoadException(Text.L.F("Sound.Missing", path));
        if (!IsSupported(path))
            throw new SoundLoadException(Text.L.F("Sound.FormatUnsupported", Path.GetExtension(path)));

        IDisposable? owner = null;
        try
        {
            var source = OpenSource(path, out owner);
            return Convert(source, target, Path.GetFileName(path));
        }
        catch (SoundLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is COMException or InvalidDataException or FormatException
                                       or ArgumentException or NotSupportedException or IOException
                                       or InvalidOperationException or EndOfStreamException)
        {
            throw new SoundLoadException(
                Text.L.F("Sound.Unreadable", Path.GetFileName(path), Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), ex.Message), ex);
        }
        finally
        {
            owner?.Dispose();
        }
    }

    private static ISampleProvider OpenSource(string path, out IDisposable? owner)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ogg")
        {
            var vorbis = new VorbisReader(path);
            owner = vorbis;
            return new VorbisSampleProvider(vorbis);
        }

        if (ext == ".wav")
        {
            // Managed reader for plain PCM/float WAV (works without Media Foundation).
            WaveFileReader? wav = null;
            try
            {
                wav = new WaveFileReader(path);
                var enc = wav.WaveFormat.Encoding;
                if (enc is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible)
                {
                    var sp = wav.ToSampleProvider();
                    owner = wav;
                    return sp;
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                // Fall through to Media Foundation (e.g. ADPCM, unusual headers).
            }
            wav?.Dispose();
        }

        // MP3, FLAC and other WAV variants via Media Foundation (Windows 10+).
        var mf = new MediaFoundationReader(path, new MediaFoundationReader.MediaFoundationReaderSettings
        {
            RequestFloatOutput = true,
        });
        owner = mf;
        return mf.ToSampleProvider();
    }

    /// <summary>Reads a sample provider to the end, resamples and maps channels.</summary>
    internal static SoundClip Convert(ISampleProvider source, TargetFormat target, string displayName)
    {
        int srcChannels = source.WaveFormat.Channels;
        if (srcChannels < 1)
            throw new SoundLoadException(Text.L.F("Sound.NoAudio", displayName));

        ISampleProvider stream = source;
        if (source.WaveFormat.SampleRate != target.SampleRate)
            stream = new WdlResamplingSampleProvider(source, target.SampleRate);

        long maxSamples = (long)(MaxDuration.TotalSeconds * target.SampleRate) * srcChannels;
        var chunk = new float[target.SampleRate * srcChannels / 10 * 2]; // ~200 ms
        var all = new float[Math.Max(chunk.Length, target.SampleRate * srcChannels)];
        int filled = 0;
        while (true)
        {
            int read = stream.Read(chunk, 0, chunk.Length);
            if (read <= 0) break;
            if (filled + read > maxSamples)
                throw new SoundLoadException(Text.L.F("Sound.TooLong", displayName, MaxDuration.TotalMinutes));
            if (filled + read > all.Length)
                Array.Resize(ref all, (int)Math.Min(maxSamples, Math.Max((long)all.Length * 2, filled + read)));
            Array.Copy(chunk, 0, all, filled, read);
            filled += read;
        }

        int frames = filled / srcChannels;
        if (frames == 0)
            throw new SoundLoadException(Text.L.F("Sound.Empty", displayName));

        var mapped = MapChannels(all, frames, srcChannels, target.Channels);
        return new SoundClip(mapped, target);
    }

    /// <summary>
    /// Maps interleaved frames from <paramref name="srcCh"/> to <paramref name="dstCh"/> channels.
    /// Mono target: average of all source channels. Otherwise the first two target channels get
    /// left/right (mono is copied to both), further source channels go to matching target channels,
    /// and remaining target channels stay silent.
    /// </summary>
    internal static float[] MapChannels(float[] src, int frames, int srcCh, int dstCh)
    {
        var dst = new float[frames * dstCh];
        if (srcCh == dstCh)
        {
            Array.Copy(src, dst, frames * dstCh);
            return dst;
        }

        for (int f = 0; f < frames; f++)
        {
            int si = f * srcCh;
            int di = f * dstCh;
            if (dstCh == 1)
            {
                float sum = 0;
                for (int c = 0; c < srcCh; c++) sum += src[si + c];
                dst[di] = sum / srcCh;
            }
            else if (srcCh == 1)
            {
                dst[di] = src[si];
                dst[di + 1] = src[si];
            }
            else
            {
                int n = Math.Min(srcCh, dstCh);
                for (int c = 0; c < n; c++) dst[di + c] = src[si + c];
            }
        }
        return dst;
    }

    private sealed class VorbisSampleProvider(VorbisReader reader) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
        public int Read(float[] buffer, int offset, int count) => reader.ReadSamples(buffer, offset, count);
    }
}

/// <summary>
/// Holds decoded sounds in memory, keyed by tile ID. Decoding runs on a worker thread;
/// the audio thread only ever sees finished <see cref="SoundClip"/>s.
/// </summary>
public sealed class SoundBank
{
    private readonly ConcurrentDictionary<Guid, SoundClip> _clips = new();

    public SoundBank(TargetFormat target) => Target = target;

    /// <summary>Format all clips are decoded to. Changing it requires <see cref="Clear"/> and reloading.</summary>
    public TargetFormat Target { get; private set; }

    public int Count => _clips.Count;

    /// <summary>Drops all clips and switches to a new target format.</summary>
    public void Reset(TargetFormat target)
    {
        _clips.Clear();
        Target = target;
    }

    /// <summary>Decodes a file on a worker thread and stores the clip under <paramref name="id"/>.</summary>
    public async Task<SoundClip> LoadAsync(Guid id, string path, CancellationToken ct = default)
    {
        var target = Target;
        var clip = await Task.Run(() => SoundDecoder.Decode(path, target), ct).ConfigureAwait(false);
        // Only keep it if the target did not change meanwhile.
        if (clip.Format == Target) _clips[id] = clip;
        return clip;
    }

    public bool TryGet(Guid id, out SoundClip clip) => _clips.TryGetValue(id, out clip!);

    public void Remove(Guid id) => _clips.TryRemove(id, out _);

    public void Clear() => _clips.Clear();
}
