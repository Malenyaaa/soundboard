using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Soundboard.Core.Config;
using Soundboard.Core.Design;

namespace Soundboard.App.Themes;

/// <summary>
/// Light or dark (following Windows or fixed), the own accent colour and the pad colours.
/// All colours are written as brushes into the application resources; the controls in
/// Controls.xaml only use those keys. Windows' accent colour and the WPF theme are not used.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _controls;
    private static bool _following;
    private static bool? _forced;

    public static bool IsDark { get; private set; }

    /// <summary>"System", "Hell" or "Dunkel".</summary>
    public static string Appearance { get; private set; } = AppearanceModes.System;

    public static string AccentId { get; private set; } = AccentPalette.DefaultId;
    public static string? CustomAccent { get; private set; }
    public static string PadMode { get; private set; } = PadColorModes.Colorful;
    public static bool AnimationsSetting { get; private set; } = true;

    /// <summary>The accent as it is used in the current mode.</summary>
    public static AccentColors Accent { get; private set; } = AccentPalette.Resolve(AccentPalette.DefaultId, null, false);

    /// <summary>Animations wanted here and allowed by Windows ("animation effects").</summary>
    public static bool AnimationsOn => AnimationsSetting && SystemParameters.ClientAreaAnimation;

    public static event EventHandler? Changed;

    /// <summary>Installs the controls and applies the design values from the profile; keeps following Windows.</summary>
    public static void Start(Application app, Profile? profile)
    {
        if (profile is not null)
        {
            Appearance = profile.Appearance;
            AccentId = profile.AccentId;
            CustomAccent = profile.CustomAccent;
            PadMode = profile.PadColors;
            AnimationsSetting = profile.Animations;
        }
        Apply(app);
        if (_following) return;
        _following = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
            app.Dispatcher.BeginInvoke(() =>
            {
                if (_forced is null && Appearance == AppearanceModes.System && ReadWindowsDark() != IsDark) Apply(app);
                else Changed?.Invoke(null, EventArgs.Empty); // animation setting may have changed
            });
        };
    }

    /// <summary>Changes the design values (from the settings) and applies them at once.</summary>
    public static void Configure(Application app, string appearance, string accentId, string? customAccent, string padMode, bool animations)
    {
        Appearance = appearance;
        AccentId = accentId;
        CustomAccent = customAccent;
        PadMode = padMode;
        AnimationsSetting = animations;
        Apply(app);
    }

    /// <summary>Fixed mode (used to render both modes without looking at Windows).</summary>
    public static void Force(Application app, bool dark)
    {
        _forced = dark;
        Apply(app);
    }

    private static bool WantDark() =>
        _forced ?? Appearance switch
        {
            AppearanceModes.Dark => true,
            AppearanceModes.Light => false,
            _ => ReadWindowsDark(),
        };

    private static void Apply(Application app)
    {
        IsDark = WantDark();
        if (_controls is null)
        {
            _controls = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Soundboard;component/Themes/Controls.xaml"),
            };
            app.Resources.MergedDictionaries.Add(_controls);
        }

        foreach (var (key, hex) in ThemeTokens.For(IsDark)) Set(app, key, (Color)ColorConverter.ConvertFromString(hex));

        Accent = AccentPalette.Resolve(AccentId, CustomAccent, IsDark);
        Set(app, "AccentBrush", ToColor(Accent.Fill));
        Set(app, "AccentHover", ToColor(Accent.Hover));
        Set(app, "AccentPressed", ToColor(Accent.Pressed));
        Set(app, "OnAccentBrush", ToColor(Accent.OnFill));
        Set(app, "AccentSoft", ToColor(Accent.Soft));
        SetPadImageBrushes(app);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Brushes of pads with a picture. They lie on the picture, not on the window, so only the resting dim differs between light and dark.</summary>
    private static void SetPadImageBrushes(Application app)
    {
        var ink = ToColor(PadImageStyle.Ink);
        Color InkAt(double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), ink.R, ink.G, ink.B);
        Set(app, "PadImageText", ToColor(PadImageStyle.Text));

        Set(app, "PadImageDim", InkAt(PadImageStyle.IdleDimAlpha(IsDark)));
        Set(app, "PadImageTrack", Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        // One brush for the whole backdrop (no seam): soft over the first pixels, then solid ink down to the edge.
        var fade = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, PadImageStyle.FadeHeight),
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        fade.GradientStops.Add(new GradientStop(InkAt(0), 0));
        fade.GradientStops.Add(new GradientStop(InkAt(PadImageStyle.BackdropAlpha * 0.4), 0.5));
        fade.GradientStops.Add(new GradientStop(InkAt(PadImageStyle.BackdropAlpha), 1));
        fade.Freeze();
        app.Resources["PadImageBackdrop"] = fade;
        app.Resources["PadImageChip"] = Frozen(InkAt(PadImageStyle.BackdropAlpha));
        // The backdrop reaches from the pad edges up to FadeHeight above the text.
        app.Resources["PadImageBackdropMargin"] = new Thickness(-12, -PadImageStyle.FadeHeight, -12, -10);
        app.Resources["PadImageBarHeight"] = PadImageStyle.BarHeight;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static void Set(Application app, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        app.Resources[key] = b;
    }

    public static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>Colours of a pad in the current mode; "Akzent" paints every pad in the accent hue.</summary>
    public static PadColors PadColorsFor(string? hex)
    {
        Rgb source;
        if (PadMode == PadColorModes.Accent) source = AccentPalette.Resolve(AccentId, CustomAccent, false).Fill;
        else if (!Rgb.TryParse(hex, out source)) source = Rgb.Parse(PadPalette.Hues[0].Hex);
        return PadPalette.Derive(source, IsDark);
    }

    /// <summary>Reads HKCU\...\Themes\Personalize\AppsUseLightTheme (0 = dark). Default light.</summary>
    public static bool ReadWindowsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }
}
