using System.IO.Compression;
using System.Text;
using Soundboard.Core.Config;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class ProfileArchiveTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static AppPaths Board(TempDir dir, string name)
    {
        var paths = new AppPaths(dir.File(name));
        Directory.CreateDirectory(paths.SoundsDir);
        Directory.CreateDirectory(paths.ImagesDir);
        return paths;
    }

    [TestMethod]
    public void Round_trip_keeps_pads_sounds_and_pictures_but_not_the_devices()
    {
        using var dir = new TempDir();
        var from = Board(dir, "from");
        WavFactory.WriteConstant(Path.Combine(from.SoundsDir, "horn.wav"), 48000, 2, 4800, 0.5f);
        WavFactory.WriteConstant(Path.Combine(from.SoundsDir, "drum.wav"), 48000, 2, 4800, 0.2f);
        File.WriteAllBytes(Path.Combine(from.ImagesDir, "pic.png"), TinyPng);
        var profile = new Profile { MicDeviceId = "mic-id", MicDeviceName = "My microphone", DiscordPreviousMic = "Headset" };
        profile.Tiles.Add(new TileConfig { Name = "Horn", FileName = "horn.wav", Volume = 0.7f, Hotkey = "NumPad1", Image = "pic.png", Color = "#7D55D0", ColorChosen = true });
        profile.Tiles.Add(new TileConfig { Name = "Drum", FileName = "drum.wav", Favorite = true });
        var memes = new CategoryConfig { Name = "Memes", Collapsed = true };
        profile.Categories.Add(memes);
        profile.Tiles[0].Category = memes.Id;
        var zip = dir.File("profile.zip");

        ProfileArchive.Export(profile, from, zip);

        using (var archive = ZipFile.OpenRead(zip))
        {
            var json = new StreamReader(archive.GetEntry("config.json")!.Open(), Encoding.UTF8).ReadToEnd();
            Assert.IsFalse(json.Contains("mic-id") || json.Contains("My microphone") || json.Contains("Headset"), "no devices of this computer in the file");
        }

        var (_, preview) = ProfileArchive.Read(zip);
        Assert.AreEqual(2, preview.PadCount);
        Assert.AreEqual(0, preview.MissingSounds);

        var to = Board(dir, "to");
        var imported = ProfileArchive.Import(zip, to);
        var pads = imported.Tiles;
        Assert.AreEqual(2, pads.Count);
        Assert.AreEqual(1, imported.Categories.Count, "the categories come along");
        Assert.AreEqual("Memes", imported.Categories[0].Name);
        Assert.IsTrue(imported.Categories[0].Collapsed);
        Assert.AreNotEqual(memes.Id, imported.Categories[0].Id, "with a new ID");
        Assert.AreEqual(imported.Categories[0].Id, pads[0].Category, "the pad points to the new category");
        Assert.IsTrue(pads[1].Favorite, "favourites stay favourites");
        Assert.AreEqual("Horn", pads[0].Name);
        Assert.AreEqual(0.7f, pads[0].Volume);
        Assert.AreEqual("NumPad1", pads[0].Hotkey);
        Assert.IsTrue(pads[0].ColorChosen);
        Assert.AreNotEqual(profile.Tiles[0].Id, pads[0].Id, "imported pads get new IDs");
        Assert.IsTrue(File.Exists(Path.Combine(to.SoundsDir, pads[0].FileName)));
        Assert.IsTrue(File.Exists(Path.Combine(to.ImagesDir, pads[0].Image!)));
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(from.SoundsDir, "drum.wav")), File.ReadAllBytes(Path.Combine(to.SoundsDir, pads[1].FileName)));

        // Importing again reuses identical files instead of piling up copies.
        var again = ProfileArchive.Import(zip, to).Tiles;
        Assert.AreEqual(pads[0].FileName, again[0].FileName);
        Assert.AreEqual(2, Directory.GetFiles(to.SoundsDir).Length);
    }

    [TestMethod]
    public void Names_with_folders_or_dots_never_leave_the_data_folder()
    {
        using var dir = new TempDir();
        var zip = dir.File("evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "config.json", """
                { "Version": 5, "Tiles": [
                  { "Name": "a", "FileName": "../../evil.wav" },
                  { "Name": "b", "FileName": "..\\evil.wav" },
                  { "Name": "c", "FileName": "good.wav", "Image": "../evil.png" } ] }
                """);
            Write(archive, "Sounds/../../evil.wav", "x");
            Write(archive, "../evil.wav", "x");
            WriteBytes(archive, "Sounds/good.wav", File.ReadAllBytes(MakeWav(dir)));
            WriteBytes(archive, "Bilder/../evil.png", TinyPng);
        }
        var to = Board(dir, "to");

        var (_, preview) = ProfileArchive.Read(zip);
        Assert.AreEqual(2, preview.MissingSounds, "the unsafe names count as missing");

        var pads = ProfileArchive.Import(zip, to).Tiles;
        Assert.AreEqual(1, pads.Count);
        Assert.AreEqual("good.wav", pads[0].FileName);
        Assert.IsNull(pads[0].Image);
        Assert.IsFalse(File.Exists(dir.File("evil.wav")));
        Assert.IsFalse(File.Exists(Path.Combine(to.DataDir, "evil.wav")));
        Assert.IsFalse(File.Exists(Path.Combine(to.DataDir, "evil.png")));
        Assert.AreEqual(1, Directory.GetFiles(to.DataDir, "*", SearchOption.AllDirectories).Length, "only the good sound arrived");
    }

    [TestMethod]
    public void Broken_files_say_why()
    {
        using var dir = new TempDir();
        var notZip = dir.File("notzip.zip");
        File.WriteAllText(notZip, "hello");
        var ex = Assert.ThrowsExactly<ProfileArchiveException>(() => ProfileArchive.Read(notZip));
        StringAssert.Contains(ex.Message, "notzip.zip");

        var noConfig = dir.File("noconfig.zip");
        using (var a = ZipFile.Open(noConfig, ZipArchiveMode.Create)) Write(a, "readme.txt", "hi");
        ex = Assert.ThrowsExactly<ProfileArchiveException>(() => ProfileArchive.Read(noConfig));
        StringAssert.Contains(ex.Message, "config.json");

        var badJson = dir.File("bad.zip");
        using (var a = ZipFile.Open(badJson, ZipArchiveMode.Create)) Write(a, "config.json", "{ not json");
        Assert.ThrowsExactly<ProfileArchiveException>(() => ProfileArchive.Read(badJson));

        Assert.ThrowsExactly<ProfileArchiveException>(() => ProfileArchive.Read(dir.File("missing.zip")));
    }

    private static string MakeWav(TempDir dir)
    {
        var p = dir.File("src.wav");
        WavFactory.WriteConstant(p, 48000, 1, 480, 0.1f);
        return p;
    }

    private static void Write(ZipArchive a, string name, string text) => WriteBytes(a, name, Encoding.UTF8.GetBytes(text));

    private static void WriteBytes(ZipArchive a, string name, byte[] data)
    {
        using var s = a.CreateEntry(name).Open();
        s.Write(data);
    }
}
