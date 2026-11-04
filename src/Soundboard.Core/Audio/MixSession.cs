namespace Soundboard.Core.Audio;

/// <summary>
/// One running mix path: microphone (optional) + sounds → <see cref="Mixer"/> → output
/// (cable or test file). Owns the capture and the output.
/// </summary>
public sealed class MixSession : IDisposable
{
    private readonly ICaptureInput? _capture;
    private readonly RingBufferSampleProvider? _voice;
    private bool _disposed;

    public MixSession(IAudioOutput output, ICaptureInput? capture, SoundPlayer sounds, BufferSize buffer)
    {
        Output = output;
        _capture = capture;
        if (capture is not null)
            _voice = new RingBufferSampleProvider(capture.Buffer, capture.Format, BufferSizes.Milliseconds(buffer));
        Mixer = new Mixer(output.SourceFormat, _voice, sounds, () => sounds.ActiveVoiceCount > 0);
        output.Stopped += OnOutputStopped;
        if (capture is not null) capture.Stopped += OnCaptureStopped;
    }

    public IAudioOutput Output { get; }

    public Mixer Mixer { get; }

    public bool HasVoice => _capture is not null;

    public string? VoiceDeviceId => _capture?.DeviceId;

    /// <summary>Times the microphone could not deliver in time (drop-outs).</summary>
    public long Underruns => _voice?.Underruns ?? 0;

    /// <summary>Output or microphone stopped on its own. Argument: which one and why.</summary>
    public event EventHandler<MixStoppedEventArgs>? Stopped;

    public void Start()
    {
        _capture?.Start();
        Output.Start(Mixer);
    }

    private void OnOutputStopped(object? sender, Exception? e)
    {
        if (!_disposed) Stopped?.Invoke(this, new MixStoppedEventArgs(false, e));
    }

    private void OnCaptureStopped(object? sender, Exception? e)
    {
        if (!_disposed) Stopped?.Invoke(this, new MixStoppedEventArgs(true, e));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Output.Stopped -= OnOutputStopped;
        Output.Dispose();
        if (_capture is not null)
        {
            _capture.Stopped -= OnCaptureStopped;
            _capture.Dispose();
        }
    }
}

public sealed record MixStoppedEventArgs(bool Microphone, Exception? Error);
