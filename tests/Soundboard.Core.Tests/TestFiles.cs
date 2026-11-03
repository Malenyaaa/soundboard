using NAudio.Wave;

namespace Soundboard.Core.Tests;

/// <summary>Temporary folder per test, deleted afterwards.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "soundboard-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

internal static class WavFactory
{
    /// <summary>Writes a 16-bit PCM sine wave.</summary>
    public static void WriteSine(string path, int sampleRate, int channels, double seconds, double freq = 440, double amplitude = 0.5)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        int frames = (int)(sampleRate * seconds);
        for (int f = 0; f < frames; f++)
        {
            float v = (float)(amplitude * Math.Sin(2 * Math.PI * freq * f / sampleRate));
            for (int c = 0; c < channels; c++) writer.WriteSample(v);
        }
    }

    /// <summary>Writes a 32-bit float WAV with a constant value (handy for exact checks).</summary>
    public static void WriteConstant(string path, int sampleRate, int channels, int frames, float value)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
        for (int i = 0; i < frames * channels; i++) writer.WriteSample(value);
    }
}
