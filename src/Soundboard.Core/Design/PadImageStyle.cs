namespace Soundboard.Core.Design;

/// <summary>
/// How a pad with a picture is drawn: the picture fills the pad, a dark ink gradient at the
/// bottom carries name, volume and status, the length sits on a small ink chip. The values are
/// chosen for the worst case, a pure white picture: white text on ink over white still has
/// at least 4.5 : 1. The same values are used by the app, so the tests check what is shown.
/// </summary>
public static class PadImageStyle
{
    /// <summary>Ink of the gradient and the chips (the text colour of the light mode).</summary>
    public static Rgb Ink { get; } = Rgb.Parse("#111314");

    /// <summary>Text on a picture.</summary>
    public static Rgb Text { get; } = Rgb.Parse("#FFFFFF");

    /// <summary>Opacity of the ink under the text (the solid part of the gradient).</summary>
    public const double BackdropAlpha = 0.68;

    /// <summary>Height of the soft part of the gradient above the text, in pixels.</summary>
    public const double FadeHeight = 40;

    /// <summary>
    /// Ink over the whole picture while the pad rests; playing shows it at full brightness. Dimmer
    /// in the dark mode, so a bright picture does not glare out of the dark window.
    /// </summary>
    public static double IdleDimAlpha(bool dark) => dark ? 0.32 : 0.18;

    /// <summary>Opacity of the played part (pad colour) sweeping over the picture.</summary>
    public const double SweepAlpha = 0.38;

    /// <summary>Height of the progress bar at the bottom edge, in pixels.</summary>
    public const double BarHeight = 4;

    /// <summary>What the ink makes of a picture pixel under it.</summary>
    public static Rgb Over(Rgb picture, double alpha) => ColorMath.Mix(picture, Ink, alpha);

    /// <summary>Lightest the backdrop under the text can get: ink over a white picture, pad playing (not dimmed).</summary>
    public static Rgb WorstCaseBackdrop => Over(Rgb.Parse("#FFFFFF"), BackdropAlpha);

    /// <summary>Darkest the backdrop can get: ink over a black picture.</summary>
    public static Rgb DarkestBackdrop => Over(Rgb.Parse("#000000"), BackdropAlpha);

    /// <summary>Contrast of the text against the backdrop in the worst case.</summary>
    public static double TextContrastWorstCase => ColorMath.Contrast(Text, WorstCaseBackdrop);
}
