using Soundboard.Core.Config;
using Soundboard.Core.Design;
using Soundboard.Core.Images;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class PadImageTests
{
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private static string WritePicture(string path, byte[] head, byte fill)
    {
        var bytes = new byte[64];
        head.CopyTo(bytes, 0);
        for (int i = head.Length; i < bytes.Length; i++) bytes[i] = fill;
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ---------------------------------------------------------------- profile

    [TestMethod]
    public void Version_2_profile_loads_and_is_migrated()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """
            {
              "Version": 2,
              "Tiles": [ { "Name": "Tusch", "FileName": "tusch.wav", "Volume": 1, "Color": "#00939B" } ]
            }
            """);

        var loaded = ProfileStore.Load(path);

        Assert.IsNull(loaded.Problem);
        Assert.AreEqual(Profile.CurrentVersion, loaded.Profile.Version);
        Assert.AreEqual(5, Profile.CurrentVersion);
        Assert.IsNull(loaded.Profile.Tiles[0].Image);
    }

    [TestMethod]
    public void Version_1_profile_without_design_and_pictures_loads()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "Version": 1, "Tiles": [ { "Name": "A", "FileName": "a.wav" } ] }""");

        var loaded = ProfileStore.Load(path);

        Assert.IsNull(loaded.Problem);
        Assert.AreEqual(Profile.CurrentVersion, loaded.Profile.Version);
        Assert.IsNull(loaded.Profile.Tiles[0].Image);
    }

    [TestMethod]
    public void Picture_name_survives_a_round_trip()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var profile = new Profile { Tiles = [new TileConfig { Name = "A", FileName = "a.wav", Image = "katze.png" }] };

        ProfileStore.Save(path, profile);
        var p = ProfileStore.Load(path).Profile;

        Assert.AreEqual("katze.png", p.Tiles[0].Image);
    }

    [TestMethod]
    public void Picture_names_with_folders_are_dropped()
    {
        var profile = new Profile
        {
            Version = 3,
            Tiles =
            [
                new TileConfig { Image = @"..\..\fremd.png" },
                new TileConfig { Image = @"C:\Bilder\a.png" },
                new TileConfig { Image = "   " },
                new TileConfig { Image = "gut.jpg" },
            ],
        };

        Assert.IsTrue(profile.Migrate());

        Assert.IsNull(profile.Tiles[0].Image);
        Assert.IsNull(profile.Tiles[1].Image);
        Assert.IsNull(profile.Tiles[2].Image);
        Assert.AreEqual("gut.jpg", profile.Tiles[3].Image);
    }

    // ---------------------------------------------------------------- import

    [TestMethod]
    public void Import_copies_keeps_original_and_numbers_clashes()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        var a = WritePicture(dir.File("katze.png"), PngHead, 1);
        var other = Directory.CreateDirectory(dir.File("anders")).FullName;
        var b = WritePicture(Path.Combine(other, "katze.png"), PngHead, 2);

        var n1 = paths.ImportImage(a);
        var n1Again = paths.ImportImage(a);
        var n2 = paths.ImportImage(b);

        Assert.AreEqual("katze.png", n1);
        Assert.AreEqual("katze.png", n1Again, "same content is reused");
        Assert.AreEqual("katze (2).png", n2);
        Assert.IsTrue(File.Exists(a), "original stays");
        Assert.IsTrue(File.Exists(Path.Combine(paths.ImagesDir, "katze (2).png")));
        Assert.AreEqual(Path.Combine(paths.DataDir, "Bilder"), paths.ImagesDir);
    }

    [TestMethod]
    public void Import_refuses_a_broken_picture_and_copies_nothing()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        var broken = dir.File("kaputt.jpg");
        File.WriteAllText(broken, "das ist kein Bild");

        var ex = Assert.ThrowsException<ImageImportException>(() => paths.ImportImage(broken));

        StringAssert.Contains(ex.Message, "kaputt.jpg");
        StringAssert.Contains(ex.Message, "beschädigt");
        Assert.IsFalse(Directory.Exists(paths.ImagesDir) && Directory.EnumerateFiles(paths.ImagesDir).Any());
    }

    [TestMethod]
    public void Import_refuses_an_empty_file()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        var empty = dir.File("leer.png");
        File.WriteAllBytes(empty, []);

        Assert.ThrowsException<ImageImportException>(() => paths.ImportImage(empty));
    }

    [TestMethod]
    public void Import_of_a_missing_file_says_so()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));

        var ex = Assert.ThrowsException<ImageImportException>(() => paths.ImportImage(dir.File("weg.png")));

        StringAssert.Contains(ex.Message, "gibt es nicht");
    }

    [TestMethod]
    public void Import_refuses_other_file_types()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        var psd = WritePicture(dir.File("ebene.psd"), PngHead, 1);

        var ex = Assert.ThrowsException<ImageImportException>(() => paths.ImportImage(psd));

        StringAssert.Contains(ex.Message, "PNG, JPG, GIF, BMP oder WebP");
    }

    [TestMethod]
    public void Header_check_knows_all_formats()
    {
        Assert.AreEqual(ImageKind.Png, ImageFiles.Sniff(PngHead));
        Assert.AreEqual(ImageKind.Jpeg, ImageFiles.Sniff([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.AreEqual(ImageKind.Gif, ImageFiles.Sniff("GIF89a"u8));
        Assert.AreEqual(ImageKind.Gif, ImageFiles.Sniff("GIF87a"u8));
        Assert.AreEqual(ImageKind.Bmp, ImageFiles.Sniff("BM\0\0"u8));
        Assert.AreEqual(ImageKind.Webp, ImageFiles.Sniff("RIFF\x1A\0\0\0WEBPVP8L"u8));
        Assert.IsNull(ImageFiles.Sniff("RIFF\x1A\0\0\0WAVEfmt "u8), "a WAV is no picture");
        Assert.IsNull(ImageFiles.Sniff("ID3"u8));
    }

    [TestMethod]
    public void Only_released_and_unused_pictures_are_deleted()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        Directory.CreateDirectory(paths.ImagesDir);
        WritePicture(Path.Combine(paths.ImagesDir, "weg.png"), PngHead, 1);
        WritePicture(Path.Combine(paths.ImagesDir, "zurueck.png"), PngHead, 2);
        WritePicture(Path.Combine(paths.ImagesDir, "fremd.png"), PngHead, 3);

        paths.ReleaseImage("weg.png");
        paths.ReleaseImage("zurueck.png"); // "Rückgängig" brought it back: a pad uses it again
        paths.ReleaseImage(@"..\ausserhalb.png");
        int removed = paths.RemoveReleasedImages(["ZURUECK.png", null]);

        Assert.AreEqual(1, removed);
        Assert.IsFalse(File.Exists(Path.Combine(paths.ImagesDir, "weg.png")));
        Assert.IsTrue(File.Exists(Path.Combine(paths.ImagesDir, "zurueck.png")));
        Assert.IsTrue(File.Exists(Path.Combine(paths.ImagesDir, "fremd.png")), "never released: never touched");
        Assert.IsFalse(File.Exists(paths.ReleasedImagesFile), "list is done");
    }

    [TestMethod]
    public void A_new_or_reset_profile_does_not_wipe_the_picture_folder()
    {
        // e.g. after "Alles entfernen" with "Sounds behalten": no profile, pictures stay.
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        Directory.CreateDirectory(paths.ImagesDir);
        WritePicture(Path.Combine(paths.ImagesDir, "alt.png"), PngHead, 1);

        Assert.AreEqual(0, paths.RemoveReleasedImages([]));
        Assert.AreEqual(0, paths.RemoveReleasedImages([]), "also not in the session after");
        Assert.IsTrue(File.Exists(Path.Combine(paths.ImagesDir, "alt.png")));
    }

    [TestMethod]
    public void Cleanup_without_folder_does_nothing()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.File("data"));
        paths.ReleaseImage("a.png");
        Assert.AreEqual(0, paths.RemoveReleasedImages([]));
    }

    [TestMethod]
    public void Plain_file_names_only()
    {
        Assert.IsTrue(ImageFiles.IsPlainFileName("Katze (2).png"));
        Assert.IsFalse(ImageFiles.IsPlainFileName("."));
        Assert.IsFalse(ImageFiles.IsPlainFileName(".."));
        Assert.IsFalse(ImageFiles.IsPlainFileName("a/b.png"));
        Assert.IsFalse(ImageFiles.IsPlainFileName(@"a\b.png"));
        Assert.IsFalse(ImageFiles.IsPlainFileName("a:b.png"));
        Assert.IsFalse(ImageFiles.IsPlainFileName(null));
    }

    [TestMethod]
    public void Image_path_only_for_plain_names()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "x"));
        Assert.AreEqual(Path.Combine(paths.ImagesDir, "a.png"), paths.ImagePath("a.png"));
        Assert.IsNull(paths.ImagePath(null));
        Assert.IsNull(paths.ImagePath(@"..\a.png"));
    }

    // ---------------------------------------------------------------- decode size

    [TestMethod]
    public void Big_photos_are_decoded_small_but_still_cover_the_pad()
    {
        // Landscape 4000 × 3000: the width decides (640 / 4000), the height (480) still covers 336.
        int w = ImageFiles.CoverDecodeWidth(4000, 3000);
        Assert.AreEqual(ImageFiles.DecodeBoxWidth, w);
        Assert.IsTrue(w * 3000.0 / 4000 >= ImageFiles.DecodeBoxHeight);

        // Portrait 1080 × 1920: the width decides.
        Assert.AreEqual(ImageFiles.DecodeBoxWidth, ImageFiles.CoverDecodeWidth(1080, 1920));

        // Panorama 6000 × 1000: height 336 → width 2016, never narrower than needed.
        int p = ImageFiles.CoverDecodeWidth(6000, 1000);
        Assert.IsTrue(p * 1000.0 / 6000 >= ImageFiles.DecodeBoxHeight - 1, $"{p}");

        // Small pictures stay as they are.
        Assert.AreEqual(0, ImageFiles.CoverDecodeWidth(300, 200));
        Assert.AreEqual(0, ImageFiles.CoverDecodeWidth(0, 0));
    }

    // ---------------------------------------------------------------- contrast

    [TestMethod]
    public void Text_on_a_white_picture_stays_readable()
    {
        double c = PadImageStyle.TextContrastWorstCase;
        Assert.IsTrue(c >= 4.5, $"Schrift auf Weiß + Verlauf: {c:0.00} : 1");
    }

    [TestMethod]
    public void Text_on_every_grey_picture_stays_readable()
    {
        for (int v = 0; v <= 255; v += 5)
        {
            var under = PadImageStyle.Over(new Rgb((byte)v, (byte)v, (byte)v), PadImageStyle.BackdropAlpha);
            double c = ColorMath.Contrast(PadImageStyle.Text, under);
            Assert.IsTrue(c >= 4.5, $"Grau {v}: {c:0.00} : 1");
        }
    }

    [TestMethod]
    public void Progress_bar_is_visible_on_every_backdrop_in_every_pad_colour()
    {
        foreach (bool dark in new[] { false, true })
        {
            foreach (var (name, hex) in PadPalette.Hues)
            {
                var lit = PadPalette.Derive(Rgb.Parse(hex), dark).Lit;
                double light = ColorMath.Contrast(lit, PadImageStyle.WorstCaseBackdrop);
                double darkest = ColorMath.Contrast(lit, PadImageStyle.DarkestBackdrop);
                Assert.IsTrue(Math.Min(light, darkest) >= 3, $"{name} {(dark ? "dunkel" : "hell")}: {light:0.0} / {darkest:0.0}");
            }
        }
    }
}
