using Soundboard.Core.Audio;
using Soundboard.Core.Config;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class ProfileTests
{
    [TestMethod]
    public void Round_trip_keeps_all_values()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var tile = new TileConfig { Name = "Tusch", FileName = "tusch.mp3", Volume = 0.6f, Color = "#FFE0B2" };
        var profile = new Profile
        {
            MonitorDeviceId = "{0.0.0.00000000}.{test}",
            MonitorDeviceName = "Kopfhörer",
            MonitorEnabled = false,
            MasterVolume = 0.42f,
            Overlap = OverlapMode.Overlay,
            Tiles = [tile],
        };

        ProfileStore.Save(path, profile);
        var loaded = ProfileStore.Load(path);

        Assert.IsNull(loaded.Problem);
        var p = loaded.Profile;
        Assert.AreEqual(profile.MonitorDeviceId, p.MonitorDeviceId);
        Assert.AreEqual("Kopfhörer", p.MonitorDeviceName);
        Assert.IsFalse(p.MonitorEnabled);
        Assert.AreEqual(0.42f, p.MasterVolume);
        Assert.AreEqual(OverlapMode.Overlay, p.Overlap);
        Assert.AreEqual(1, p.Tiles.Count);
        Assert.AreEqual(tile.Id, p.Tiles[0].Id);
        Assert.AreEqual("Tusch", p.Tiles[0].Name);
        Assert.AreEqual(0.6f, p.Tiles[0].Volume);
        StringAssert.Contains(File.ReadAllText(path), "\"Overlay\"", "enums as text");
    }

    [TestMethod]
    public void Missing_file_gives_empty_profile_without_problem()
    {
        using var dir = new TempDir();
        var loaded = ProfileStore.Load(dir.File("nicht-da.json"));
        Assert.IsNull(loaded.Problem);
        Assert.AreEqual(0, loaded.Profile.Tiles.Count);
        Assert.IsTrue(loaded.Profile.MonitorEnabled);
    }

    [TestMethod]
    public void Broken_json_is_backed_up_and_reported()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, "{ das ist kein JSON");

        var loaded = ProfileStore.Load(path);

        Assert.IsNotNull(loaded.Problem);
        Assert.AreEqual(0, loaded.Profile.Tiles.Count);
        Assert.AreEqual(1, Directory.GetFiles(dir.Path, "config.defekt-*.json").Length);
    }

    [TestMethod]
    public void Import_copies_and_numbers_name_clashes()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        var a = dir.File("tusch.wav");
        WavFactory.WriteConstant(a, 8000, 1, 10, 0.1f);
        var other = Directory.CreateDirectory(dir.File("anders")).FullName;
        var b = Path.Combine(other, "tusch.wav");
        WavFactory.WriteConstant(b, 8000, 1, 10, 0.2f);

        var n1 = paths.ImportSound(a);
        var n1Again = paths.ImportSound(a);
        var n2 = paths.ImportSound(b);

        Assert.AreEqual("tusch.wav", n1);
        Assert.AreEqual("tusch.wav", n1Again, "same content is reused");
        Assert.AreEqual("tusch (2).wav", n2);
        Assert.IsTrue(File.Exists(a), "original stays");
    }
}
