using Soundboard.Core.Config;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class OwnTracesTests
{
    private static AppPaths Fill(TempDir dir)
    {
        var paths = new AppPaths(Path.Combine(dir.Path, "Soundboard"));
        Directory.CreateDirectory(paths.SoundsDir);
        Directory.CreateDirectory(paths.RecordingsDir);
        Directory.CreateDirectory(paths.ImagesDir);
        File.WriteAllText(paths.ProfileFile, "{}");
        File.WriteAllText(paths.SnapshotFile, "{}");
        File.WriteAllText(Path.Combine(paths.DataDir, "audio-snapshot.vorher.json"), "{}");
        File.WriteAllText(paths.DirtyFlagFile, "{}");
        File.WriteAllText(Path.Combine(paths.SoundsDir, "a.wav"), "x");
        File.WriteAllText(Path.Combine(paths.RecordingsDir, "test.wav"), "x");
        File.WriteAllText(Path.Combine(paths.ImagesDir, "a.png"), "x");
        return paths;
    }

    [TestMethod]
    public void Keeps_sounds_when_asked()
    {
        using var dir = new TempDir();
        var paths = Fill(dir);
        // Something that is not ours must survive.
        var foreign = Path.Combine(dir.Path, "fremd.txt");
        File.WriteAllText(foreign, "x");

        var result = OwnTraces.Remove(paths, keepSounds: true);

        Assert.IsTrue(result.All(r => r.Removed));
        Assert.IsTrue(File.Exists(Path.Combine(paths.SoundsDir, "a.wav")));
        Assert.IsTrue(File.Exists(Path.Combine(paths.ImagesDir, "a.png")), "pictures are kept with the sounds");
        Assert.IsFalse(File.Exists(paths.ProfileFile));
        Assert.IsFalse(File.Exists(paths.SnapshotFile));
        Assert.IsFalse(File.Exists(paths.DirtyFlagFile));
        Assert.IsFalse(Directory.Exists(paths.RecordingsDir));
        Assert.IsTrue(Directory.Exists(paths.DataDir), "data folder stays because the sounds are in it");
        Assert.IsTrue(File.Exists(foreign));
    }

    [TestMethod]
    public void Removes_everything_including_folder_without_keep()
    {
        using var dir = new TempDir();
        var paths = Fill(dir);

        var listed = OwnTraces.List(paths, keepSounds: false);
        Assert.IsTrue(listed.Any(l => l.Path == paths.SoundsDir));
        Assert.IsTrue(listed.Any(l => l.Path == paths.ImagesDir));

        OwnTraces.Remove(paths, keepSounds: false);

        Assert.IsFalse(Directory.Exists(paths.DataDir));
    }

    [TestMethod]
    public void Unknown_files_in_data_folder_are_left_alone()
    {
        using var dir = new TempDir();
        var paths = Fill(dir);
        var other = Path.Combine(paths.DataDir, "notizen.txt");
        File.WriteAllText(other, "x");

        OwnTraces.Remove(paths, keepSounds: false);

        Assert.IsTrue(File.Exists(other));
        Assert.IsTrue(Directory.Exists(paths.DataDir));
    }

    [TestMethod]
    public void Nothing_there_lists_nothing()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(Path.Combine(dir.Path, "fehlt"));
        Assert.AreEqual(0, OwnTraces.List(paths, false).Count);
        Assert.AreEqual(0, OwnTraces.Remove(paths, false).Count);
    }
}
