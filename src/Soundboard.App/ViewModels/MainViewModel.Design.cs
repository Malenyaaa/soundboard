using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Soundboard.App.Themes;
using Soundboard.Core.Config;
using Soundboard.Core.Design;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>One colour chip in the "Appearance" tab.</summary>
public sealed partial class AccentChoice : ObservableObject
{
    private readonly MainViewModel _owner;

    public AccentChoice(MainViewModel owner, string id)
    {
        _owner = owner;
        Id = id;
    }

    public string Id { get; }

    public string Name => L.Get(IsCustom ? "Accent.custom" : "Accent." + Id);

    public bool IsCustom => Id == AccentPalette.CustomId;

    [ObservableProperty]
    private Brush _swatch = Brushes.Transparent;

    [ObservableProperty]
    private bool _isSelected;

    public void RefreshText() => OnPropertyChanged(nameof(Name));

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _owner.SelectAccent(Id);
    }
}

/// <summary>Appearance tab: language, light/dark, accent colour (presets or a free colour from the picker), pad colours, animations.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Status line: the cable should send but does not.</summary>
    [ObservableProperty]
    private bool _cableWarn;

    public ObservableCollection<AccentChoice> AccentChoices { get; } = [];

    private bool _syncingDesign;
    private DispatcherTimer? _designTimer;

    /// <summary>"System", "Hell" or "Dunkel".</summary>
    [ObservableProperty]
    private string _appearance = AppearanceModes.System;

    /// <summary>"Bunt" or "Akzent".</summary>
    [ObservableProperty]
    private string _padColorMode = PadColorModes.Colorful;

    [ObservableProperty]
    private bool _animations = true;

    /// <summary>The free colour as chosen in the picker (before any adjustment for contrast).</summary>
    [ObservableProperty]
    private Color _customAccentColor = Color.FromRgb(0x2F, 0x6B, 0xD0);

    /// <summary>Hex text of the free colour (small extra field next to the picker).</summary>
    [ObservableProperty]
    private string _customAccentText = "";

    /// <summary>Quiet line under the picker: how the colour is used if it had to be adjusted, or why the typed value is no colour. Empty = fine.</summary>
    [ObservableProperty]
    private string _customAccentHint = "";

    /// <summary>The free colour is the chosen accent: the picker is shown.</summary>
    [ObservableProperty]
    private bool _customAccentSelected;

    /// <summary>Windows has "animation effects" switched off: said next to the switch.</summary>
    public bool WindowsAnimationsOff => !SystemParameters.ClientAreaAnimation;

    private void InitDesign()
    {
        _syncingDesign = true;
        try
        {
            Appearance = _profile.Appearance;
            PadColorMode = _profile.PadColors;
            Animations = _profile.Animations;
            if (Rgb.TryParse(_profile.CustomAccent, out var custom)) SetCustomFields(custom);
            foreach (var p in AccentPalette.Presets) AccentChoices.Add(new AccentChoice(this, p.Id));
            AccentChoices.Add(new AccentChoice(this, AccentPalette.CustomId));
            foreach (var c in AccentChoices) c.IsSelected = c.Id == _profile.AccentId;
            CustomAccentSelected = _profile.AccentId == AccentPalette.CustomId;
        }
        finally
        {
            _syncingDesign = false;
        }
        RefreshDesignViews();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        foreach (var t in Tiles) t.RefreshTheme();
        RefreshDesignViews();
        OnPropertyChanged(nameof(WindowsAnimationsOff));
    }

    private void RefreshDesignViews()
    {
        bool dark = ThemeManager.IsDark;
        foreach (var c in AccentChoices)
        {
            c.Swatch = c.IsCustom
                ? Rgb.TryParse(_profile.CustomAccent, out _) ? ToBrush(AccentPalette.Derive(ToRgb(CustomAccentColor), dark).Fill) : HueBrush
                : ToBrush(AccentPalette.Resolve(c.Id, null, dark).Fill);
        }
        UpdateCustomAccentHint();
    }

    /// <summary>The custom chip before a colour was picked: all hues, so it does not look like one of the presets.</summary>
    private static readonly Brush HueBrush = MakeHueBrush();

    private static Brush MakeHueBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i <= 6; i++)
        {
            var c = ColorMath.FromHsv(new Hsv(i * 60, 0.65, 0.85));
            b.GradientStops.Add(new GradientStop(ThemeManager.ToColor(c), i / 6.0));
        }
        b.Freeze();
        return b;
    }

    private static Brush ToBrush(Rgb c)
    {
        var b = new SolidColorBrush(ThemeManager.ToColor(c));
        b.Freeze();
        return b;
    }

    private static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);

    public void SelectAccent(string id)
    {
        foreach (var c in AccentChoices)
            if (c.Id != id && c.IsSelected) c.IsSelected = false;
        CustomAccentSelected = id == AccentPalette.CustomId;
        if (_syncingDesign) return;
        if (id == AccentPalette.CustomId && !Rgb.TryParse(_profile.CustomAccent, out _))
        {
            // First time: the picker starts at the accent in use, so nothing jumps.
            var start = ThemeManager.Accent.Fill;
            _syncingDesign = true;
            try { SetCustomFields(start); }
            finally { _syncingDesign = false; }
            _profile.CustomAccent = start.ToHex();
        }
        _profile.AccentId = id;
        ApplyDesign();
    }

    partial void OnAppearanceChanged(string value)
    {
        if (_syncingDesign || value == _profile.Appearance) return;
        _profile.Appearance = value;
        ApplyDesign();
    }

    partial void OnPadColorModeChanged(string value)
    {
        if (_syncingDesign || value == _profile.PadColors) return;
        _profile.PadColors = value;
        ApplyDesign();
    }

    partial void OnAnimationsChanged(bool value)
    {
        if (_syncingDesign || value == _profile.Animations) return;
        _profile.Animations = value;
        ApplyDesign();
    }

    /// <summary>The picker moved: take the colour at once (applied a few times a second while dragging).</summary>
    partial void OnCustomAccentColorChanged(Color value)
    {
        if (_syncingDesign) return;
        var rgb = ToRgb(value);
        _syncingDesign = true;
        try { CustomAccentText = rgb.ToHex(); }
        finally { _syncingDesign = false; }
        TakeCustom(rgb);
    }

    /// <summary>A hex value was typed into the small field.</summary>
    partial void OnCustomAccentTextChanged(string value)
    {
        if (_syncingDesign) return;
        // While typing only a complete six-digit value counts; "#2E6" on its way to "#2E6E4C" must not
        // recolour the window. The short form (#RGB) is taken when the field is left (CommitCustomAccentText).
        if (HexDigits(value).Length == 6 && Rgb.TryParse(value, out var c))
        {
            _syncingDesign = true;
            try { CustomAccentColor = ThemeManager.ToColor(c); }
            finally { _syncingDesign = false; }
            TakeCustom(c);
        }
        else
        {
            UpdateCustomAccentHint();
        }
    }

    private static string HexDigits(string? text) => (text ?? "").Trim().TrimStart('#');

    /// <summary>The hex field was left or Enter pressed: a short value (#RGB) is taken now; anything unusable gets the quiet hint.</summary>
    public void CommitCustomAccentText()
    {
        if (Rgb.TryParse(CustomAccentText, out var c) && c != ToRgb(CustomAccentColor))
        {
            _syncingDesign = true;
            try
            {
                CustomAccentColor = ThemeManager.ToColor(c);
                CustomAccentText = c.ToHex();
            }
            finally { _syncingDesign = false; }
            TakeCustom(c);
            return;
        }
        _hexCommitted = true;
        UpdateCustomAccentHint();
    }

    private bool _hexCommitted;

    private void SetCustomFields(Rgb c)
    {
        CustomAccentColor = ThemeManager.ToColor(c);
        CustomAccentText = c.ToHex();
    }

    private void TakeCustom(Rgb c)
    {
        _profile.CustomAccent = c.ToHex();
        _profile.AccentId = AccentPalette.CustomId;
        var custom = AccentChoices.First(a => a.IsCustom);
        if (!custom.IsSelected)
        {
            _syncingDesign = true;
            try { custom.IsSelected = true; }
            finally { _syncingDesign = false; }
        }
        CustomAccentSelected = true;
        UpdateCustomAccentHint();
        ApplyDesignSoon();
    }

    private void UpdateCustomAccentHint()
    {
        if (!CustomAccentSelected)
        {
            CustomAccentHint = "";
            return;
        }
        var digits = HexDigits(CustomAccentText);
        bool unusable = digits.Length > 6 || digits.Any(ch => !Uri.IsHexDigit(ch)) || (_hexCommitted && digits.Length is not (3 or 6) && digits.Length > 0);
        _hexCommitted = false;
        if (unusable)
        {
            CustomAccentHint = L.F("Design.HexInvalid", CustomAccentText.Trim());
            return;
        }
        var raw = ToRgb(CustomAccentColor);
        var light = AccentPalette.Derive(raw, dark: false);
        var dark = AccentPalette.Derive(raw, dark: true);
        CustomAccentHint = (light.Adjusted, dark.Adjusted) switch
        {
            (true, true) => L.F("Design.AdjustedBoth", light.Fill.ToHex(), dark.Fill.ToHex()),
            (true, false) => L.F("Design.AdjustedLight", light.Fill.ToHex()),
            (false, true) => L.F("Design.AdjustedDark", dark.Fill.ToHex()),
            _ => "",
        };
    }

    /// <summary>While the picker is dragged, the whole window is recoloured at most every 60 ms.</summary>
    private void ApplyDesignSoon()
    {
        ScheduleSave();
        if (_designTimer is null)
        {
            _designTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _designTimer.Tick += (_, _) =>
            {
                _designTimer.Stop();
                ApplyDesign();
            };
        }
        if (!_designTimer.IsEnabled) _designTimer.Start();
    }

    private void ApplyDesign()
    {
        ScheduleSave();
        if (Application.Current is { } app)
            ThemeManager.Configure(app, _profile.Appearance, _profile.AccentId, _profile.CustomAccent, _profile.PadColors, _profile.Animations);
        else
            RefreshDesignViews();
    }
}
