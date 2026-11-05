using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Cutting a pad (right-click → "Trim…"): start and end are stored with the pad, the file stays as
/// it is. Listening while cutting goes to the monitor only, so the others in the call do not hear it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The window opens the trim editor for this pad (it owns the window).</summary>
    public event Action<TileViewModel>? TrimRequested;

    private SoundClip? _preview;

    public void RequestTrim(TileViewModel tile)
    {
        if (tile.FullClip is null)
        {
            ShowNotice("play", TF(tile.State == TileState.Error ? "Notice.TrimBroken" : "Notice.StillLoading", tile.Name));
            return;
        }
        TrimRequested?.Invoke(tile);
    }

    /// <summary>Sets the cut (seconds into the file; null = not cut there) and remembers the step for Ctrl+Z.</summary>
    public void SetTileTrim(TileViewModel tile, double? start, double? end) => SetTileTrim(tile, start, end, remember: true);

    private void SetTileTrim(TileViewModel tile, double? start, double? end, bool remember)
    {
        if (tile.FullClip is not { } full) return;
        var (s, e) = SoundTrim.Normalize(start, end, full.Duration.TotalSeconds);
        double? oldStart = tile.Config.TrimStart, oldEnd = tile.Config.TrimEnd;
        if (Nullable.Equals(s, oldStart) && Nullable.Equals(e, oldEnd)) return;

        if (tile.Clip is { } playing) _player.Stop(playing.Handle);
        tile.Config.TrimStart = s;
        tile.Config.TrimEnd = e;
        var clip = SoundTrim.Apply(full, s, e);
        tile.Clip = clip;
        _player.SetVolume(clip.Handle, tile.Volume);
        tile.RefreshStatus();
        ScheduleSave();
        if (remember)
            PushUndo(() => L.F("Notice.Trimmed", tile.Name), () => SetTileTrim(tile, oldStart, oldEnd, remember: false));
    }

    /// <summary>
    /// Plays the part from <paramref name="start"/> to <paramref name="end"/> (seconds) on the monitor
    /// only. Returns the handle to follow and stop it, or null when nothing can be heard (monitor off).
    /// </summary>
    public int? PreviewTrim(TileViewModel tile, double start, double end)
    {
        StopTrimPreview();
        if (tile.FullClip is not { } full) return null;
        var clip = SoundTrim.Apply(full, start, end);
        // A part that is the whole file is the bank's clip: a copy keeps the pad's own playback apart.
        if (ReferenceEquals(clip, full)) clip = new SoundClip(full.Samples, full.Format);
        if (!_player.Preview(clip, tile.Volume)) return null;
        _preview = clip;
        return clip.Handle;
    }

    /// <summary>Progress 0..1 of the running preview; null when it has ended or was stopped.</summary>
    public double? TrimPreviewProgress() => _preview is { } p ? _player.GetPreviewProgress(p.Handle) : null;

    public void StopTrimPreview()
    {
        if (_preview is { } p) _player.StopPreview(p.Handle);
        _preview = null;
    }

    /// <summary>Whether listening is possible now (the monitor runs).</summary>
    public bool CanPreview => _player.MonitorActive;
}
