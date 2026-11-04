using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.App.Themes;

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
        Background = ThemeManager.TileBrush(config.Color);
    }

    public TileConfig Config { get; }

    /// <summary>Decoded sound, null while loading or on error.</summary>
    public SoundClip? Clip { get; set; }

    /// <summary>Increased on every load start; stale load results are ignored.</summary>
    public int LoadVersion { get; set; }

    /// <summary>When play was requested; bridges the few ms until the audio thread picks it up.</summary>
    public DateTime PlayRequestedAt { get; set; }

    public Brush Background { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkey), nameof(HotkeyLabel))]
    private string? _hotkeyText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyLabel))]
    private bool _hotkeyProblem;

    [ObservableProperty]
    private bool _isCapturingKey;

    [ObservableProperty]
    private string _captureHint = "";

    public bool HasHotkey => HotkeyText is not null;

    /// <summary>Key shown on the tile; a warning sign when the key does not work.</summary>
    public string HotkeyLabel => HotkeyText is null ? "" : HotkeyProblem ? "⚠ " + HotkeyText : HotkeyText;

    public void RefreshTheme()
    {
        Background = ThemeManager.TileBrush(Config.Color);
        OnPropertyChanged(nameof(Background));
    }

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
        : HotkeyText is null
            ? "Klick: abspielen, nochmal klicken: stoppen. Rechtsklick: Taste belegen, umbenennen, entfernen"
            : HotkeyProblem
                ? $"Die Taste {HotkeyText} funktioniert gerade nicht – der gelbe Hinweis oben sagt, warum. Klick: abspielen, nochmal klicken: stoppen."
                : $"Klick oder {HotkeyText}: abspielen, nochmal: stoppen. Rechtsklick: Taste ändern, umbenennen, entfernen";

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

    partial void OnHotkeyTextChanged(string? value) => OnPropertyChanged(nameof(ToolTipText));

    partial void OnHotkeyProblemChanged(bool value) => OnPropertyChanged(nameof(ToolTipText));

    [RelayCommand]
    private void Click() => _owner.OnTileClicked(this);

    [RelayCommand]
    private void Rename() => IsRenaming = true;

    [RelayCommand]
    private void AssignHotkey() => _owner.BeginKeyCapture(this);

    [RelayCommand]
    private void ClearHotkey() => _owner.SetTileHotkey(this, null);

    [RelayCommand]
    private void Remove() => _owner.RemoveTile(this);

    [RelayCommand]
    private void ShowInFolder() => _owner.ShowTileInFolder(this);

    private static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1
            ? $"{(int)d.TotalMinutes}:{d.Seconds:00} min"
            : d.TotalSeconds.ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " s";
}
