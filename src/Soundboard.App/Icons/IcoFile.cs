using System.IO;
using System.Windows.Media.Imaging;
using Soundboard.Core.Design;

namespace Soundboard.App.Icons;

/// <summary>
/// Writes and reads ICO files whose images are stored as PNG (Windows Vista and later):
/// 6-byte header (0, type 1, count), one 16-byte directory entry per image
/// (width, height with 256 written as 0, 0 colours, 0, 1 plane, 32 bpp, length, offset),
/// then the PNG data in the same order.
/// </summary>
public static class IcoFile
{
    public const int HeaderSize = 6;
    public const int EntrySize = 16;

    /// <summary>The PNG file signature.</summary>
    public static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The app icon in the given accent with one PNG image per size. Needs an STA thread.</summary>
    public static byte[] Build(Rgb accent, IEnumerable<int> sizes) =>
        Write(sizes.Select(s => (s, EncodePng(AppIconDrawing.Render(s, accent)))).ToList());

    /// <summary>Encodes a bitmap as PNG.</summary>
    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Puts square PNG images (1 to 256 px) into one ICO file.</summary>
    public static byte[] Write(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        if (images.Count is 0 or > ushort.MaxValue) throw new ArgumentException("1 to 65535 images", nameof(images));
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)images.Count);
            int offset = HeaderSize + EntrySize * images.Count;
            foreach (var (size, png) in images)
            {
                if (size is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(images), size, "1 to 256 px");
                if (!png.AsSpan().StartsWith(PngSignature)) throw new ArgumentException("not a PNG image", nameof(images));
                byte b = size == 256 ? (byte)0 : (byte)size;
                w.Write(b);
                w.Write(b);
                w.Write((byte)0);   // colours in the palette
                w.Write((byte)0);   // reserved
                w.Write((ushort)1); // colour planes
                w.Write((ushort)32);
                w.Write(png.Length);
                w.Write(offset);
                offset += png.Length;
            }
            foreach (var (_, png) in images) w.Write(png);
        }
        return ms.ToArray();
    }

    /// <summary>The images of an ICO file: size (0 read as 256) and the stored bytes.</summary>
    public static IReadOnlyList<(int Size, byte[] Data)> Read(byte[] ico)
    {
        if (ico.Length < HeaderSize) throw new InvalidDataException("too short");
        var span = ico.AsSpan();
        if (BitConverter.ToUInt16(span[0..2]) != 0 || BitConverter.ToUInt16(span[2..4]) != 1) throw new InvalidDataException("not an icon");
        int count = BitConverter.ToUInt16(span[4..6]);
        if (ico.Length < HeaderSize + EntrySize * count) throw new InvalidDataException("directory cut off");
        var list = new List<(int, byte[])>(count);
        for (int i = 0; i < count; i++)
        {
            var e = span.Slice(HeaderSize + EntrySize * i, EntrySize);
            int size = e[0] == 0 ? 256 : e[0];
            int length = BitConverter.ToInt32(e[8..12]);
            int offset = BitConverter.ToInt32(e[12..16]);
            if (length < 0 || offset < 0 || (long)offset + length > ico.Length) throw new InvalidDataException("image outside the file");
            list.Add((size, span.Slice(offset, length).ToArray()));
        }
        return list;
    }
}
