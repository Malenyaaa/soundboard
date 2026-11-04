using Soundboard.Core.Config;
using Soundboard.Core.Design;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class DesignTests
{
    [TestMethod]
    public void Contrast_matches_the_wcag_reference_values()
    {
        Assert.AreEqual(21.0, ColorMath.Contrast("#000000", "#FFFFFF"), 0.01);
        Assert.AreEqual(1.0, ColorMath.Contrast("#777777", "#777777"), 0.0001);
        // #767676 on white is the classic 4.54 : 1
        Assert.AreEqual(4.54, ColorMath.Contrast("#767676", "#FFFFFF"), 0.01);
    }

    [TestMethod]
    public void Hex_parsing_accepts_short_and_long_forms_and_rejects_rubbish()
    {
        Assert.IsTrue(Rgb.TryParse("#0a8", out var a));
        Assert.AreEqual("#00AA88", a.ToHex());
        Assert.IsTrue(Rgb.TryParse(" 5A82B3 ", out var b));
        Assert.AreEqual("#5A82B3", b.ToHex());
        Assert.IsFalse(Rgb.TryParse("#12345", out _));
        Assert.IsFalse(Rgb.TryParse("blau", out _));
        Assert.IsFalse(Rgb.TryParse(null, out _));
    }

    [TestMethod]
    public void Oklch_round_trip_keeps_the_colour()
    {
        foreach (var hex in new[] { "#00939B", "#F99D1B", "#6450A1", "#111314", "#ECEFEF", "#BF5892" })
        {
            var c = Rgb.Parse(hex);
            var back = ColorMath.FromOklch(ColorMath.ToOklch(c));
            Assert.IsTrue(Math.Abs(back.R - c.R) <= 1 && Math.Abs(back.G - c.G) <= 1 && Math.Abs(back.B - c.B) <= 1, $"{hex} -> {back}");
        }
    }

    [TestMethod]
    public void Neutral_tokens_have_enough_contrast_in_both_modes()
    {
        foreach (bool dark in new[] { false, true })
        {
            var t = ThemeTokens.For(dark);
            foreach (var ground in new[] { "WindowBg", "BarBg", "FieldBg", "RaisedBg", "ButtonBg", "ButtonHover", "ButtonPressed" })
            {
                AssertAtLeast(4.5, t["TextBrush"], t[ground], $"text on {ground}, dark={dark}");
                AssertAtLeast(4.5, t["SubTextBrush"], t[ground], $"subtext on {ground}, dark={dark}");
            }
            foreach (var ground in new[] { "WindowBg", "BarBg", "FieldBg" })
                AssertAtLeast(3.0, t["LineBrush"], t[ground], $"control line on {ground}, dark={dark}");
            // A notice is a field like the others; only its drawn warning sign is amber.
            AssertAtLeast(3.0, t["DiffText"], t["FieldBg"], $"warning sign in a notice, dark={dark}");
            AssertAtLeast(3.0, t["LineBrush"], t["WindowBg"], $"notice frame against the window, dark={dark}");
            AssertAtLeast(4.5, t["OnStopBrush"], t["StopBg"], $"stop, dark={dark}");
            AssertAtLeast(4.5, t["OnStopBrush"], t["StopHover"], $"stop hover, dark={dark}");
            AssertAtLeast(3.0, t["StopBg"], t["BarBg"], $"stop button against bar, dark={dark}");
            AssertAtLeast(4.5, t["OkText"], t["FieldBg"], $"ok, dark={dark}");
            AssertAtLeast(4.5, t["DiffText"], t["FieldBg"], $"diff, dark={dark}");
            AssertAtLeast(3.0, t["MeterBrush"], t["TrackBg"], $"meter, dark={dark}");
            AssertAtLeast(3.0, t["DiffText"], t["BarBg"], $"warning sign in the status line, dark={dark}");
            AssertAtLeast(4.5, t["TextBrush"], t["BarBg"], $"status text, dark={dark}");
        }
    }

    [TestMethod]
    public void Every_preset_accent_is_readable_in_both_modes()
    {
        foreach (var p in AccentPalette.Presets)
            foreach (bool dark in new[] { false, true })
                AssertAccent(AccentPalette.Resolve(p.Id, null, dark), dark, p.Id);
    }

    [TestMethod]
    public void Too_light_custom_colour_is_darkened_and_flagged()
    {
        var pale = Rgb.Parse("#FFF2B3");
        var light = AccentPalette.Derive(pale, dark: false);
        Assert.IsTrue(light.Adjusted);
        AssertAccent(light, false, "pale");
        // the hue stays (yellow), only lightness moved
        Assert.AreEqual(ColorMath.ToOklch(pale).H, ColorMath.ToOklch(light.Fill).H, 12);
    }

    [TestMethod]
    public void Too_dark_custom_colour_is_lightened_for_dark_mode()
    {
        var deep = Rgb.Parse("#12354E");
        var dark = AccentPalette.Derive(deep, dark: true);
        Assert.IsTrue(dark.Adjusted);
        AssertAccent(dark, true, "deep");
    }

    [TestMethod]
    public void Fitting_custom_colour_is_left_alone()
    {
        var ok = Rgb.Parse("#1C4286"); // Deep Lyons Blue: already dark enough for white text
        var light = AccentPalette.Derive(ok, dark: false);
        Assert.IsFalse(light.Adjusted);
        Assert.AreEqual(ok, light.Fill);
    }

    [TestMethod]
    public void Unknown_accent_falls_back_to_the_default()
    {
        var fallback = AccentPalette.Resolve("gibt-es-nicht", null, false);
        var def = AccentPalette.Resolve(AccentPalette.DefaultId, null, false);
        Assert.AreEqual(def.Fill, fallback.Fill);
        var brokenCustom = AccentPalette.Resolve(AccentPalette.CustomId, "#zz", false);
        Assert.AreEqual(def.Fill, brokenCustom.Fill);
    }

    [TestMethod]
    public void Pad_colours_are_readable_idle_and_lit_in_both_modes()
    {
        var hexes = PadPalette.Hues.Select(h => h.Hex)
            .Concat(new[] { "#DCEBFF", "#FFE3C2", "#D9F2D9", "#F6D6E8", "#E6DDF7", "#FFF2B3", "#D5F0F0", "#F2DCCF" }); // old pastels
        foreach (var hex in hexes)
            foreach (bool dark in new[] { false, true })
            {
                var t = ThemeTokens.For(dark);
                var p = PadPalette.Derive(Rgb.Parse(hex), dark);
                AssertAtLeast(4.5, p.Text, p.Face, $"{hex} idle text dark={dark}");
                AssertAtLeast(4.5, p.Text, p.CapBg, $"{hex} cap text dark={dark}");
                AssertAtLeast(3.0, Rgb.Parse(t["DiffText"]), p.CapBg, $"{hex} warning sign on the cap dark={dark}");
                AssertAtLeast(3.0, p.Line, Rgb.Parse(t["WindowBg"]), $"{hex} pad edge dark={dark}");
                AssertAtLeast(4.5, p.LitText, p.Lit, $"{hex} lit text dark={dark}");
                AssertAtLeast(4.5, p.LitText, p.LitPlayed, $"{hex} played text dark={dark}");
                AssertAtLeast(3.0, p.LitLine, Rgb.Parse(t["WindowBg"]), $"{hex} lit edge dark={dark}");
            }
    }

    [TestMethod]
    public void New_pads_cycle_through_the_hues()
    {
        Assert.AreEqual(PadPalette.Hues[0].Hex, PadPalette.ForIndex(0));
        Assert.AreEqual(PadPalette.Hues[0].Hex, PadPalette.ForIndex(PadPalette.Hues.Count));
        Assert.AreEqual(PadPalette.Hues[3].Hex, PadPalette.ForIndex(3));
    }

    private static void AssertAccent(AccentColors a, bool dark, string what)
    {
        var t = ThemeTokens.For(dark);
        foreach (var ground in new[] { "WindowBg", "BarBg", "FieldBg", "RaisedBg" })
            AssertAtLeast(4.5, a.Fill, Rgb.Parse(t[ground]), $"{what} accent on {ground}, dark={dark}");
        AssertAtLeast(4.5, a.OnFill, a.Fill, $"{what} text on accent, dark={dark}");
        AssertAtLeast(4.5, a.OnFill, a.Hover, $"{what} text on hover, dark={dark}");
        AssertAtLeast(4.5, a.OnFill, a.Pressed, $"{what} text on pressed, dark={dark}");
        AssertAtLeast(4.5, Rgb.Parse(t["TextBrush"]), a.Soft, $"{what} text on soft, dark={dark}");
    }

    private static void AssertAtLeast(double min, string a, string b, string what) =>
        AssertAtLeast(min, Rgb.Parse(a), Rgb.Parse(b), what);

    private static void AssertAtLeast(double min, Rgb a, Rgb b, string what)
    {
        double r = ColorMath.Contrast(a, b);
        Assert.IsTrue(r >= min, $"{what}: {a} / {b} = {r:0.00} < {min}");
    }
}

[TestClass]
public sealed class ProfileMigrationTests
{
    [TestMethod]
    public void Version_1_profile_gets_design_defaults_and_keeps_everything_else()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """
            {
              "Version": 1,
              "MonitorDeviceId": "{0.0.0.00000000}.{x}",
              "MasterVolume": 0.5,
              "Overlap": "Overlay",
              "SetupSeen": true,
              "Tiles": [ { "Name": "Tusch", "FileName": "tusch.mp3", "Volume": 0.7, "Color": "#FFE3C2", "Hotkey": "NumPad1" } ]
            }
            """);

        var loaded = ProfileStore.Load(path);

        Assert.IsNull(loaded.Problem);
        var p = loaded.Profile;
        Assert.AreEqual(Profile.CurrentVersion, p.Version);
        Assert.AreEqual(AppearanceModes.System, p.Appearance);
        Assert.AreEqual(AccentPalette.DefaultId, p.AccentId);
        Assert.IsNull(p.CustomAccent);
        Assert.AreEqual(PadColorModes.Colorful, p.PadColors);
        Assert.IsTrue(p.Animations);
        Assert.AreEqual(0.5f, p.MasterVolume);
        Assert.IsTrue(p.SetupSeen);
        Assert.AreEqual("#FFE3C2", p.Tiles[0].Color, "old pad colours stay; the pad derives its look from the hue");
        Assert.AreEqual("NumPad1", p.Tiles[0].Hotkey);
    }

    [TestMethod]
    public void Unknown_or_broken_design_values_are_replaced_not_fatal()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """
            { "Version": 2, "Appearance": "Lila", "AccentId": "eigene", "CustomAccent": "kein-hex",
              "PadColors": null, "Tiles": [ { "Name": "A", "FileName": "a.wav", "Color": null } ] }
            """);

        var loaded = ProfileStore.Load(path);

        Assert.IsNull(loaded.Problem);
        var p = loaded.Profile;
        Assert.AreEqual(AppearanceModes.System, p.Appearance);
        Assert.AreEqual(AccentPalette.DefaultId, p.AccentId);
        Assert.AreEqual(PadColorModes.Colorful, p.PadColors);
        Assert.AreEqual(PadPalette.ForIndex(0), p.Tiles[0].Color);
    }

    [TestMethod]
    public void Design_values_survive_a_round_trip()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var profile = new Profile
        {
            Appearance = AppearanceModes.Dark,
            AccentId = AccentPalette.CustomId,
            CustomAccent = "#12354E",
            PadColors = PadColorModes.Accent,
            Animations = false,
        };
        ProfileStore.Save(path, profile);

        var p = ProfileStore.Load(path).Profile;

        Assert.AreEqual(AppearanceModes.Dark, p.Appearance);
        Assert.AreEqual(AccentPalette.CustomId, p.AccentId);
        Assert.AreEqual("#12354E", p.CustomAccent);
        Assert.AreEqual(PadColorModes.Accent, p.PadColors);
        Assert.IsFalse(p.Animations);
    }
}
