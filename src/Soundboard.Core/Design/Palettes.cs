namespace Soundboard.Core.Design;

/// <summary>
/// The fixed colours of the window: a quiet, slightly cool grey housing (neutrals tinted after
/// Sanzo Wada No. 221, Neutral Gray / Black). Light and dark are composed separately; dark is
/// derived (same tint, dark ground), not inverted. The only red is "Stop".
/// </summary>
public static class ThemeTokens
{
    public static IReadOnlyDictionary<string, string> Light { get; } = new Dictionary<string, string>
    {
        ["WindowBg"] = "#ECEFEF",
        ["BarBg"] = "#E0E5E6",
        ["FieldBg"] = "#F8FAFA",
        ["RaisedBg"] = "#F8FAFA",
        ["LineBrush"] = "#77828A",
        ["LineSoft"] = "#C3CBCE",
        ["TextBrush"] = "#111314",
        ["SubTextBrush"] = "#475155",
        ["ButtonBg"] = "#F8FAFA",
        ["ButtonHover"] = "#E3E8E9",
        ["ButtonPressed"] = "#D2D9DB",
        ["TrackBg"] = "#C3CBCE",
        ["StopBg"] = "#B3261E",
        ["StopHover"] = "#951F18",
        ["StopPressed"] = "#6E1712",
        ["OnStopBrush"] = "#FFFFFF",
        ["StopIcon"] = "#B3261E", // the red square on the quiet "Stop" while nothing plays
        ["OkText"] = "#1E6B34",
        ["DiffText"] = "#8A4B00", // amber: differences and the drawn warning sign
        ["MeterBrush"] = "#2E7D32",
        ["ScrollThumb"] = "#8E989C",
        ["HoverOverlay"] = "#14111314",
        ["PressedOverlay"] = "#26111314",
    };

    public static IReadOnlyDictionary<string, string> Dark { get; } = new Dictionary<string, string>
    {
        ["WindowBg"] = "#151819",
        ["BarBg"] = "#1C2022",
        ["FieldBg"] = "#22272A",
        ["RaisedBg"] = "#262B2E",
        ["LineBrush"] = "#6F7A7E",
        ["LineSoft"] = "#2F3639",
        ["TextBrush"] = "#E9ECEC",
        ["SubTextBrush"] = "#A9B3B6",
        ["ButtonBg"] = "#262B2E",
        ["ButtonHover"] = "#30363A",
        ["ButtonPressed"] = "#3A4145",
        ["TrackBg"] = "#3A4245",
        ["StopBg"] = "#CC372E",
        ["StopHover"] = "#B5302A",
        ["StopPressed"] = "#962620",
        ["OnStopBrush"] = "#FFFFFF",
        ["StopIcon"] = "#F07A70",
        ["OkText"] = "#6FCF8A",
        ["DiffText"] = "#F0B060", // amber: differences and the drawn warning sign
        ["MeterBrush"] = "#5FC46A",
        ["ScrollThumb"] = "#5D676B",
        ["HoverOverlay"] = "#1FE9ECEC",
        ["PressedOverlay"] = "#33E9ECEC",
    };

    public static IReadOnlyDictionary<string, string> For(bool dark) => dark ? Dark : Light;
}

/// <summary>An accent colour as used on screen: fill, hover, pressed, text on it, soft background.</summary>
public sealed record AccentColors(
    Rgb Fill,
    Rgb Hover,
    Rgb Pressed,
    Rgb OnFill,
    Rgb Soft,
    double FillOnGround,
    bool Adjusted);

/// <summary>One of the preset accent colours: plain, familiar hues. The name comes from the text files ("Accent.&lt;id&gt;").</summary>
public sealed record AccentPreset(string Id, string BaseHex)
{
    public string Name => Text.L.Get("Accent." + Id);
}

/// <summary>
/// Accent colours: presets plus a free colour. Every accent is derived per mode until the
/// contrast holds: as text and fill against the grounds ≥ 4.5 : 1, text on the fill ≥ 4.5 : 1.
/// </summary>
public static class AccentPalette
{
    public const string DefaultId = "teal";

    /// <summary>Stored value for the free colour (kept from the first versions, so old profiles stay valid).</summary>
    public const string CustomId = "eigene";

    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("blue", "#2F6BD0"),
        new("teal", "#00838A"),
        new("green", "#2E8B4A"),
        new("orange", "#E07020"),
        new("pink", "#C8336F"),
        new("violet", "#7046C8"),
    ];

    /// <summary>
    /// Presets of versions up to 0.10 (colours after Sanzo Wada) and the new preset closest to each:
    /// same hue family, the slate grey goes to blue.
    /// </summary>
    public static IReadOnlyDictionary<string, string> OldPresets { get; } = new Dictionary<string, string>
    {
        ["pfauenblau"] = "teal",
        ["olympiablau"] = "blue",
        ["blauviolett"] = "violet",
        ["purpur"] = "pink",
        ["orange"] = "orange",
        ["olivgelb"] = "green",
        ["gruen"] = "green",
        ["schiefer"] = "blue",
    };

    /// <summary>The id an old or unknown preset id stands for today; null if it is neither a preset nor an old one.</summary>
    public static string? Migrate(string? id) =>
        id is null ? null : Find(id) is not null ? id : OldPresets.TryGetValue(id, out var now) ? now : null;

    public static Rgb OnLight { get; } = Rgb.Parse("#FFFFFF");
    public static Rgb OnDark { get; } = Rgb.Parse("#111314");

    public static AccentPreset? Find(string? id) => Presets.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Resolves the configured accent: a preset id, or "eigene" with a hex colour. Anything
    /// unknown falls back to the default preset.
    /// </summary>
    public static AccentColors Resolve(string? id, string? customHex, bool dark)
    {
        if (id == CustomId && Rgb.TryParse(customHex, out var custom)) return Derive(custom, dark);
        var preset = Find(id) ?? Find(DefaultId)!;
        return Derive(Rgb.Parse(preset.BaseHex), dark);
    }

    /// <summary>
    /// Moves the lightness of <paramref name="baseColor"/> (hue fixed) until fill and text on the
    /// fill are readable in this mode. Adjusted = the colour had to change noticeably.
    /// </summary>
    public static AccentColors Derive(Rgb baseColor, bool dark)
    {
        var tokens = ThemeTokens.For(dark);
        var grounds = new[] { tokens["WindowBg"], tokens["BarBg"], tokens["FieldBg"], tokens["RaisedBg"] }.Select(Rgb.Parse).ToArray();
        var o = ColorMath.ToOklch(baseColor);
        var on = dark ? OnDark : OnLight;

        double c = dark ? Math.Max(0, o.C - 0.02) : o.C;
        double l = o.L;
        Rgb fill = ColorMath.FromOklch(new Oklch(l, c, o.H));
        bool Ok(Rgb x) => ColorMath.Contrast(x, on) >= 4.5 && grounds.All(g => ColorMath.Contrast(x, g) >= 4.5);
        for (int i = 0; i < 100 && !Ok(fill); i++)
        {
            l += dark ? 0.01 : -0.01;
            if (l is < 0 or > 1) break;
            fill = ColorMath.FromOklch(new Oklch(l, c, o.H));
        }

        double step = dark ? 0.05 : -0.05;
        var hover = ColorMath.FromOklch(new Oklch(Math.Clamp(l + step, 0, 1), c, o.H));
        var pressed = ColorMath.FromOklch(new Oklch(Math.Clamp(l + 2 * step, 0, 1), c, o.H));
        var soft = ColorMath.FromOklch(dark ? new Oklch(0.33, Math.Min(c, 0.06), o.H) : new Oklch(0.91, Math.Min(c, 0.05), o.H));
        double onGround = grounds.Min(g => ColorMath.Contrast(fill, g));
        return new AccentColors(fill, hover, pressed, on, soft, onGround, Math.Abs(l - o.L) > 0.03);
    }
}

/// <summary>Colours of one pad in one mode.</summary>
/// <param name="Edge">The thick lower edge of the pad, like a key cap: darker than the face in both modes.</param>
public sealed record PadColors(Rgb Face, Rgb Line, Rgb Text, Rgb CapBg, Rgb CapLine, Rgb Lit, Rgb LitLine, Rgb LitPlayed, Rgb LitText, Rgb Edge);

/// <summary>
/// Pad colours: seven calm, familiar hues (blue, orange, green, pink, teal, violet, grey). Idle the pad shows its colour dimmed,
/// playing it lights up in full (like an LED pad) with dark ink on it in both modes.
/// </summary>
public static class PadPalette
{
    public static IReadOnlyList<(string Name, string Hex)> Hues { get; } =
    [
        ("Blue", "#3A78D4"),
        ("Orange", "#E8822A"),
        ("Green", "#3D9A50"),
        ("Pink", "#D2457F"),
        ("Teal", "#16968F"),
        ("Violet", "#7D55D0"),
        ("Grey", "#7A8288"),
    ];

    /// <summary>Pad colours of versions up to 0.10 (after Sanzo Wada) and the new hue that replaces each.</summary>
    public static IReadOnlyDictionary<string, string> OldHues { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["#00939B"] = "#16968F", // Peacock Blue → teal
        ["#F99D1B"] = "#E8822A", // Orange
        ["#6450A1"] = "#7D55D0", // Blue Violet → violet
        ["#489B6E"] = "#3D9A50", // Green
        ["#BF5892"] = "#D2457F", // Eupatorium Purple → pink
        ["#5A82B3"] = "#3A78D4", // Olympic Blue → blue
        ["#A6A159"] = "#3D9A50", // Olive Yellow → green
        ["#70727C"] = "#7A8288", // Plumbeous → grey
    };

    /// <summary>The colour an old Wada pad colour becomes; any other colour stays as it is.</summary>
    public static string Migrate(string hex) =>
        Rgb.TryParse(hex, out var c) && OldHues.TryGetValue(c.ToHex(), out var now) ? now : hex;

    /// <summary>Colour stored for the n-th new pad.</summary>
    public static string ForIndex(int n) => Hues[((n % Hues.Count) + Hues.Count) % Hues.Count].Hex;

    /// <summary>Same colour, whatever the spelling ("#3a78d4", "3A78D4").</summary>
    public static bool SameHex(string? a, string? b) =>
        Rgb.TryParse(a, out var x) && Rgb.TryParse(b, out var y) && x == y;

    /// <summary>
    /// Colour of every pad in order: automatic pads take the colour of their place, chosen ones keep theirs.
    /// Returns the indices whose colour changed.
    /// </summary>
    public static List<int> Recolor(IList<Config.TileConfig> tiles)
    {
        var changed = new List<int>();
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i].ColorChosen) continue;
            var want = ForIndex(i);
            if (SameHex(tiles[i].Color, want)) continue;
            tiles[i].Color = want;
            changed.Add(i);
        }
        return changed;
    }

    public static PadColors Derive(Rgb color, bool dark)
    {
        var h = ColorMath.ToOklch(color).H;
        double chroma = Math.Max(ColorMath.ToOklch(color).C, 0.0);
        bool grey = chroma < 0.02;
        double Cx(double c) => grey ? Math.Min(c, 0.025) : c;
        Rgb At(double l, double c) => ColorMath.FromOklch(new Oklch(l, Cx(c), h));
        var ink = Rgb.Parse("#111314");
        return dark
            ? new PadColors(
                Face: At(0.285, 0.045),
                Line: At(0.60, 0.085),
                Text: Rgb.Parse("#E9ECEC"),
                CapBg: At(0.20, 0.03),
                CapLine: At(0.60, 0.085),
                Lit: At(0.82, 0.13),
                LitLine: At(0.92, 0.07),
                LitPlayed: At(0.73, 0.14),
                LitText: ink,
                Edge: At(0.215, 0.045))
            : new PadColors(
                Face: At(0.935, 0.032),
                Line: At(0.56, 0.09),
                Text: ink,
                CapBg: Rgb.Parse("#FBFCFC"),
                CapLine: At(0.56, 0.09),
                Lit: At(0.82, 0.13),
                LitLine: At(0.48, 0.12),
                LitPlayed: At(0.73, 0.14),
                LitText: ink,
                Edge: At(0.47, 0.10));
    }
}
