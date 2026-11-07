namespace Soundboard.Core.Audio;

/// <summary>
/// The same sounds go to two places with independent clocks: the monitor (headphones) and
/// the mix (cable or test file). Each place gets its own <see cref="SoundPlayer"/> over the
/// same decoded clips; commands go to every player whose output is running. Call from the UI
/// thread only.
/// </summary>
public sealed class PlayerGroup
{
    private float _master = 1f;
    private OverlapMode _overlap = OverlapMode.StopPrevious;

    public PlayerGroup(TargetFormat format)
    {
        Format = format;
        Monitor = new SoundPlayer(format);
        Mix = new SoundPlayer(format);
    }

    public TargetFormat Format { get; }

    /// <summary>Player read by the monitor output.</summary>
    public SoundPlayer Monitor { get; }

    /// <summary>Player read by the mixer (cable/file).</summary>
    public SoundPlayer Mix { get; }

    public bool MonitorActive { get; private set; }

    public bool MixActive { get; private set; }

    public bool AnyActive => MonitorActive || MixActive;

    public float MasterVolume
    {
        get => _master;
        set
        {
            _master = value;
            Monitor.MasterVolume = value;
            Mix.MasterVolume = value;
        }
    }

    public OverlapMode Overlap
    {
        get => _overlap;
        set
        {
            _overlap = value;
            Monitor.Overlap = value;
            Mix.Overlap = value;
        }
    }

    /// <summary>Marks the monitor output as running or stopped. Clears its player either way.</summary>
    public void SetMonitorActive(bool active)
    {
        Monitor.Reset();
        MonitorActive = active;
    }

    /// <summary>Marks the mix output as running or stopped. Clears its player either way.</summary>
    public void SetMixActive(bool active)
    {
        Mix.Reset();
        MixActive = active;
    }

    /// <summary>Starts a clip on every running output, from <paramref name="startAt"/> (0..1 of its length).</summary>
    public void Play(SoundClip clip, float volume, double startAt = 0)
    {
        if (MonitorActive) Monitor.Play(clip, volume, startAt);
        if (MixActive) Mix.Play(clip, volume, startAt);
    }

    /// <summary>Jumps a playing clip to <paramref name="position"/> (0..1) on every running output.</summary>
    public void Seek(int clipHandle, double position)
    {
        if (MonitorActive) Monitor.Seek(clipHandle, position);
        if (MixActive) Mix.Seek(clipHandle, position);
    }

    public void Stop(int clipHandle)
    {
        if (MonitorActive) Monitor.Stop(clipHandle);
        if (MixActive) Mix.Stop(clipHandle);
    }

    public void StopAll()
    {
        if (MonitorActive) Monitor.StopAll();
        if (MixActive) Mix.StopAll();
    }

    public void SetVolume(int clipHandle, float volume)
    {
        if (MonitorActive) Monitor.SetVolume(clipHandle, volume);
        if (MixActive) Mix.SetVolume(clipHandle, volume);
    }

    /// <summary>
    /// Plays <paramref name="clip"/> on the monitor only (listening while cutting): the others in the
    /// call do not hear it. False when the monitor is not running.
    /// </summary>
    public bool Preview(SoundClip clip, float volume)
    {
        if (!MonitorActive) return false;
        Monitor.Play(clip, volume);
        return true;
    }

    /// <summary>Stops a preview started with <see cref="Preview"/>.</summary>
    public void StopPreview(int clipHandle)
    {
        if (MonitorActive) Monitor.Stop(clipHandle);
    }

    /// <summary>Progress of a preview (monitor only), null when it does not play.</summary>
    public double? GetPreviewProgress(int clipHandle) => MonitorActive ? Monitor.GetProgress(clipHandle) : null;

    /// <summary>Progress from the monitor if it runs, else from the mix.</summary>
    public double? GetProgress(int clipHandle) =>
        MonitorActive ? Monitor.GetProgress(clipHandle)
        : MixActive ? Mix.GetProgress(clipHandle)
        : null;
}
