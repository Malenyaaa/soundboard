namespace Soundboard.Core.Audio;

/// <summary>How loud a sound is: its peak and the RMS of its audible part.</summary>
public readonly record struct LoudnessInfo(double Peak, double Rms)
{
    public bool IsSilent => Rms <= 0;
}

/// <summary>
/// Evening out the pads: measure each sound (decoded samples), then set the pad volume so the
/// loud ones come down to the level of the middle one. The files are never touched, only the
/// pad volume, and nothing is made louder than 100 %.
/// </summary>
public static class Loudness
{
    /// <summary>Blocks quieter than this (−50 dBFS RMS) count as silence and are left out of the RMS.</summary>
    public const double GateRms = 0.00316;

    /// <summary>Peak and RMS of <paramref name="samples"/> (interleaved), the RMS over 50-ms blocks above the gate.</summary>
    public static LoudnessInfo Analyze(ReadOnlySpan<float> samples, int channels, int sampleRate)
    {
        if (samples.Length == 0 || channels <= 0 || sampleRate <= 0) return default;
        int block = Math.Max(channels, sampleRate / 20 * channels);
        double peak = 0, sum = 0;
        long count = 0;
        for (int start = 0; start < samples.Length; start += block)
        {
            var part = samples.Slice(start, Math.Min(block, samples.Length - start));
            double s = 0;
            foreach (float v in part)
            {
                double a = Math.Abs(v);
                if (a > peak) peak = a;
                s += (double)v * v;
            }
            if (Math.Sqrt(s / part.Length) < GateRms) continue;
            sum += s;
            count += part.Length;
        }
        return new LoudnessInfo(peak, count == 0 ? 0 : Math.Sqrt(sum / count));
    }

    /// <summary>The level everything is brought to: the median RMS of the sounds that are not silent.</summary>
    public static double Target(IEnumerable<LoudnessInfo> all)
    {
        var levels = all.Where(l => !l.IsSilent).Select(l => l.Rms).Order().ToList();
        if (levels.Count == 0) return 0;
        int mid = levels.Count / 2;
        return levels.Count % 2 == 1 ? levels[mid] : (levels[mid - 1] + levels[mid]) / 2;
    }

    /// <summary>
    /// Pad volume (0..1, whole percent) that brings <paramref name="sound"/> to <paramref name="target"/>.
    /// Quieter sounds stay at 100 % (never louder than the file); silence gives null (leave as is).
    /// </summary>
    public static float? VolumeFor(LoudnessInfo sound, double target)
    {
        if (sound.IsSilent || target <= 0) return null;
        double v = Math.Min(1.0, target / sound.Rms);
        return (float)(Math.Round(v * 100) / 100);
    }
}
