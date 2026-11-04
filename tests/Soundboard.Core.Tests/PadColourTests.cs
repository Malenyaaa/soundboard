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
    public void Version_4_profile_keeps_every_colour_and_marks_only_odd_ones_as_chosen()
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
        Assert.IsFalse(p.Tiles[0].ColorChosen);
        Assert.IsTrue(p.Tiles[1].ColorChosen, "green on place 2 (orange) stays green");
        Assert.IsTrue(p.Tiles[2].ColorChosen);
        Assert.AreEqual("#3D9A50", p.Tiles[1].Color);
    }

    [TestMethod]
    public void Automatic_pads_follow_their_place_chosen_ones_stay()
    {
        var tiles = new List<TileConfig>
        {
            new() { Name = "a", Color = PadPalette.ForIndex(1) },
            new() { Name = "b", Color = "#7D55D0", ColorChosen = true },
            new() { Name = "c", Color = PadPalette.ForIndex(0) },
        };
        var changed = PadPalette.Recolor(tiles);
        CollectionAssert.AreEqual(new[] { 0, 2 }, changed);
        Assert.AreEqual(PadPalette.ForIndex(0), tiles[0].Color);
        Assert.AreEqual("#7D55D0", tiles[1].Color);
        Assert.AreEqual(PadPalette.ForIndex(2), tiles[2].Color);
        Assert.AreEqual(0, PadPalette.Recolor(tiles).Count, "a second pass changes nothing");
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
