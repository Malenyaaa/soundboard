using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Soundboard.Core.Images;

namespace Soundboard.App.Images;

public enum ImageProblem
{
    None,
    Missing,
    Broken,
    NoWebpCodec,
}

public sealed record ImageLoadResult(BitmapSource? Image, ImageProblem Problem, string? Detail = null);

/// <summary>
/// Loads a pad picture with Windows' own decoders (WIC): only the first frame (GIF), scaled down
/// to the pad box, turned upright by the camera's orientation tag, frozen, and without keeping
/// the file open. Safe to call from a worker thread.
/// </summary>
public static class ImageLoader
{
    // 1 × 1 lossless WebP, to find out whether Windows has a WebP decoder.
    private const string WebpProbe = "UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==";
    private static readonly Lazy<bool> WebpSupported = new(() =>
    {
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(WebpProbe));
            var d = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return d.Frames.Count > 0 && d.Frames[0].PixelWidth == 1;
        }
        catch (Exception ex) when (IsDecodeError(ex))
        {
            return false;
        }
    });

    /// <summary>True if Windows on this computer can read WebP pictures.</summary>
    public static bool CanReadWebp => WebpSupported.Value;

    public static ImageLoadResult Load(string path)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path)) return new(null, ImageProblem.Missing);
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, ImageProblem.Broken, ex.Message);
        }

        var kind = ImageFiles.Sniff(bytes);
        if (kind == ImageKind.Webp && !CanReadWebp) return new(null, ImageProblem.NoWebpCodec);
        try
        {
            int width, height;
            Rotation turn;
            using (var probe = new MemoryStream(bytes, writable: false))
            {
                var frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                turn = ReadOrientation(frame);
            }
            bool sideways = turn is Rotation.Rotate90 or Rotation.Rotate270;
            // Cover the box with the picture as it will stand (rotated), then decode the stored width.
            int shownWidth = ImageFiles.CoverDecodeWidth(sideways ? height : width, sideways ? width : height);
            int decodeWidth = shownWidth == 0 ? 0
                : sideways ? (int)Math.Ceiling(shownWidth * (double)width / height)
                : shownWidth;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes, writable: false);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();

            BitmapSource result = bmp;
            if (turn != Rotation.Rotate0)
            {
                var upright = new TransformedBitmap(bmp, new RotateTransform(turn switch
                {
                    Rotation.Rotate90 => 90,
                    Rotation.Rotate180 => 180,
                    _ => 270,
                }));
                upright.Freeze();
                result = upright;
            }
            return new(result, ImageProblem.None);
        }
        catch (Exception ex) when (IsDecodeError(ex))
        {
            return new(null, kind == ImageKind.Webp && !CanReadWebp ? ImageProblem.NoWebpCodec : ImageProblem.Broken, ex.Message);
        }
    }

    /// <summary>EXIF orientation of photos (tag 274); mirrored variants are only turned.</summary>
    private static Rotation ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/app1/ifd/{ushort=274}")
                && meta.GetQuery("/app1/ifd/{ushort=274}") is ushort o)
            {
                return o switch
                {
                    3 or 4 => Rotation.Rotate180,
                    5 or 6 => Rotation.Rotate90,
                    7 or 8 => Rotation.Rotate270,
                    _ => Rotation.Rotate0,
                };
            }
        }
        catch (Exception ex) when (IsDecodeError(ex))
        {
        }
        return Rotation.Rotate0;
    }

    private static bool IsDecodeError(Exception ex) =>
        ex is NotSupportedException or FileFormatException or IOException or ArgumentException
            or InvalidOperationException or COMException or OverflowException or UnauthorizedAccessException
            or OutOfMemoryException; // WIC reports absurd picture sizes this way
}
