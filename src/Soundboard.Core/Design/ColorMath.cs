using System.Globalization;

namespace Soundboard.Core.Design;

/// <summary>An sRGB colour with 8 bits per channel.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Parses "#RRGGBB", "RRGGBB" or "#RGB". Returns false for anything else.</summary>
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6) return false;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Keine Farbe: {text}");

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();
}

/// <summary>Colour in OKLCH: lightness 0..1, chroma, hue in degrees.</summary>
public readonly record struct Oklch(double L, double C, double H);

/// <summary>
/// Colour arithmetic for the theme: WCAG contrast and OKLCH, so lightness can be moved
/// without the hue wandering (dark mode is derived, not inverted).
/// </summary>
public static class ColorMath
{
    /// <summary>WCAG relative luminance 0..1.</summary>
    public static double Luminance(Rgb c) =>
        0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    /// <summary>WCAG contrast ratio 1..21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double Contrast(string a, string b) => Contrast(Rgb.Parse(a), Rgb.Parse(b));

    public static Oklch ToOklch(Rgb c)
    {
        double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double C = Math.Sqrt(A * A + B * B);
        double H = Math.Atan2(B, A) * 180 / Math.PI;
        if (H < 0) H += 360;
        return new Oklch(L, C, H);
    }

    /// <summary>OKLCH to sRGB. Outside the sRGB gamut the chroma is reduced until it fits (hue and lightness stay).</summary>
    public static Rgb FromOklch(Oklch c)
    {
        double lo = 0, hi = Math.Max(0, c.C);
        if (TryToRgb(c.L, hi, c.H, out var direct)) return direct;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) / 2;
            if (TryToRgb(c.L, mid, c.H, out _)) lo = mid;
            else hi = mid;
        }
        TryToRgb(c.L, lo, c.H, out var fit);
        return fit;
    }

    private static bool TryToRgb(double L, double C, double H, out Rgb rgb)
    {
        double hr = H * Math.PI / 180;
        double a = C * Math.Cos(hr), b = C * Math.Sin(hr);
        double l = L + 0.3963377774 * a + 0.2158037573 * b;
        double m = L - 0.1055613458 * a - 0.0638541728 * b;
        double s = L - 0.0894841775 * a - 1.2914855480 * b;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        const double eps = 0.0005;
        bool inside = r >= -eps && r <= 1 + eps && g >= -eps && g <= 1 + eps && bl >= -eps && bl <= 1 + eps;
        rgb = new Rgb(Enc(r), Enc(g), Enc(bl));
        return inside;
    }

    /// <summary>Same hue and chroma, other lightness.</summary>
    public static Rgb WithLightness(Rgb c, double lightness, double? chroma = null)
    {
        var o = ToOklch(c);
        return FromOklch(new Oklch(Math.Clamp(lightness, 0, 1), chroma ?? o.C, o.H));
    }

    /// <summary>Mixes <paramref name="a"/> towards <paramref name="b"/> (t = 0 → a, 1 → b) in sRGB.</summary>
    public static Rgb Mix(Rgb a, Rgb b, double t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static double Lin(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Enc(double lin)
    {
        lin = Math.Clamp(lin, 0, 1);
        double c = lin <= 0.0031308 ? 12.92 * lin : 1.055 * Math.Pow(lin, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(c, 0, 1) * 255);
    }
}
