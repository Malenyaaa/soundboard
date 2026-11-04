using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.App.Themes;
using Soundboard.Core.Config;
using Soundboard.Core.Design;

namespace Soundboard.App.ViewModels;

/// <summary>One colour chip in the "Design" tab.</summary>
public sealed partial class AccentChoice : ObservableObject
{
    private readonly MainViewModel _owner;

    public AccentChoice(MainViewModel owner, string id, string name)
    {
        _owner = owner;
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }

    public bool IsCustom => Id == AccentPalette.CustomId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHash))]
    private Brush _swatch = Brushes.Transparent;

    /// <summary>Free colour without a valid value yet: the chip shows a "#".</summary>
    public bool ShowHash => IsCustom && ReferenceEquals(Swatch, Brushes.Transparent);

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _owner.SelectAccent(Id);
    }
}

/// <summary>Design tab: appearance, accent colour, pad colours, animations.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Status line: the cable should send but does not.</summary>
    [ObservableProperty]
    private bool _cableWarn;

    public ObservableCollection<AccentChoice> AccentChoices { get; } = [];

    private bool _syncingDesign;

    /// <summary>"System", "Hell" or "Dunkel".</summary>
    [ObservableProperty]
    private string _appearance = AppearanceModes.System;

    /// <summary>"Bunt" or "Akzent".</summary>
    [ObservableProperty]
    private string _padColorMode = PadColorModes.Colorful;

    [ObservableProperty]
    private bool _animations = true;

    /// <summary>Hex text of the free colour (as typed).</summary>
    [ObservableProperty]
    private string _customAccentText = "";

    /// <summary>Yellow hint under the free colour: not a colour, or it had to be adjusted. Empty = fine.</summary>
    [ObservableProperty]
    private string _customAccentHint = "";

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
            CustomAccentText = _profile.CustomAccent ?? "";
            foreach (var p in AccentPalette.Presets) AccentChoices.Add(new AccentChoice(this, p.Id, p.Name));
            AccentChoices.Add(new AccentChoice(this, AccentPalette.CustomId, "Eigene"));
            foreach (var c in AccentChoices) c.IsSelected = c.Id == _profile.AccentId;
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
                ? Rgb.TryParse(CustomAccentText, out var raw) ? ToBrush(AccentPalette.Derive(raw, dark).Fill) : Brushes.Transparent
                : ToBrush(AccentPalette.Resolve(c.Id, null, dark).Fill);
        }
        UpdateCustomAccentHint();
    }

    private static Brush ToBrush(Rgb c)
    {
        var b = new SolidColorBrush(ThemeManager.ToColor(c));
        b.Freeze();
        return b;
    }

    public void SelectAccent(string id)
    {
        foreach (var c in AccentChoices)
            if (c.Id != id && c.IsSelected) c.IsSelected = false;
        if (_syncingDesign) return;
        if (id == AccentPalette.CustomId && !Rgb.TryParse(CustomAccentText, out _))
        {
            // Nothing usable typed yet: the old colour stays, the hint says what is missing.
            UpdateCustomAccentHint();
            return;
        }
        _profile.AccentId = id;
        if (id == AccentPalette.CustomId) _profile.CustomAccent = Rgb.Parse(CustomAccentText).ToHex();
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

    partial void OnCustomAccentTextChanged(string value)
    {
        if (_syncingDesign) return;
        if (Rgb.TryParse(value, out var c))
        {
            _profile.CustomAccent = c.ToHex();
            var custom = AccentChoices.First(a => a.IsCustom);
            if (!custom.IsSelected) custom.IsSelected = true; // selects and applies
            else
            {
                _profile.AccentId = AccentPalette.CustomId;
                ApplyDesign();
            }
        }
        else
        {
            UpdateCustomAccentHint();
        }
    }

    private void UpdateCustomAccentHint()
    {
        bool customSelected = AccentChoices.FirstOrDefault(a => a.IsCustom)?.IsSelected == true;
        if (!Rgb.TryParse(CustomAccentText, out var raw))
        {
            CustomAccentHint = customSelected
                ? string.IsNullOrWhiteSpace(CustomAccentText)
                    ? "Gib eine Farbe als Hex-Wert ein, z. B. #2E6E4C. Bis dahin bleibt die bisherige Akzentfarbe."
                    : $"„{CustomAccentText.Trim()}“ ist kein Hex-Wert. So sieht einer aus: #2E6E4C (Raute, dann sechs Zeichen aus 0–9 und A–F)."
                : "";
            return;
        }
        if (!customSelected)
        {
            CustomAccentHint = "";
            return;
        }
        var light = AccentPalette.Derive(raw, dark: false);
        var dark = AccentPalette.Derive(raw, dark: true);
        var parts = new List<string>();
        if (light.Adjusted) parts.Add($"im hellen Modus zu {light.Fill.ToHex()} abgedunkelt");
        if (dark.Adjusted) parts.Add($"im dunklen Modus zu {dark.Fill.ToHex()} aufgehellt");
        CustomAccentHint = parts.Count == 0
            ? ""
            : $"{raw.ToHex()} ist für Schrift und Knöpfe zu schwach. Damit alles lesbar bleibt (Kontrast mindestens 4,5 : 1), wird die Farbe {string.Join(" und ", parts)}.";
    }

    private void ApplyDesign()
    {
        ScheduleSave();
        if (Application.Current is { } app)
            ThemeManager.Configure(app, _profile.Appearance, _profile.AccentId, _profile.CustomAccent, _profile.PadColors, _profile.Animations);
    }
}
