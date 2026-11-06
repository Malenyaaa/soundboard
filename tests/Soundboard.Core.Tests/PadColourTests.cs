using System.Text.Json;
using Soundboard.Core.Config;
using Soundboard.Core.Design;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class PadColourTests
{
    private static Profile Load(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "config.json");
            File.WriteAllText(file, json);
            return ProfileStore.Load(file).Profile;
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Version_4_profile_keeps_chosen_colours_and_rolls_the_automatic_ones()
    {
        // Pad 1 was removed earlier, so pad "c" no longer has the colour of its place.
        var p = Load("""
            { "Version": 4, "Language": "en", "Tiles": [
              { "Name": "a", "FileName": "a.wav", "Color": "#3A78D4" },
              { "Name": "c", "FileName": "c.wav", "Color": "#3D9A50" },
              { "Name": "d", "FileName": "d.wav", "Color": "#d2457f" } ] }
            """);
        Assert.AreEqual(Profile.CurrentVersion, p.Version);
        Assert.AreEqual("en", p.Language, "a version 4 profile keeps its language");
        Assert.IsTrue(PadPalette.Hues.Any(h => h.Hex == p.Tiles[0].Color), "the automatic pad gets a colour of the new palette");
        Assert.IsTrue(p.Tiles[1].ColorChosen, "green on place 2 (orange) stays green");
        Assert.IsTrue(p.Tiles[2].ColorChosen);
        Assert.AreEqual("#3D9A50", p.Tiles[1].Color);
    }

    [TestMethod]
    public void Version_5_automatic_colours_change_chosen_ones_stay_and_neighbours_differ()
    {
        var tiles = string.Join(",", Enumerable.Range(0, 30).Select(i =>
            $$"""{ "Name": "p{{i}}", "FileName": "p{{i}}.wav", "Color": "{{(i == 3 ? "#7D55D0" : PadPalette.LegacyForIndex(i))}}", "ColorChosen": {{(i == 3 ? "true" : "false")}} }"""));
        var p = Load($$"""{ "Version": 5, "Language": "en", "Tiles": [ {{tiles}} ] }""");
        Assert.AreEqual("#7D55D0", p.Tiles[3].Color, "a chosen colour stays");
        var hexes = PadPalette.Hues.Select(h => h.Hex).ToHashSet();
        for (int i = 0; i < p.Tiles.Count; i++)
        {
            if (i == 3) continue;
            Assert.IsTrue(hexes.Contains(p.Tiles[i].Color), $"pad {i}: {p.Tiles[i].Color} is no colour of the new palette");
            if (i > 0) Assert.AreNotEqual(PadPalette.Nearest(p.Tiles[i - 1].Color), PadPalette.Nearest(p.Tiles[i].Color), $"pads {i - 1} and {i} look alike");
        }
    }

    [TestMethod]
    public void Random_colours_never_repeat_the_pad_before_and_spread_over_the_wheel()
    {
        var rng = new Random(7);
        var row = new List<string> { PadPalette.Hues[0].Hex };
        for (int n = 0; n < 2000; n++)
        {
            var next = PadPalette.Random(rng, [row[^1]], row.TakeLast(PadPalette.AvoidRecent));
            int a = PadPalette.Nearest(row[^1]), b = PadPalette.Nearest(next);
            int step = Math.Abs(a - b);
            Assert.IsTrue(Math.Min(step, PadPalette.Hues.Count - step) >= 2, $"{row[^1]} then {next}: same or next hue");
            Assert.IsFalse(row.TakeLast(PadPalette.AvoidRecent).Any(r => PadPalette.SameHex(r, next)), "one of the last pads repeated");
            row.Add(next);
        }
        var counts = PadPalette.Hues.Select(h => row.Count(r => r == h.Hex)).ToList();
        Assert.IsTrue(counts.Min() > 2000 / PadPalette.Hues.Count / 2, "every hue comes up: " + string.Join(", ", counts));

        // Not one fixed sequence: the next colour after the same history differs between runs.
        var after = Enumerable.Range(0, 50).Select(s => PadPalette.Random(new Random(s), [PadPalette.Hues[0].Hex], [])).Distinct().Count();
        Assert.IsTrue(after >= 5);
    }

    [TestMethod]
    public void When_every_hue_is_ruled_out_a_colour_still_comes()
    {
        var all = PadPalette.Hues.Select(h => (string?)h.Hex).ToList();
        Assert.IsTrue(PadPalette.Hues.Any(h => h.Hex == PadPalette.Random(new Random(1), all, all)));
        Assert.AreEqual(-1, PadPalette.Nearest("#808080"), "grey belongs to no hue");
        Assert.AreEqual(-1, PadPalette.Nearest("kein-hex"));
    }

    [TestMethod]
    public void Twelve_distinct_hues_around_the_wheel()
    {
        Assert.AreEqual(12, PadPalette.Hues.Count);
        Assert.AreEqual(12, PadPalette.Hues.Select(h => h.Hex).Distinct().Count());
        for (int i = 0; i < 12; i++) Assert.AreEqual(i, PadPalette.Nearest(PadPalette.Hues[i].Hex));
    }

    [TestMethod]
    public void Idle_pads_are_strong_not_washed_out()
    {
        foreach (var (name, hex) in PadPalette.Hues)
            foreach (bool dark in new[] { false, true })
            {
                var face = ColorMath.ToOklch(PadPalette.Derive(Rgb.Parse(hex), dark).Face);
                Assert.IsTrue(face.C >= 0.07, $"{name} dark={dark}: chroma {face.C:0.000}");
            }
    }

    [TestMethod]
    public void The_choice_survives_saving()
    {
        var p = new Profile();
        p.Tiles.Add(new TileConfig { Name = "x", Color = "#E8822A", ColorChosen = true });
        var json = JsonSerializer.Serialize(p, ProfileStore.JsonOptions);
        var back = Load(json);
        Assert.IsTrue(back.Tiles[0].ColorChosen);
        Assert.IsTrue(PadPalette.SameHex("#e8822a", "E8822A"));
        Assert.IsFalse(PadPalette.SameHex("#E8822A", null));
    }
}
