using CommunityToolkit.Mvvm.ComponentModel;
using Soundboard.Core.Audio;

namespace Soundboard.App.ViewModels;

/// <summary>
/// The playback bar at the bottom: the sound started last, its waveform, the time played and the whole
/// length. A click or a drag on the bar jumps within the sound (and starts it there when it no longer plays).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The pad started last (also after it ended); null before the first sound and after its pad was removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNowTile))]
    private TileViewModel? _nowTile;

    public bool HasNowTile => NowTile is not null;

    /// <summary>The clip shown in the bar (the trimmed one, as it plays); null without one.</summary>
    [ObservableProperty]
    private SoundClip? _nowClip;

    /// <summary>Position 0..1 in the bar.</summary>
    [ObservableProperty]
    private double _nowProgress;

    [ObservableProperty]
    private bool _nowPlaying;

    /// <summary>Played time, "m:ss".</summary>
    [ObservableProperty]
    private string _nowElapsedText = Clock(TimeSpan.Zero);

    /// <summary>Whole length, "m:ss".</summary>
    [ObservableProperty]
    private string _nowTotalText = Clock(TimeSpan.Zero);

    /// <summary>"m:ss" (minutes not padded), the way a player shows it.</summary>
    public static string Clock(TimeSpan t)
    {
        int s = (int)Math.Floor(Math.Max(0, t.TotalSeconds));
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The bar follows the pad started last; called when a pad starts.</summary>
    private void SetNowTile(TileViewModel tile)
    {
        NowTile = tile;
        UpdateNowView();
    }

    /// <summary>Called with every progress tick and when the pads change.</summary>
    private void UpdateNowView()
    {
        if (NowTile is { } t && !Tiles.Contains(t)) NowTile = null;
        var tile = NowTile;
        var clip = tile?.Clip;
        if (!ReferenceEquals(clip, NowClip)) NowClip = clip;
        bool playing = tile is { IsPlaying: true };
        if (playing != NowPlaying) NowPlaying = playing;
        double progress = playing ? tile!.Progress : 0; // after the end the bar stands at the start again
        if (Math.Abs(progress - NowProgress) > 1e-9) NowProgress = progress;
        var length = clip?.Duration ?? TimeSpan.Zero;
        string elapsed = Clock(length * progress), total = Clock(length);
        if (elapsed != NowElapsedText) NowElapsedText = elapsed;
        if (total != NowTotalText) NowTotalText = total;
    }

    /// <summary>
    /// Click or drag on the bar: jumps to <paramref name="position"/> (0..1). A playing sound continues there;
    /// one that ended starts again there, like a click on its pad would (same notices if nothing can play).
    /// </summary>
    public void SeekNow(double position)
    {
        if (NowTile is not { Clip: { } clip } tile || tile.State != TileState.Ready) return;
        position = Math.Clamp(position, 0, 1);
        if (tile.IsPlaying)
        {
            _player.Seek(clip.Handle, position);
            tile.Progress = position;
            tile.PlayRequestedAt = DateTime.UtcNow; // the audio thread takes the jump with its next block
        }
        else
        {
            StartTile(tile, position);
        }
        UpdateNowView();
    }
}
