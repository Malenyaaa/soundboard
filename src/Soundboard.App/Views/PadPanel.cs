using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soundboard.App.Views;

/// <summary>
/// Lays the pads out like the grid of a pad sampler: as many columns as fit with at least
/// <see cref="MinPadWidth"/>, all pads equally wide and filling the row, almost square.
/// With <see cref="Fill"/> (full screen) all pads share the whole area instead: the grid is
/// chosen so the pads become as large as possible, and each pad is scaled as a whole, so its
/// text grows with it.
/// </summary>
public sealed class PadPanel : Panel
{
    public static readonly DependencyProperty MinPadWidthProperty = DependencyProperty.Register(
        nameof(MinPadWidth), typeof(double), typeof(PadPanel),
        new FrameworkPropertyMetadata(144.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(PadPanel),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(bool), typeof(PadPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinPadWidth
    {
        get => (double)GetValue(MinPadWidthProperty);
        set => SetValue(MinPadWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Full screen: the pads fill the available area.</summary>
    public bool Fill
    {
        get => (bool)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Height = width × this, at most <see cref="MaxPadHeight"/>.</summary>
    public double Aspect { get; set; } = 0.86;

    public double MaxPadHeight { get; set; } = 168;

    /// <summary>Size a pad is laid out at before it is scaled in full screen.</summary>
    public const double FillBaseWidth = 190;

    /// <summary>Largest scale in full screen (one or two pads on a big screen stay sensible).</summary>
    public const double FillMaxScale = 3.2;

    private (int Cols, double W, double H) Grid(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 4 * MinPadWidth + 3 * Gap;
        int cols = Math.Max(1, (int)Math.Floor((width + Gap) / (MinPadWidth + Gap)));
        double w = Math.Floor((width - Gap * (cols - 1)) / cols);
        double h = Math.Min(MaxPadHeight, Math.Round(w * Aspect));
        return (cols, w, h);
    }

    /// <summary>
    /// Full screen grid for <paramref name="count"/> pads in <paramref name="area"/>: columns, rows,
    /// the height of a pad laid out at <see cref="FillBaseWidth"/> (between 0.6 and 0.95 of its
    /// width, following the shape of the screen) and its scale — chosen for the largest pads.
    /// </summary>
    public static (int Cols, int Rows, double BaseHeight, double Scale) FillGrid(int count, Size area, double gap)
    {
        double w0 = FillBaseWidth;
        if (count <= 0 || area.Width <= 0 || area.Height <= 0 || double.IsInfinity(area.Width) || double.IsInfinity(area.Height))
            return (Math.Max(1, count), 1, Math.Round(w0 * 0.86), 1);
        (int Cols, int Rows, double H0, double S, double Area) best = (count, 1, w0, 0, -1);
        for (int cols = 1; cols <= count; cols++)
        {
            int rows = (count + cols - 1) / cols;
            double aspect = Math.Clamp(area.Height / rows / (area.Width / cols), 0.6, 0.95);
            double h0 = Math.Round(w0 * aspect);
            // the gaps are scaled with the pads, so they stay in proportion
            double s = Math.Min(area.Width / (cols * w0 + (cols - 1) * gap), area.Height / (rows * h0 + (rows - 1) * gap));
            double padArea = s * s * w0 * h0;
            if (padArea > best.Area + 1e-6) best = (cols, rows, h0, s, padArea);
        }
        return (best.Cols, best.Rows, best.H0, Math.Min(best.S, FillMaxScale));
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Fill && !double.IsInfinity(available.Height) && !double.IsInfinity(available.Width))
        {
            var (_, _, h0, _) = FillGrid(InternalChildren.Count, available, Gap);
            foreach (UIElement child in InternalChildren) child.Measure(new Size(FillBaseWidth, h0));
            return available;
        }
        var (cols, w, h) = Grid(available.Width);
        foreach (UIElement child in InternalChildren) child.Measure(new Size(w, h));
        int rows = (InternalChildren.Count + cols - 1) / cols;
        double width = double.IsInfinity(available.Width) ? cols * w + (cols - 1) * Gap : available.Width;
        return new Size(width, rows == 0 ? 0 : rows * h + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Fill)
        {
            double w0 = FillBaseWidth;
            var (fcols, frows, h0, s) = FillGrid(InternalChildren.Count, final, Gap);
            double usedW = (fcols * w0 + (fcols - 1) * Gap) * s, usedH = (frows * h0 + (frows - 1) * Gap) * s;
            double x0 = Math.Max(0, (final.Width - usedW) / 2), y0 = Math.Max(0, (final.Height - usedH) / 2);
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                int r = i / fcols, c = i % fcols;
                var child = InternalChildren[i];
                // Laid out at the base size, then scaled as a whole around its top left corner.
                child.RenderTransform = new ScaleTransform(s, s);
                child.Arrange(new Rect(x0 + c * (w0 + Gap) * s, y0 + r * (h0 + Gap) * s, w0, h0));
            }
            return final;
        }

        var (cols, w, h) = Grid(final.Width);
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int r = i / cols, c = i % cols;
            var child = InternalChildren[i];
            if (child.RenderTransform is ScaleTransform) child.RenderTransform = Transform.Identity;
            child.Arrange(new Rect(c * (w + Gap), r * (h + Gap), w, h));
        }
        return final;
    }
}
