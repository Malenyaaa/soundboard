namespace Soundboard.Core.Images;

/// <summary>Picture formats a pad can show. Decoding itself is done by Windows (WIC) in the app.</summary>
public enum ImageKind
{
    Png,
    Jpeg,
    Gif,
    Bmp,
    Webp,
}

/// <summary>A picture could not be taken over; <see cref="Exception.Message"/> is a sentence in the language of the interface with the reason.</summary>
public sealed class ImageImportException(string message) : Exception(message);

/// <summary>
/// Pad pictures without any UI dependency: which files count as pictures, a header check
/// (so a broken or misnamed file is refused before it is copied) and the decode size.
/// </summary>
public static class ImageFiles
{
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    /// <summary>The formats in words, for messages.</summary>
    public static string FormatsText => Text.L.Get("Image.Formats");

    /// <summary>Filter for the Windows file dialog.</summary>
    public static string DialogFilter => Text.L.Get("Image.DialogFilter");

    /// <summary>Decoded pictures cover at most this box (twice the largest pad), so big photos stay small in memory.</summary>
    public const int DecodeBoxWidth = 720;
    public const int DecodeBoxHeight = 400;

    public static bool HasImageExtension(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Kind by the first bytes of the file; null if it is none of the supported formats (or unreadable).</summary>
    public static ImageKind? Sniff(string path)
    {
        Span<byte> head = stackalloc byte[12];
        int n;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        return Sniff(head[..n]);
    }

    public static ImageKind? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47 && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return ImageKind.Png;
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ImageKind.Jpeg;
        if (h.Length >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8' && (h[4] == '7' || h[4] == '9') && h[5] == 'a') return ImageKind.Gif;
        if (h.Length >= 2 && h[0] == 'B' && h[1] == 'M') return ImageKind.Bmp;
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ImageKind.Webp;
        return null;
    }

    /// <summary>A plain file name (no folder, no ".."), as stored in the profile.</summary>
    public static bool IsPlainFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name is not ("." or "..")
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// Width to decode a picture of <paramref name="width"/> × <paramref name="height"/> pixels so it
    /// still covers the decode box (uniform to fill). 0 = keep the full size (the picture is small enough).
    /// </summary>
    public static int CoverDecodeWidth(int width, int height, int boxWidth = DecodeBoxWidth, int boxHeight = DecodeBoxHeight)
    {
        if (width <= 0 || height <= 0) return 0;
        double scale = Math.Max((double)boxWidth / width, (double)boxHeight / height);
        if (scale >= 1) return 0;
        return Math.Max(1, (int)Math.Ceiling(width * scale));
    }

    /// <summary>
    /// Checks a picture before it is copied: it must exist, have a picture ending and start like
    /// one of the supported formats. Throws <see cref="ImageImportException"/> with the reason.
    /// </summary>
    public static ImageKind Check(string path)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
            throw new ImageImportException(Text.L.F("Image.Missing", name));
        if (!HasImageExtension(path))
            throw new ImageImportException(Text.L.F("Image.NotPicture", name, FormatsText));
        return Sniff(path)
            ?? throw new ImageImportException(Text.L.F("Image.Unreadable", name, FormatsText));
    }
}
