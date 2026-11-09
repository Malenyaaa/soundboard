namespace Soundboard.Core.Design;

/// <summary>
/// The fixed colours of the window: a quiet, slightly cool grey housing (neutrals tinted after
/// Sanzo Wada No. 221, Neutral Gray / Black). Light and dark are composed separately; dark is
/// derived (same tint, dark ground), not inverted.
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
        ["OkText"] = "#6FCF8A",
        ["DiffText"] = "#F0B060", // amber: differences and the drawn warning sign
        ["MeterBrush"] = "#5FC46A",
        ["ScrollThumb"] = "#5D676B",
        ["HoverOverlay"] = "#1FE9ECEC",
        ["PressedOverlay"] = "#33E9ECEC",
    };

    public static IReadOnlyDictionary<string, string> For(bool dark) => dark ? Dark : Light;
}

/// <summary>
/// An accent colour as used on screen. <see cref="Fill"/> is exactly the chosen colour (buttons, switches,
/// sliders); <see cref="Line"/> is the same hue made readable on the window ground (text, rings, borders).
/// </summary>
public sealed record AccentColors(
    Rgb Fill,
    Rgb Hover,
    Rgb Pressed,
    Rgb OnFill,
    Rgb Soft,
    Rgb Line,
    double LineOnGround,
    bool Adjusted);

/// <summary>One of the preset accent colours: plain, familiar hues. The name comes from the text files ("Accent.&lt;id&gt;").</summary>
public sealed record AccentPreset(string Id, string BaseHex)
{
    public string Name => Text.L.Get("Accent." + Id);
}

/// <summary>
/// Accent colours: presets plus a free colour. Filled parts take the colour exactly as chosen, with
/// white or dark text on it, whichever reads better. Only where the accent itself is text or a line on
/// the window ground is its lightness moved (hue and chroma kept) until it reaches 4.5 : 1.
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
    /// The accent in this mode: the fill stays exactly <paramref name="baseColor"/>; the line colour (text,
    /// rings, borders on the ground) moves its lightness until it is readable. Adjusted = the line colour
    /// differs noticeably from the chosen colour.
    /// </summary>
    public static AccentColors Derive(Rgb baseColor, bool dark)
    {
        var tokens = ThemeTokens.For(dark);
        var grounds = new[] { tokens["WindowBg"], tokens["BarBg"], tokens["FieldBg"], tokens["RaisedBg"] }.Select(Rgb.Parse).ToArray();
        var o = ColorMath.ToOklch(baseColor);

        // Fill: the chosen colour as it is, text on it white or dark, whichever has more contrast.
        var fill = baseColor;
        var on = ColorMath.Contrast(fill, OnLight) >= ColorMath.Contrast(fill, OnDark) ? OnLight : OnDark;
        // Hover and pressed move away from the text colour (more contrast); at the very ends of the
        // scale they go the other way, so black and white still show a change.
        double dir = on == OnLight ? -1 : 1, first = 0.05;
        if (o.L + 2 * first * dir is < 0.02 or > 0.98)
        {
            dir = -dir;
            first = 0.2; // near black a small step stays invisible
        }
        var hover = ColorMath.FromOklch(new Oklch(Math.Clamp(o.L + first * dir, 0, 1), o.C, o.H));
        var pressed = ColorMath.FromOklch(new Oklch(Math.Clamp(o.L + (first + 0.05) * dir, 0, 1), o.C, o.H));

        // Line: lightness moved (hue and chroma kept) until it reads on every ground.
        double l = o.L;
        Rgb line = baseColor;
        bool Ok(Rgb x) => grounds.All(g => ColorMath.Contrast(x, g) >= 4.5);
        for (int i = 0; i < 100 && !Ok(line); i++)
        {
            l += dark ? 0.01 : -0.01;
            if (l is < 0 or > 1) break;
            line = ColorMath.FromOklch(new Oklch(l, o.C, o.H));
        }

        var soft = ColorMath.FromOklch(dark ? new Oklch(0.33, Math.Min(o.C, 0.06), o.H) : new Oklch(0.91, Math.Min(o.C, 0.05), o.H));
        double onGround = grounds.Min(g => ColorMath.Contrast(line, g));
        return new AccentColors(fill, hover, pressed, on, soft, line, onGround, Math.Abs(l - o.L) > 0.03);
    }
}

/// <summary>Colours of one pad in one mode.</summary>
/// <param name="Edge">The thick lower edge of the pad, like a key cap: darker than the face in both modes.</param>
public sealed record PadColors(Rgb Face, Rgb Line, Rgb Text, Rgb CapBg, Rgb CapLine, Rgb Lit, Rgb LitLine, Rgb LitPlayed, Rgb LitText, Rgb Edge);

/// <summary>
/// Pad colours: twelve strong hues around the colour wheel. Idle the pad shows its colour (full, not
/// washed out), playing it lights up brighter (like an LED pad). The text on a pad is dark or light,
/// whichever reads better on that colour. New pads get a random hue that differs from the pad before.
/// </summary>
public static class PadPalette
{
    /// <summary>Name (text key "PadColour.&lt;name&gt;") and OKLCH hue of the twelve pad colours, in the order of the colour wheel.</summary>
    private static readonly (string Name, double Hue)[] Wheel =
    [
        ("Red", 25), ("Orange", 50), ("Amber", 75), ("Lime", 115), ("Green", 145), ("Teal", 180),
        ("Cyan", 215), ("Blue", 255), ("Indigo", 280), ("Violet", 305), ("Magenta", 335), ("Pink", 0),
    ];

    /// <summary>The twelve pad colours as stored (#RRGGBB); only their hue counts, the shades come from <see cref="Derive"/>.</summary>
    public static IReadOnlyList<(string Name, string Hex)> Hues { get; } =
        Wheel.Select(w => (w.Name, ColorMath.FromOklch(new Oklch(0.65, 0.15, w.Hue)).ToHex())).ToList();

    /// <summary>The seven pad colours of versions 0.11 to 0.17, in their old order (needed to read old profiles).</summary>
    public static IReadOnlyList<string> LegacyHues { get; } =
        ["#3A78D4", "#E8822A", "#3D9A50", "#D2457F", "#16968F", "#7D55D0", "#7A8288"];

    /// <summary>Colour an automatic pad had at place <paramref name="n"/> up to version 0.17.</summary>
    public static string LegacyForIndex(int n) => LegacyHues[((n % LegacyHues.Count) + LegacyHues.Count) % LegacyHues.Count];

    /// <summary>Pad colours of versions up to 0.10 (after Sanzo Wada) and the 0.11 colour that replaced each.</summary>
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

    /// <summary>Same colour, whatever the spelling ("#3a78d4", "3A78D4").</summary>
    public static bool SameHex(string? a, string? b) =>
        Rgb.TryParse(a, out var x) && Rgb.TryParse(b, out var y) && x == y;

    /// <summary>Index of the pad colour whose hue is nearest to <paramref name="hex"/>; -1 for a grey or no colour.</summary>
    public static int Nearest(string? hex)
    {
        if (!Rgb.TryParse(hex, out var c)) return -1;
        var o = ColorMath.ToOklch(c);
        if (o.C < 0.03) return -1;
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < Wheel.Length; i++)
        {
            double d = Math.Abs(((o.H - Wheel[i].Hue) % 360 + 540) % 360 - 180);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>How many of the last pads of a group a new random colour tries not to repeat.</summary>
    public const int AvoidRecent = 6;

    /// <summary>
    /// A random pad colour for a new pad (or "Random" in the menu). Never the hue of a pad in
    /// <paramref name="neighbours"/> (the pad before and after it) nor the hues next to it on the wheel;
    /// if possible none of <paramref name="recent"/> either (the last pads of its group).
    /// </summary>
    public static string Random(Random rng, IEnumerable<string?> neighbours, IEnumerable<string?> recent)
    {
        var banned = new HashSet<int>();
        foreach (var n in neighbours)
        {
            int i = Nearest(n);
            if (i < 0) continue;
            banned.Add(i);
            banned.Add((i + 1) % Wheel.Length);
            banned.Add((i + Wheel.Length - 1) % Wheel.Length);
        }
        var soft = recent.Select(Nearest).Where(i => i >= 0).ToHashSet();
        var allowed = Enumerable.Range(0, Wheel.Length).Where(i => !banned.Contains(i)).ToList();
        if (allowed.Count == 0) allowed = Enumerable.Range(0, Wheel.Length).ToList();
        var fresh = allowed.Where(i => !soft.Contains(i)).ToList();
        var from = fresh.Count > 0 ? fresh : allowed;
        return Hues[from[rng.Next(from.Count)]].Hex;
    }

    private static readonly Rgb Ink = Rgb.Parse("#111314");
    private static readonly Rgb Paper = Rgb.Parse("#F4F6F6");

    /// <summary>Dark or light text, whichever has more contrast on all of <paramref name="grounds"/>.</summary>
    public static Rgb ReadableOn(params Rgb[] grounds) =>
        grounds.Min(g => ColorMath.Contrast(Ink, g)) >= grounds.Min(g => ColorMath.Contrast(Paper, g)) ? Ink : Paper;

    public static PadColors Derive(Rgb color, bool dark)
    {
        var o = ColorMath.ToOklch(color);
        double h = o.H;
        bool grey = o.C < 0.02;
        double Cx(double c) => grey ? Math.Min(c, 0.025) : c;
        Rgb At(double l, double c) => ColorMath.FromOklch(new Oklch(l, Cx(c), h));
        if (dark)
        {
            var face = At(0.42, 0.105);
            var cap = At(0.22, 0.04);
            var lit = At(0.83, 0.15);
            var played = At(0.74, 0.16);
            return new PadColors(
                Face: face,
                Line: At(0.64, 0.12),
                Text: ReadableOn(face, cap),
                CapBg: cap,
                CapLine: At(0.64, 0.12),
                Lit: lit,
                LitLine: At(0.93, 0.07),
                LitPlayed: played,
                LitText: ReadableOn(lit, played),
                Edge: At(0.31, 0.08));
        }
        else
        {
            var face = At(0.85, 0.11);
            var cap = Rgb.Parse("#FBFCFC");
            var lit = At(0.80, 0.17);
            var played = At(0.71, 0.17);
            return new PadColors(
                Face: face,
                Line: At(0.55, 0.13),
                Text: ReadableOn(face, cap),
                CapBg: cap,
                CapLine: At(0.55, 0.13),
                Lit: lit,
                LitLine: At(0.47, 0.13),
                LitPlayed: played,
                LitText: ReadableOn(lit, played),
                Edge: At(0.47, 0.12));
        }
    }
}
