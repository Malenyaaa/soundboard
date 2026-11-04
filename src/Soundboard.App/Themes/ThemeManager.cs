using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Soundboard.App.Themes;

/// <summary>
/// Light or dark, following the Windows setting "app mode" (only read). Swaps the colour
/// dictionary and the WPF control theme; raises <see cref="Changed"/> for code-made colours.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;
    private static bool? _forced;

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    /// <summary>Applies the Windows setting and keeps following it.</summary>
    public static void FollowWindows(Application app)
    {
        Apply(app, ReadWindowsDark());
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
            if (_forced is not null) return;
            app.Dispatcher.BeginInvoke(() =>
            {
                bool dark = ReadWindowsDark();
                if (dark != IsDark) Apply(app, dark);
            });
        };
    }

    /// <summary>Fixed mode (used to render both modes without looking at Windows).</summary>
    public static void Force(Application app, bool dark)
    {
        _forced = dark;
        Apply(app, dark);
    }

    private static void Apply(Application app, bool dark)
    {
        IsDark = dark;
#pragma warning disable WPF0001 // ThemeMode is marked experimental; it only styles the standard controls.
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Soundboard;component/Themes/{(dark ? "Dark" : "Light")}.xaml"),
        };
        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
        Changed?.Invoke(null, EventArgs.Empty);
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

    /// <summary>
    /// Tile colour for the current mode. Light: the stored pastel. Dark: same hue, low
    /// lightness, less saturation (derived, not inverted), so light text stays readable.
    /// </summary>
    public static Brush TileBrush(string hex)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { c = Colors.LightSteelBlue; }
        if (IsDark) c = DarkTile(c);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    internal static Color DarkTile(Color c)
    {
        RgbToHsl(c, out double h, out double s, out _);
        return HslToRgb(h, Math.Min(0.38, s * 0.45), 0.22);
    }

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max - min < 1e-9) { h = 0; s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6;
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromRgb(Ch(p, q, h + 1.0 / 3), Ch(p, q, h), Ch(p, q, h - 1.0 / 3));

        static byte Ch(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                : t < 0.5 ? q
                : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                : p;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }
}
