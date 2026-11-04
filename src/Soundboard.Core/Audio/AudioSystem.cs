using NAudio.Wave;
using Soundboard.Core.Audio.Sinks;

namespace Soundboard.Core.Audio;

/// <summary>An output that pulls float samples from a source (device or file).</summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Endpoint ID, or a file path for the file sink.</summary>
    string DeviceId { get; }

    /// <summary>Format the source must deliver (float, rate, channels).</summary>
    TargetFormat SourceFormat { get; }

    void Start(ISampleProvider source);

    void Stop();

    /// <summary>Raised when the output stops on its own (device removed, driver error, file done).</summary>
    event EventHandler<Exception?>? Stopped;
}

/// <summary>
/// Access to Windows audio, replaceable for previews and tests. Every implementation is
/// read-only towards Windows: it lists endpoints and opens them by ID in shared mode.
/// </summary>
public interface IAudioSystem : IDisposable
{
    /// <summary>Endpoint added/removed/changed or a default changed. Raised on any thread.</summary>
    event EventHandler? DevicesChanged;

    void StartWatching();

    AudioStateReport ReadState();

    /// <summary>Mix format of an active endpoint, or null.</summary>
    MixFormatInfo? ReadMixFormat(string id);

    /// <summary>Opens an active render endpoint by ID; null if it is not there or not active.</summary>
    IAudioOutput? OpenOutput(string id, int latencyMs);
}

/// <summary>The real thing: WASAPI via <see cref="DeviceCatalog"/>.</summary>
public sealed class WasapiAudioSystem : IAudioSystem
{
    private readonly DeviceCatalog _catalog = new();

    public WasapiAudioSystem() => _catalog.DevicesChanged += (s, e) => DevicesChanged?.Invoke(this, e);

    public event EventHandler? DevicesChanged;

    public void StartWatching() => _catalog.StartWatching();

    public AudioStateReport ReadState() => _catalog.ReadState();

    public MixFormatInfo? ReadMixFormat(string id)
    {
        using var dev = _catalog.OpenActive(id);
        return dev is null ? null : DeviceCatalog.ReadMixFormat(dev);
    }

    public IAudioOutput? OpenOutput(string id, int latencyMs)
    {
        var dev = _catalog.OpenActive(id);
        if (dev is null) return null;
        try
        {
            return new WasapiSink(dev, latencyMs);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            dev.Dispose();
            throw new SinkException($"Das Gerät antwortet nicht ({ex.Message}).", ex);
        }
    }

    public void Dispose() => _catalog.Dispose();
}
