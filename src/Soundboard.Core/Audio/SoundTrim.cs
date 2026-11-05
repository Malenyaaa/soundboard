namespace Soundboard.Core.Audio;

/// <summary>
/// Cutting a pad's sound to a part of its file: start and end in seconds, stored with the pad.
/// The file is never changed; the pad plays a copy of the chosen part.
/// </summary>
public static class SoundTrim
{
    /// <summary>Shortest part that can be kept.</summary>
    public const double MinLength = 0.05;

    /// <summary>
    /// Start and end as they are stored: null where nothing is cut (start at 0, end at the end),
    /// clamped into the sound and at least <see cref="MinLength"/> apart.
    /// </summary>
    public static (double? Start, double? End) Normalize(double? start, double? end, double duration)
    {
        if (duration <= 0) return (null, null);
        double s = Math.Clamp(start ?? 0, 0, duration);
        double e = Math.Clamp(end ?? duration, 0, duration);
        if (e - s < MinLength)
        {
            // Too short or crossed: keep the start, push the end out (or the start back at the very end).
            e = Math.Min(duration, s + MinLength);
            s = Math.Max(0, e - MinLength);
        }
        const double edge = 0.0005; // half a millisecond: counts as "not cut"
        return (s <= edge ? null : s, e >= duration - edge ? null : e);
    }

    /// <summary>The part of <paramref name="full"/> from start to end; the clip itself when nothing is cut.</summary>
    public static SoundClip Apply(SoundClip full, double? start, double? end)
    {
        var (s, e) = Normalize(start, end, full.Duration.TotalSeconds);
        if (s is null && e is null) return full;
        int ch = full.Format.Channels;
        int rate = full.Format.SampleRate;
        int from = (int)Math.Round((s ?? 0) * rate);
        int to = e is { } end2 ? (int)Math.Round(end2 * rate) : full.FrameCount;
        from = Math.Clamp(from, 0, full.FrameCount);
        to = Math.Clamp(to, from, full.FrameCount);
        var samples = new float[(to - from) * ch];
        Array.Copy(full.Samples, from * ch, samples, 0, samples.Length);
        return new SoundClip(samples, full.Format);
    }

    /// <summary>Highest absolute level per column (all channels), 0..1, for drawing the waveform.</summary>
    public static float[] Peaks(SoundClip clip, int columns)
    {
        columns = Math.Max(1, columns);
        var peaks = new float[columns];
        int frames = clip.FrameCount;
        if (frames == 0) return peaks;
        int ch = clip.Format.Channels;
        var s = clip.Samples;
        for (int c = 0; c < columns; c++)
        {
            int f0 = (int)((long)frames * c / columns);
            int f1 = Math.Max(f0 + 1, (int)((long)frames * (c + 1) / columns));
            float max = 0;
            for (int i = f0 * ch, end = Math.Min(s.Length, f1 * ch); i < end; i++)
            {
                float a = Math.Abs(s[i]);
                if (a > max) max = a;
            }
            peaks[c] = Math.Min(1f, max);
        }
        return peaks;
    }
}
