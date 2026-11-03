using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;

namespace Soundboard.App.ViewModels;

public enum TileState
{
    Loading,
    Ready,
    Error,
}

/// <summary>One sound tile.</summary>
public sealed partial class TileViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public TileViewModel(MainViewModel owner, TileConfig config)
    {
        _owner = owner;
        Config = config;
        Name = config.Name;
        VolumePercent = Math.Round(Math.Clamp(config.Volume, 0f, 1f) * 100);
        Background = ToBrush(config.Color);
    }

    public TileConfig Config { get; }

    /// <summary>Decoded sound, null while loading or on error.</summary>
    public SoundClip? Clip { get; set; }

    /// <summary>Increased on every load start; stale load results are ignored.</summary>
    public int LoadVersion { get; set; }

    /// <summary>When play was requested; bridges the few ms until the audio thread picks it up.</summary>
    public DateTime PlayRequestedAt { get; set; }

    public Brush Background { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private double _volumePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsError), nameof(IsLoading))]
    private TileState _state = TileState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayGlyph))]
    private bool _isPlaying;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isRenaming;

    public bool IsError => State == TileState.Error;

    public bool IsLoading => State == TileState.Loading;

    public string PlayGlyph => IsPlaying ? "■" : "▶";

    public string StatusText => State switch
    {
        TileState.Loading => "wird geladen …",
        TileState.Error => "⚠ nicht abspielbar",
        _ => Clip is null ? "" : FormatDuration(Clip.Duration),
    };

    public float Volume => (float)(VolumePercent / 100.0);

    public string ToolTipText => State == TileState.Error && ErrorText is not null
        ? ErrorText
        : "Klick: abspielen · nochmal klicken: stoppen · Rechtsklick: umbenennen, entfernen";

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToolTipText));
    }

    partial void OnNameChanged(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed == Config.Name) return;
        Config.Name = trimmed;
        _owner.ScheduleSave();
    }

    partial void OnVolumePercentChanged(double value) => _owner.OnTileVolumeChanged(this);

    partial void OnStateChanged(TileState value) => OnPropertyChanged(nameof(ToolTipText));

    [RelayCommand]
    private void Click() => _owner.OnTileClicked(this);

    [RelayCommand]
    private void Rename() => IsRenaming = true;

    [RelayCommand]
    private void Remove() => _owner.RemoveTile(this);

    [RelayCommand]
    private void ShowInFolder() => _owner.ShowTileInFolder(this);

    private static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1
            ? $"{(int)d.TotalMinutes}:{d.Seconds:00} min"
            : d.TotalSeconds.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " s";

    private static Brush ToBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Brushes.LightSteelBlue;
        }
    }
}
