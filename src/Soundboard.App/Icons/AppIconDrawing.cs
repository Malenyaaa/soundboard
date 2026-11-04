using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Soundboard.Core.Design;

namespace Soundboard.App.Icons;

/// <summary>
/// The app icon: a key cap seen from the front (accent face, darker thick lower edge) with a
/// white sample waveform on the face. 16, 20 and 24 px are drawn pixel by pixel (3, 4, 5 bars
/// on whole pixels); every other size comes from one formula (6 rounded bars).
/// The colours follow an accent: the face is the accent, the edge the same hue with
/// 0.10 less OKLCH lightness.
/// </summary>
public static class AppIconDrawing
{
    /// <summary>Sizes in the ICO file: tray at 100/125/150 %, small and large icons, Explorer.</summary>
    public static IReadOnlyList<int> IcoSizes { get; } = [16, 20, 24, 32, 48, 256];

    private static readonly Color White = Colors.White;

    /// <summary>Fixed face colour of the app symbol (dark orchid), the same for the EXE icon and the
    /// tray symbol and independent of the accent chosen in the settings. White bars on it: 5.6 : 1.</summary>
    public static Rgb DefaultAccent { get; } = Rgb.Parse("#9932CC");

    /// <summary>The lower edge of the cap: same hue, 0.10 darker in OKLCH, a little less chroma.</summary>
    public static Rgb EdgeFor(Rgb accent)
    {
        var o = ColorMath.ToOklch(accent);
        return ColorMath.FromOklch(new Oklch(Math.Max(0, o.L - 0.10), o.C * 0.95, o.H));
    }

    /// <summary>The icon for one pixel size as a vector drawing (bounds exactly size × size).</summary>
    public static DrawingGroup Drawing(int size, Rgb accent)
    {
        if (size < 16) throw new ArgumentOutOfRangeException(nameof(size), size, "at least 16 px");
        var face = ToColor(accent);
        var edge = ToColor(EdgeFor(accent));
        int n = size;
        var g = new DrawingGroup();
        // Transparent frame: keeps the bounds at n × n even though the cap leaves rows free at the top and bottom.
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, n, n))));

        double top = Top(n);
        double e = n switch { 16 => 3, 20 => 4, 24 => 4, _ => Math.Round(n * 0.17) };
        double r = n <= 16 ? 3 : Math.Round(n * 0.19);
        double h = n - 2 * top;
        Add(g, RoundedRect(0, top, n, h, r), edge);
        Add(g, RoundedRect(0, top, n, h - e, r), face);

        double faceTop = top, faceH = h - e;
        // Bars: width, gap and heights; pixel sizes for 16/20/24, fractions of the face otherwise.
        (double w, double gap, double[] hs) spec = n switch
        {
            16 => (2, 2, [5, 9, 3]),
            20 => (2, 2, [4, 10, 6, 2]),
            24 => (2, 2, [4, 8, 12, 6, 2]),
            _ => (n * 0.07, n * 0.065, [0.24, 0.48, 0.78, 0.36, 0.58, 0.16]),
        };
        bool pixel = n <= 24 && spec.hs[0] >= 1;
        int k = spec.hs.Length;
        double total = k * spec.w + (k - 1) * spec.gap;
        double x0 = Math.Round((n - total) / 2);
        double cy = faceTop + faceH / 2;
        for (int i = 0; i < k; i++)
        {
            double bh = pixel ? spec.hs[i] : Math.Round(spec.hs[i] * faceH * 0.95);
            double y = pixel ? Math.Floor(cy - bh / 2) : cy - bh / 2;
            double x = x0 + i * (spec.w + spec.gap);
            double rb = pixel ? 0 : spec.w / 2;
            Add(g, RoundedRect(x, y, spec.w, bh, rb), White);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The drawing as an image source (for WPF elements).</summary>
    public static DrawingImage Image(int size, Rgb accent)
    {
        var img = new DrawingImage(Drawing(size, accent));
        img.Freeze();
        return img;
    }

    /// <summary>The icon rendered to exactly size × size pixels (96 dpi, premultiplied BGRA). Needs an STA thread.</summary>
    public static BitmapSource Render(int size, Rgb accent)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawDrawing(Drawing(size, accent));
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Free rows above and below the cap.</summary>
    private static double Top(int size) => size <= 16 ? 1 : Math.Round(size * 0.06);

    /// <summary>A pixel row inside the lower edge of the cap (the row above its last one), for checks.</summary>
    public static int EdgeRow(int size) => size - 1 - (int)Top(size) - 1;

    private static void Add(DrawingGroup g, Geometry geo, Color fill)
    {
        var brush = new SolidColorBrush(fill);
        brush.Freeze();
        g.Children.Add(new GeometryDrawing(brush, null, geo));
    }

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Rounded rectangle as path data (radius limited to half the shorter side), the same
    /// arcs as the drawn drafts so the pixels match.
    /// </summary>
    private static Geometry RoundedRect(double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        string d = r <= 0
            ? $"M{F(x)},{F(y)} H{F(x + w)} V{F(y + h)} H{F(x)} Z"
            : $"M{F(x + r)},{F(y)} H{F(x + w - r)} A{F(r)},{F(r)} 0 0 1 {F(x + w)},{F(y + r)} V{F(y + h - r)} " +
              $"A{F(r)},{F(r)} 0 0 1 {F(x + w - r)},{F(y + h)} H{F(x + r)} A{F(r)},{F(r)} 0 0 1 {F(x)},{F(y + h - r)} " +
              $"V{F(y + r)} A{F(r)},{F(r)} 0 0 1 {F(x + r)},{F(y)} Z";
        var geo = Geometry.Parse(d);
        geo.Freeze();
        return geo;
    }
}
