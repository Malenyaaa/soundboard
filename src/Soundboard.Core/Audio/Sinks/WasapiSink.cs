using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Soundboard.Core.Audio.Sinks;

/// <summary>
/// Renders a float stream to one endpoint, opened by ID. WASAPI shared mode, event driven,
/// in the endpoint's own mix format — the device is never reconfigured.
/// </summary>
public sealed class WasapiSink : IAudioOutput
{
    private readonly MMDevice _device;
    private WasapiOut? _out;

    /// <summary>Takes ownership of <paramref name="device"/>.</summary>
    public WasapiSink(MMDevice device, int latencyMs = 30)
    {
        _device = device;
        DeviceId = device.ID;
        LatencyMs = latencyMs;
        using var client = device.AudioClient;
        MixFormat = client.MixFormat;
    }

    public string DeviceId { get; }
    public int LatencyMs { get; }

    /// <summary>The endpoint's shared-mode mix format (read only).</summary>
    public WaveFormat MixFormat { get; }

    /// <summary>The format sources must deliver: float, mix rate, mix channels.</summary>
    public TargetFormat SourceFormat => new(MixFormat.SampleRate, MixFormat.Channels);

    public bool IsRunning => _out?.PlaybackState == PlaybackState.Playing;

    /// <summary>
    /// Raised when playback stops on its own (device removed, driver error). Raised on the
    /// thread that created the sink if it has a synchronization context (UI thread).
    /// </summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>Starts rendering <paramref name="source"/>. Throws <see cref="SinkException"/> with a user hint.</summary>
    public void Start(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != MixFormat.SampleRate || source.WaveFormat.Channels != MixFormat.Channels)
            throw new ArgumentException("The source must deliver the mix format of the device.", nameof(source));

        StopInternal(raise: false);
        try
        {
            var provider = DeviceFormatProvider.Create(source, MixFormat);
            var o = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, LatencyMs);
            o.PlaybackStopped += OnPlaybackStopped;
            o.Init(provider);
            o.Play();
            _out = o;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            StopInternal(raise: false);
            throw new SinkException(Text.L.F("Output.OpenFailed", ex.Message), ex);
        }
    }

    public void Stop() => StopInternal(raise: false);

    private void StopInternal(bool raise)
    {
        var o = _out;
        _out = null;
        if (o is null) return;
        o.PlaybackStopped -= OnPlaybackStopped;
        try { o.Stop(); } catch (COMException) { }
        o.Dispose();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _out)) return;
        StopInternal(raise: false);
        Stopped?.Invoke(this, e.Exception);
    }

    public void Dispose()
    {
        StopInternal(raise: false);
        _device.Dispose();
    }

    /// <summary>
    /// Presents a float sample provider as bytes in exactly the device's mix format, so WASAPI
    /// needs no conversion. Preallocated buffer; no allocation per callback.
    /// </summary>
    private sealed class DeviceFormatProvider : IWaveProvider
    {
        private readonly ISampleProvider _source;
        private float[] _scratch;

        private DeviceFormatProvider(ISampleProvider source, WaveFormat format)
        {
            _source = source;
            WaveFormat = format;
            _scratch = new float[format.SampleRate * format.Channels]; // 1 s, far more than one period
        }

        public WaveFormat WaveFormat { get; }

        public static IWaveProvider Create(ISampleProvider source, WaveFormat mix)
        {
            bool mixIsFloat32 = mix.BitsPerSample == 32 && (mix.Encoding == WaveFormatEncoding.IeeeFloat
                || (mix is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT));
            if (mixIsFloat32) return new DeviceFormatProvider(source, mix);

            // Unusual: integer mix format. Let NAudio convert to 16-bit PCM in the same rate/channels.
            return new NAudio.Wave.SampleProviders.SampleToWaveProvider16(source);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int samples = count / 4;
            if (samples > _scratch.Length) _scratch = new float[samples]; // only if WASAPI asks for > 1 s
            int read = _source.Read(_scratch, 0, samples);
            var bytes = MemoryMarshal.AsBytes(_scratch.AsSpan(0, read));
            bytes.CopyTo(buffer.AsSpan(offset));
            return bytes.Length;
        }
    }
}

/// <summary>An output could not be started. Message is a user-facing hint in the language of the interface.</summary>
public sealed class SinkException(string message, Exception? inner = null) : Exception(message, inner);
