using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.Design;
using Soundboard.App.Themes;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

public enum TileState
{
    Loading,
    Ready,
    Error,
}

/// <summary>Brushes of one pad in the current mode (idle and lit).</summary>
public sealed class PadBrushes
{
    public PadBrushes(PadColors c)
    {
        Face = B(c.Face);
        Line = B(c.Line);
        Text = B(c.Text);
        CapBg = B(c.CapBg);
        CapLine = B(c.CapLine);
        Lit = B(c.Lit);
        LitLine = B(c.LitLine);
        LitPlayed = B(c.LitPlayed);
        LitText = B(c.LitText);
        GlowColor = ThemeManager.ToColor(c.Lit);
        var sweep = new SolidColorBrush(GlowColor) { Opacity = PadImageStyle.SweepAlpha };
        sweep.Freeze();
        LitSweep = sweep;
    }

    public Brush Face { get; }
    public Brush Line { get; }
    public Brush Text { get; }
    public Brush CapBg { get; }
    public Brush CapLine { get; }
    public Brush Lit { get; }
    public Brush LitLine { get; }
    public Brush LitPlayed { get; }
    public Brush LitText { get; }
    public Color GlowColor { get; }

    /// <summary>Played part over a picture: the pad colour, see-through.</summary>
    public Brush LitSweep { get; }

    private static SolidColorBrush B(Rgb c)
    {
        var b = new SolidColorBrush(ThemeManager.ToColor(c));
        b.Freeze();
        return b;
    }
}

/// <summary>One entry of the "Colour" submenu of a pad.</summary>
public sealed class PadColorChoice(string name, Brush? swatch, bool isChecked, System.Windows.Input.ICommand command)
{
    public string Name { get; } = name;

    /// <summary>The colour as it lights up in the current mode; null for "Automatic" and the hint line.</summary>
    public Brush? Swatch { get; } = swatch;

    public bool IsChecked { get; } = isChecked;

    public System.Windows.Input.ICommand Command { get; } = command;
}

/// <summary>One sound pad.</summary>
public sealed partial class TileViewModel : ObservableObject, IKeyTarget
{
    private readonly MainViewModel _owner;
    private Action? _issueAction;

    public TileViewModel(MainViewModel owner, TileConfig config)
    {
        _owner = owner;
        Config = config;
        Name = config.Name;
        VolumePercent = Math.Round(Math.Clamp(config.Volume, 0f, 1f) * 100);
        Pad = new PadBrushes(ThemeManager.PadColorsFor(config.Color));
        _compact = owner.CompactPads;
    }

    /// <summary>Hidden by the search above the pads.</summary>
    [ObservableProperty]
    private bool _isFilteredOut;

    /// <summary>Compact pad size: the name gets two lines instead of three.</summary>
    [ObservableProperty]
    private bool _compact;

    /// <summary>Just added: lit by a ring for a moment, so the eye finds it.</summary>
    [ObservableProperty]
    private bool _isJustAdded;

    public TileConfig Config { get; }

    /// <summary>Owner name of this pad's key in the hotkey service.</summary>
    public string OwnerId => Config.Id.ToString();

    /// <summary>Decoded sound, null while loading or on error.</summary>
    public SoundClip? Clip { get; set; }

    /// <summary>Increased on every load start; stale load results are ignored.</summary>
    public int LoadVersion { get; set; }

    /// <summary>When play was requested; bridges the few ms until the audio thread picks it up.</summary>
    public DateTime PlayRequestedAt { get; set; }

    public PadBrushes Pad { get; private set; }

    /// <summary>Short transitions allowed (setting + Windows animation effects).</summary>
    public bool AnimationsOn => ThemeManager.AnimationsOn;

    public void RefreshTheme()
    {
        Pad = new PadBrushes(ThemeManager.PadColorsFor(Config.Color));
        OnPropertyChanged(nameof(Pad));
        OnPropertyChanged(nameof(AnimationsOn));
        OnPropertyChanged(nameof(ColorMenu));
    }

    /// <summary>"Colour" submenu: automatic (by order), the seven pad colours, and in accent mode a line that says why nothing changes.</summary>
    public IReadOnlyList<PadColorChoice> ColorMenu
    {
        get
        {
            var list = new List<PadColorChoice>();
            if (ThemeManager.PadMode == PadColorModes.Accent)
                list.Add(new PadColorChoice(L.Get("Pad.ColourAccentMode"), null, false, new RelayCommand(_owner.ShowAppearance)));
            list.Add(new PadColorChoice(L.Get("Pad.ColourAuto"), null, !Config.ColorChosen, new RelayCommand(() => _owner.SetTileColor(this, null))));
            foreach (var (name, hex) in PadPalette.Hues)
            {
                var swatch = new SolidColorBrush(ThemeManager.ToColor(PadPalette.Derive(Rgb.Parse(hex), ThemeManager.IsDark).Lit));
                swatch.Freeze();
                list.Add(new PadColorChoice(L.Get("PadColour." + name), swatch, Config.ColorChosen && PadPalette.SameHex(Config.Color, hex),
                    new RelayCommand(() => _owner.SetTileColor(this, hex))));
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- key

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkey))]
    private string? _hotkeyText;

    /// <summary>The key for the cap on the pad, from full to shortened at the front ("…Shift+F12").</summary>
    [ObservableProperty]
    private IReadOnlyList<string>? _hotkeyVariants;

    [ObservableProperty]
    private bool _hotkeyProblem;

    [ObservableProperty]
    private bool _isCapturingKey;

    [ObservableProperty]
    private string _captureHint = "";

    /// <summary>State of the key in words, for the list in the settings ("works", "duplicate …").</summary>
    [ObservableProperty]
    private string _hotkeyStateText = "";

    public bool HasHotkey => HotkeyText is not null;

    // ---------------------------------------------------------------- problem shown on the pad

    /// <summary>Notice box on the pad: what does not work here and the way out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssueAction))]
    private string? _issueText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssueAction))]
    private string? _issueActionText;

    [ObservableProperty]
    private bool _isIssueOpen;

    public bool HasIssueAction => _issueAction is not null && !string.IsNullOrEmpty(IssueActionText);

    // The pad keeps two problems apart: the file (cannot play) goes first, then the key.
    // A missing or unreadable picture is no problem: the pad simply shows its colour.
    // The texts are produced on demand, so they follow a change of the interface language.
    private (Func<string>? Text, Func<string>? ActionText, Action? Action) _loadIssue;
    private (Func<string>? Text, Func<string>? ActionText, Action? Action) _keyIssue;

    /// <summary>Way out for a key problem (settings list, context menu); null without one.</summary>
    [ObservableProperty]
    private string? _keyIssueActionText;

    /// <summary>Problem of the sound file (cannot be played). Null clears it.</summary>
    public void SetIssue(Func<string>? text, Func<string>? actionText = null, Action? action = null)
    {
        _loadIssue = (text, actionText, action);
        ShowIssue();
    }

    /// <summary>Problem of the key (taken, refused). Null clears it.</summary>
    public void SetKeyIssue(Func<string>? text, Func<string>? actionText = null, Action? action = null)
    {
        _keyIssue = (text, actionText, action);
        ShowIssue();
    }

    private void ShowIssue()
    {
        var shown = _loadIssue.Text is not null ? _loadIssue : _keyIssue;
        _issueAction = shown.Action;
        KeyIssueActionText = _keyIssue.Text is null ? null : _keyIssue.ActionText?.Invoke();
        IssueActionText = shown.ActionText?.Invoke();
        IssueText = shown.Text?.Invoke();
        OnPropertyChanged(nameof(HasIssueAction));
        if (shown.Text is null) IsIssueOpen = false;
    }

    /// <summary>The language changed: every text of the pad again.</summary>
    public void RefreshTexts()
    {
        bool open = IsIssueOpen;
        ShowIssue();
        IsIssueOpen = open && IssueText is not null;
        OnPropertyChanged(nameof(VolumeText));
        OnPropertyChanged(nameof(HotkeyToolTip));
        OnPropertyChanged(nameof(ColorMenu));
        RefreshStatus();
    }

    [RelayCommand]
    private void RunIssueAction()
    {
        var action = _issueAction;
        IsIssueOpen = false;
        action?.Invoke();
    }

    [RelayCommand]
    private void RunKeyIssueAction()
    {
        var action = _keyIssue.Action;
        IsIssueOpen = false;
        action?.Invoke();
    }

    [RelayCommand]
    private void CloseIssue() => IsIssueOpen = false;

    /// <summary>Click on the printed key: shows the problem if there is one, otherwise waits for a new key.</summary>
    [RelayCommand]
    private void KeyCapClick()
    {
        if (HotkeyProblem && _keyIssue.Text is not null)
        {
            if (_loadIssue.Text is null) IsIssueOpen = !IsIssueOpen;
            else RunKeyIssueAction(); // the notice box shows the file problem; go straight to the key fix
        }
        else _owner.BeginKeyCapture(this);
    }

    // ---------------------------------------------------------------- sound

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText), nameof(ShowVolume))]
    private double _volumePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText), nameof(StatusText), nameof(IsError), nameof(IsLoading))]
    private TileState _state = TileState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private double _progress;

    [ObservableProperty]
    private bool _isRenaming;

    public bool IsError => State == TileState.Error;

    public bool IsLoading => State == TileState.Loading;

    // ---------------------------------------------------------------- picture

    /// <summary>The decoded picture (frozen), null without one or while it loads or when it is missing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    public bool HasImage => Image is not null;

    /// <summary>
    /// A picture is set in the profile (shown or not): "Remove picture" in the menu. A missing or
    /// unreadable file keeps the entry, so the picture comes back with the file.
    /// </summary>
    public bool HasImageConfigured => Config.Image is not null;

    /// <summary>A picture file is being dragged over this pad.</summary>
    [ObservableProperty]
    private bool _isImageDropTarget;

    /// <summary>Increased on every picture load; stale results are ignored.</summary>
    public int ImageLoadVersion { get; set; }

    public void RefreshImageConfigured() => OnPropertyChanged(nameof(HasImageConfigured));

    /// <summary>
    /// Length as printed on the pad ("3.5 s", "1:05 min"); while it plays the time left ("−2.1 s").
    /// Empty while loading or broken.
    /// </summary>
    public string DurationText => State != TileState.Ready || Clip is null ? ""
        : IsPlaying ? "−" + FormatDuration(Clip.Duration * Math.Clamp(1 - Progress, 0, 1))
        : FormatDuration(Clip.Duration);

    /// <summary>Second line on the pad when it cannot play.</summary>
    public string StatusText => State switch
    {
        TileState.Loading => L.Get("Pad.Loading"),
        TileState.Error => L.Get("Pad.CannotPlay"),
        _ => "",
    };

    public string VolumeText => L.Percent(VolumePercent / 100.0);

    /// <summary>Tooltip of the printed key.</summary>
    public string HotkeyToolTip => HotkeyProblem ? L.F("Pad.KeyBrokenTip", HotkeyText) : L.F("Pad.KeyTip", HotkeyText);

    /// <summary>Only printed on the pad when it is not 100 %.</summary>
    public bool ShowVolume => VolumePercent < 99.5;

    public float Volume => (float)(VolumePercent / 100.0);

    public string ToolTipText => State == TileState.Error && ErrorText is not null
        ? ErrorText
        : HotkeyText is null
            ? L.Get("Pad.Tip")
            : HotkeyProblem
                ? L.F("Pad.TipKeyBroken", HotkeyText)
                : L.F("Pad.TipKey", HotkeyText);

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ToolTipText));
    }

    /// <summary>Mouse wheel on the pad: ±5 %.</summary>
    public void NudgeVolume(int steps) => VolumePercent = Math.Clamp(Math.Round(VolumePercent / 5) * 5 + steps * 5, 0, 100);

    partial void OnNameChanged(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed == Config.Name) return;
        Config.Name = trimmed;
        _owner.ScheduleSave();
        _owner.RefreshSearch();
    }

    partial void OnVolumePercentChanged(double value) => _owner.OnTileVolumeChanged(this);

    partial void OnStateChanged(TileState value) => OnPropertyChanged(nameof(ToolTipText));

    partial void OnHotkeyTextChanged(string? value)
    {
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(HotkeyToolTip));
    }

    partial void OnHotkeyProblemChanged(bool value)
    {
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(HotkeyToolTip));
    }

    [RelayCommand]
    private void Click()
    {
        if (State == TileState.Error && IssueText is not null)
        {
            IsIssueOpen = true;
            return;
        }
        _owner.OnTileClicked(this);
    }

    [RelayCommand]
    private void Rename() => IsRenaming = true;

    [RelayCommand]
    private void AssignHotkey() => _owner.BeginKeyCapture(this);

    [RelayCommand]
    private void ClearHotkey() => _owner.SetTileHotkey(this, null);

    [RelayCommand]
    private void Remove() => _owner.RemoveTile(this);

    [RelayCommand]
    private void EvenOut() => _owner.EvenOut(this);

    [RelayCommand]
    private void EvenOutAll() => _owner.EvenOutAll();

    [RelayCommand]
    private void ShowInFolder() => _owner.ShowTileInFolder(this);

    [RelayCommand]
    private void ChooseImage() => _owner.ChooseImageDialog(this);

    [RelayCommand]
    private void RemoveImage() => _owner.RemoveImage(this);

    [RelayCommand]
    private void OpenImagesFolder() => _owner.ShowTileImageInFolder(this);

    public static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1
            ? L.F("Format.Minutes", (int)d.TotalMinutes, d.Seconds)
            : L.F("Format.Seconds", d.TotalSeconds);
}
