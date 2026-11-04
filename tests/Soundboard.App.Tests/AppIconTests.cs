using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Soundboard.App.Icons;
using Soundboard.Core.Design;

namespace Soundboard.App.Tests;

[TestClass]
public class AppIconTests
{
    /// <summary>WPF rendering needs an STA thread; MSTest runs tests on MTA threads.</summary>
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    private static byte[] Pixels(BitmapSource b)
    {
        if (b.Format != PixelFormats.Pbgra32) b = new FormatConvertedBitmap(b, PixelFormats.Pbgra32, null, 0);
        var px = new byte[b.PixelWidth * b.PixelHeight * 4];
        b.CopyPixels(px, b.PixelWidth * 4, 0);
        return px;
    }

    private static (byte B, byte G, byte R, byte A) At(byte[] px, int size, int x, int y)
    {
        int i = (y * size + x) * 4;
        return (px[i], px[i + 1], px[i + 2], px[i + 3]);
    }

    private static readonly Rgb[] Accents =
    [
        .. AccentPalette.Presets.Select(p => AccentPalette.Resolve(p.Id, null, false).Fill),
        Rgb.Parse("#000000"),
        Rgb.Parse("#FFFFFF"),
        Rgb.Parse("#FFEE00"),
    ];

    // ------------------------------------------------------------------ ICO writer

    [TestMethod]
    public void IcoHasHeaderDirectoryAndPngImages()
    {
        byte[] ico = [];
        OnSta(() => ico = IcoFile.Build(AppIconDrawing.DefaultAccent, AppIconDrawing.IcoSizes));

        Assert.AreEqual(0, BitConverter.ToUInt16(ico, 0), "reserved");
        Assert.AreEqual(1, BitConverter.ToUInt16(ico, 2), "type icon");
        Assert.AreEqual(6, BitConverter.ToUInt16(ico, 4), "count");

        int[] expected = [16, 20, 24, 32, 48, 256];
        int end = IcoFile.HeaderSize + IcoFile.EntrySize * expected.Length;
        for (int i = 0; i < expected.Length; i++)
        {
            int e = IcoFile.HeaderSize + IcoFile.EntrySize * i;
            byte stored = expected[i] == 256 ? (byte)0 : (byte)expected[i];
            Assert.AreEqual(stored, ico[e], $"width {expected[i]}");
            Assert.AreEqual(stored, ico[e + 1], $"height {expected[i]}");
            Assert.AreEqual(0, ico[e + 2], "colours");
            Assert.AreEqual(0, ico[e + 3], "reserved");
            Assert.AreEqual(1, BitConverter.ToUInt16(ico, e + 4), "planes");
            Assert.AreEqual(32, BitConverter.ToUInt16(ico, e + 6), "bits");
            int length = BitConverter.ToInt32(ico, e + 8);
            int offset = BitConverter.ToInt32(ico, e + 12);
            Assert.AreEqual(end, offset, "images follow each other without gaps");
            Assert.IsTrue(ico.AsSpan(offset, length).StartsWith(IcoFile.PngSignature), $"PNG at {expected[i]}");
            end += length;
        }
        Assert.AreEqual(ico.Length, end, "nothing after the last image");

        var read = IcoFile.Read(ico);
        CollectionAssert.AreEqual(expected, read.Select(r => r.Size).ToArray());
        OnSta(() =>
        {
            foreach (var (size, data) in read)
            {
                var frame = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Assert.AreEqual(size, frame.PixelWidth);
                Assert.AreEqual(size, frame.PixelHeight);
            }
        });
    }

    [TestMethod]
    public void IcoWriterRejectsWrongInput()
    {
        byte[] notPng = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        Assert.ThrowsException<ArgumentException>(() => IcoFile.Write([]));
        Assert.ThrowsException<ArgumentException>(() => IcoFile.Write([(16, notPng)]));
        byte[] png = [.. IcoFile.PngSignature, 0];
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => IcoFile.Write([(257, png)]));
        Assert.ThrowsException<InvalidDataException>(() => IcoFile.Read([0, 0, 2, 0, 1, 0]));
    }

    [TestMethod]
    public void CheckedInIcoMatchesTheDrawing()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Soundboard.ico");
        var stored = IcoFile.Read(File.ReadAllBytes(path));
        CollectionAssert.AreEqual(AppIconDrawing.IcoSizes.ToArray(), stored.Select(s => s.Size).ToArray());
        OnSta(() =>
        {
            foreach (var (size, data) in stored)
            {
                var fromFile = Pixels(BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]);
                var fresh = Pixels(AppIconDrawing.Render(size, AppIconDrawing.DefaultAccent));
                int worst = 0;
                for (int i = 0; i < fresh.Length; i++) worst = Math.Max(worst, Math.Abs(fresh[i] - fromFile[i]));
                Assert.IsTrue(worst <= 2, $"{size} px differs by up to {worst}; regenerate with tools/IconGen");
            }
        });
    }

    // ------------------------------------------------------------------ geometry

    [TestMethod]
    public void EveryIconSizeGivesAFullImage()
    {
        OnSta(() =>
        {
            foreach (int size in AppIconDrawing.IcoSizes.Concat([28, 36, 40, 64, 128]))
            {
                var bmp = AppIconDrawing.Render(size, AppIconDrawing.DefaultAccent);
                Assert.AreEqual(size, bmp.PixelWidth);
                Assert.AreEqual(size, bmp.PixelHeight);
                var px = Pixels(bmp);
                Assert.AreEqual(0, At(px, size, 0, 0).A, $"{size}: corner stays transparent");
                Assert.AreEqual(255, At(px, size, size / 2, size / 2).A, $"{size}: middle is covered");
                int white = 0;
                for (int i = 0; i < px.Length; i += 4) if (px[i] == 255 && px[i + 1] == 255 && px[i + 2] == 255 && px[i + 3] == 255) white++;
                Assert.IsTrue(white > 0, $"{size}: waveform bars are there");

                var img = AppIconDrawing.Image(size, AppIconDrawing.DefaultAccent);
                Assert.AreEqual(size, img.Width, 1e-9);
                Assert.AreEqual(size, img.Height, 1e-9);
            }
        });
    }

    [TestMethod]
    public void SmallSizesSitOnWholePixels()
    {
        OnSta(() =>
        {
            var accent = AppIconDrawing.DefaultAccent;
            var edge = AppIconDrawing.EdgeFor(accent);
            var px = Pixels(AppIconDrawing.Render(16, accent));
            // Middle bar of the 16 px icon: x 7–8, y 2–10, pure white.
            Assert.AreEqual((255, 255, 255, 255), At(px, 16, 7, 2));
            Assert.AreEqual((255, 255, 255, 255), At(px, 16, 8, 10));
            // Face between the bars, edge below the face.
            Assert.AreEqual((accent.B, accent.G, accent.R, (byte)255), At(px, 16, 5, 6));
            Assert.AreEqual((edge.B, edge.G, edge.R, (byte)255), At(px, 16, 8, 13));
            // Rows 0 and 15 stay free.
            for (int x = 0; x < 16; x++)
            {
                Assert.AreEqual(0, At(px, 16, x, 0).A);
                Assert.AreEqual(0, At(px, 16, x, 15).A);
            }
            // 16/20/24 px: the bars are whole pixels: exactly 2 px × bar heights pure white,
            // and no pixel mixes white into the face.
            foreach (var (size, whitePixels) in new[] { (16, 34), (20, 44), (24, 64) })
            {
                var p = Pixels(AppIconDrawing.Render(size, accent));
                int white = 0;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var c = At(p, size, x, y);
                        if (c == (255, 255, 255, 255)) { white++; continue; }
                        bool lighterThanFace = c.A == 255 && c.R >= accent.R && c.G >= accent.G && c.B >= accent.B
                            && c != (accent.B, accent.G, accent.R, (byte)255);
                        Assert.IsFalse(lighterThanFace, $"{size}: blurred bar pixel at {x},{y}");
                    }
                Assert.AreEqual(whitePixels, white, $"{size}: white pixels");
            }
        });
    }

    [TestMethod]
    public void EdgeIsTheSameHueDarker()
    {
        foreach (var a in Accents)
        {
            var o = ColorMath.ToOklch(a);
            var e = ColorMath.ToOklch(AppIconDrawing.EdgeFor(a));
            Assert.IsTrue(e.L <= o.L - 0.05 || o.L < 0.11, $"{a}: edge darker");
        }
    }

    // ------------------------------------------------------------------ tray

    [TestMethod]
    public void TrayIconRendersInEveryAccentAndSize()
    {
        OnSta(() =>
        {
            foreach (var accent in Accents)
                foreach (int size in new[] { 16, 20, 24, 28, 32 })
                {
                    using var icon = TrayIconImage.Create(accent, size);
                    Assert.AreEqual(size, icon.Width, $"{accent} {size}");
                    Assert.AreEqual(size, icon.Height);
                    Assert.AreNotEqual(IntPtr.Zero, icon.Handle);
                    using var bmp = icon.ToBitmap();
                    var mid = bmp.GetPixel(size / 2, AppIconDrawing.EdgeRow(size));
                    var edge = AppIconDrawing.EdgeFor(accent);
                    Assert.AreEqual(edge.R, mid.R, 1, $"{accent} {size}: edge colour in the tray icon");
                    Assert.AreEqual(edge.G, mid.G, 1);
                    Assert.AreEqual(edge.B, mid.B, 1);
                }
        });
    }

    [TestMethod]
    public void TraySizeIsASmallIconSize()
    {
        int size = TrayIconImage.CurrentSize();
        Assert.IsTrue(size is >= 16 and <= 256, size.ToString());
    }
}
