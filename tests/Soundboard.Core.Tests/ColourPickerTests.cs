using Soundboard.Core.Config;
using Soundboard.Core.Design;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class ColourPickerTests
{
    [TestMethod]
    public void Hsv_of_the_primaries_and_greys()
    {
        Assert.AreEqual(new Rgb(255, 0, 0), ColorMath.FromHsv(new Hsv(0, 1, 1)));
        Assert.AreEqual(new Rgb(0, 255, 0), ColorMath.FromHsv(new Hsv(120, 1, 1)));
        Assert.AreEqual(new Rgb(0, 0, 255), ColorMath.FromHsv(new Hsv(240, 1, 1)));
        Assert.AreEqual(new Rgb(255, 255, 0), ColorMath.FromHsv(new Hsv(60, 1, 1)));
        Assert.AreEqual(new Rgb(128, 128, 128), ColorMath.FromHsv(new Hsv(200, 0, 128 / 255.0)));
        Assert.AreEqual(new Rgb(0, 0, 0), ColorMath.FromHsv(new Hsv(300, 0.7, 0)));

        var grey = ColorMath.ToHsv(new Rgb(90, 90, 90));
        Assert.AreEqual(0, grey.S, 1e-9);
        Assert.AreEqual(90 / 255.0, grey.V, 1e-9);
        var cyan = ColorMath.ToHsv(new Rgb(0, 255, 255));
        Assert.AreEqual(180, cyan.H, 1e-9);
    }

    [TestMethod]
    public void Hue_wraps_and_values_are_clamped()
    {
        Assert.AreEqual(ColorMath.FromHsv(new Hsv(10, 0.8, 0.6)), ColorMath.FromHsv(new Hsv(370, 0.8, 0.6)));
        Assert.AreEqual(ColorMath.FromHsv(new Hsv(330, 0.8, 0.6)), ColorMath.FromHsv(new Hsv(-30, 0.8, 0.6)));
        Assert.AreEqual(new Rgb(255, 0, 0), ColorMath.FromHsv(new Hsv(0, 2, 5)));
        Assert.AreEqual(new Rgb(0, 0, 0), ColorMath.FromHsv(new Hsv(0, -1, -1)));
    }

    [TestMethod]
    public void Rgb_to_hsv_and_back_is_exact_for_every_step()
    {
        for (int r = 0; r < 256; r += 17)
            for (int g = 0; g < 256; g += 17)
                for (int b = 0; b < 256; b += 17)
                {
                    var c = new Rgb((byte)r, (byte)g, (byte)b);
                    Assert.AreEqual(c, ColorMath.FromHsv(ColorMath.ToHsv(c)), c.ToHex());
                }
    }

    [TestMethod]
    public void Any_colour_from_the_picker_ends_up_readable()
    {
        // corners and edges of the picker field for every 30° of hue
        for (int h = 0; h < 360; h += 30)
            foreach (var (s, v) in new[] { (0.0, 0.0), (0.0, 1.0), (1.0, 1.0), (1.0, 0.0), (0.5, 0.5), (0.15, 0.95), (1.0, 0.3) })
            {
                var c = ColorMath.FromHsv(new Hsv(h, s, v));
                foreach (bool dark in new[] { false, true })
                {
                    var a = AccentPalette.Derive(c, dark);
                    var t = ThemeTokens.For(dark);
                    foreach (var ground in new[] { "WindowBg", "BarBg", "FieldBg", "RaisedBg" })
                        Assert.IsTrue(ColorMath.Contrast(a.Fill, Rgb.Parse(t[ground])) >= 4.5, $"{c} on {ground} dark={dark}: {a.Fill}");
                    Assert.IsTrue(ColorMath.Contrast(a.OnFill, a.Fill) >= 4.5, $"{c} text on fill dark={dark}");
                }
            }
    }

    [TestMethod]
    public void Pale_picker_colour_is_adjusted_and_keeps_its_hue()
    {
        var pale = ColorMath.FromHsv(new Hsv(210, 0.25, 1)); // a washed-out sky blue
        var light = AccentPalette.Derive(pale, dark: false);
        Assert.IsTrue(light.Adjusted);
        Assert.AreEqual(ColorMath.ToOklch(pale).H, ColorMath.ToOklch(light.Fill).H, 12);
    }
}

[TestClass]
public sealed class ColourMigrationTests
{
    [TestMethod]
    public void Few_plain_presets_all_readable()
    {
        Assert.IsTrue(AccentPalette.Presets.Count is >= 5 and <= 6);
        CollectionAssert.AllItemsAreUnique(AccentPalette.Presets.Select(p => p.Id).ToList());
        Assert.IsNotNull(AccentPalette.Find(AccentPalette.DefaultId));
    }

    [TestMethod]
    public void Every_old_preset_moves_to_a_new_one()
    {
        foreach (var (old, now) in AccentPalette.OldPresets)
        {
            Assert.IsNotNull(AccentPalette.Find(now), $"{old} → {now} is no preset");
            Assert.AreEqual(now, AccentPalette.Migrate(old));
        }
        Assert.AreEqual("blue", AccentPalette.Migrate("blue"), "a new id stays");
        Assert.IsNull(AccentPalette.Migrate("gibt-es-nicht"));
    }

    [TestMethod]
    public void Every_old_pad_colour_moves_to_a_new_hue_and_others_stay()
    {
        var hues = PadPalette.Hues.Select(h => h.Hex).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (old, now) in PadPalette.OldHues)
        {
            Assert.IsTrue(hues.Contains(now), $"{old} → {now} is no pad hue");
            Assert.AreEqual(now, PadPalette.Migrate(old));
            Assert.AreEqual(now, PadPalette.Migrate(old.ToLowerInvariant()), "case does not matter");
        }
        Assert.AreEqual("#FFE3C2", PadPalette.Migrate("#FFE3C2"), "own colours stay");
        Assert.AreEqual("kein-hex", PadPalette.Migrate("kein-hex"));
    }

    [TestMethod]
    public void Version_3_profile_gets_the_new_colours_and_keeps_German()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """
            {
              "Version": 3,
              "AccentId": "olympiablau",
              "PadColors": "Bunt",
              "Tiles": [
                { "Name": "A", "FileName": "a.wav", "Color": "#5A82B3" },
                { "Name": "B", "FileName": "b.wav", "Color": "#a6a159" },
                { "Name": "C", "FileName": "c.wav", "Color": "#FFE3C2" }
              ]
            }
            """);

        var p = ProfileStore.Load(path).Profile;

        Assert.AreEqual(Profile.CurrentVersion, p.Version);
        Assert.AreEqual("blue", p.AccentId);
        Assert.AreEqual("#3A78D4", p.Tiles[0].Color);
        Assert.AreEqual("#3D9A50", p.Tiles[1].Color);
        Assert.AreEqual("#FFE3C2", p.Tiles[2].Color);
        Assert.AreEqual(Text.L.German, p.Language);
    }

    [TestMethod]
    public void Custom_accent_survives_the_migration()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "Version": 3, "AccentId": "eigene", "CustomAccent": "#12354E", "Tiles": [] }""");
        var p = ProfileStore.Load(path).Profile;
        Assert.AreEqual(AccentPalette.CustomId, p.AccentId);
        Assert.AreEqual("#12354E", p.CustomAccent);
    }

    [TestMethod]
    public void Version_4_profile_with_an_old_accent_id_is_still_mapped()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "Version": 4, "Language": "en", "AccentId": "purpur", "Tiles": [ { "Name": "A", "FileName": "a.wav", "Color": "#00939B" } ] }""");
        var p = ProfileStore.Load(path).Profile;
        Assert.AreEqual("pink", p.AccentId);
        Assert.AreEqual("#00939B", p.Tiles[0].Color, "pad colours are only moved once, from version 3");
        Assert.AreEqual(Text.L.English, p.Language);
    }
}
