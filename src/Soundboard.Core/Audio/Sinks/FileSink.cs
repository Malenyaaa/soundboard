using System.Diagnostics;
using NAudio.Wave;

namespace Soundboard.Core.Audio.Sinks;

/// <summary>
/// Test output without hardware: pulls the mix in real time (10 ms blocks, paced by the
/// clock like a sound card) into memory and writes a 32-bit float WAV when the time is up.
/// The memory for the whole recording is allocated before the start.
/// </summary>
public sealed class FileSink : IAudioOutput
{
    private const int BlockMs = 10;

    private readonly string _path;
    private readonly float[] _data;
    private readonly SynchronizationContext? _context;
    private Thread? _thread;
    private volatile bool _stop;
    private ISampleProvider? _source;

    public FileSink(string path, TargetFormat format, TimeSpan duration)
    {
        _path = path;
        SourceFormat = format;
        Duration = duration;
        _data = new float[(int)(duration.TotalSeconds * format.SampleRate) * format.Channels];
        _context = SynchronizationContext.Current;
    }

    public string DeviceId => _path;

    public TargetFormat SourceFormat { get; }

    public TimeSpan Duration { get; }

    /// <summary>0..1 while recording.</summary>
    public double Progress { get; private set; }

    /// <summary>Raised when the file is written (null) or writing failed (the exception).</summary>
    public event EventHandler<Exception?>? Stopped;

    public void Start(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != SourceFormat.SampleRate || source.WaveFormat.Channels != SourceFormat.Channels)
            throw new ArgumentException("Quelle muss im Format der Datei liefern.", nameof(source));
        if (_thread is not null) return;
        _source = source;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "Datei-Test", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Cancels; no file is written.</summary>
    public void Stop()
    {
        _stop = true;
        var t = _thread;
        _thread = null;
        if (t is not null && t != Thread.CurrentThread) t.Join(1000);
    }

    private void Run()
    {
        var source = _source!;
        int ch = SourceFormat.Channels;
        int block = SourceFormat.SampleRate * BlockMs / 1000 * ch;
        int filled = 0;
        var clock = Stopwatch.StartNew();
        while (!_stop && filled < _data.Length)
        {
            long due = (long)(clock.Elapsed.TotalSeconds * SourceFormat.SampleRate) * ch;
            while (filled < due && filled < _data.Length)
            {
                int n = Math.Min(block, _data.Length - filled);
                source.Read(_data, filled, n);
                filled += n;
            }
            Progress = (double)filled / _data.Length;
            Thread.Sleep(BlockMs / 2);
        }
        if (_stop) return;

        Exception? error = null;
        try
        {
            WriteWav(_path, SourceFormat, _data, _data.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
        }
        if (_context is not null) _context.Post(_ => Stopped?.Invoke(this, error), null);
        else Stopped?.Invoke(this, error);
    }

    /// <summary>Renders <paramref name="frames"/> frames at once, without waiting (for tests).</summary>
    public static void RenderOffline(string path, ISampleProvider source, int frames)
    {
        var fmt = new TargetFormat(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
        var data = new float[frames * fmt.Channels];
        int block = fmt.SampleRate / 100 * fmt.Channels;
        for (int filled = 0; filled < data.Length; filled += block)
            source.Read(data, filled, Math.Min(block, data.Length - filled));
        WriteWav(path, fmt, data, data.Length);
    }

    private static void WriteWav(string path, TargetFormat format, float[] data, int count)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels));
        writer.WriteSamples(data, 0, count);
    }

    public void Dispose() => Stop();
}
