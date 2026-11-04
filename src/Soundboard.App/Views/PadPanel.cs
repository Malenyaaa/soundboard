using System.Windows;
using System.Windows.Controls;

namespace Soundboard.App.Views;

/// <summary>
/// Lays the pads out like the grid of a pad sampler: as many columns as fit with at least
/// <see cref="MinPadWidth"/>, all pads equally wide and filling the row, almost square.
/// </summary>
public sealed class PadPanel : Panel
{
    public static readonly DependencyProperty MinPadWidthProperty = DependencyProperty.Register(
        nameof(MinPadWidth), typeof(double), typeof(PadPanel),
        new FrameworkPropertyMetadata(144.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(PadPanel),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

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

    /// <summary>Height = width × this, at most <see cref="MaxPadHeight"/>.</summary>
    public double Aspect { get; set; } = 0.86;

    public double MaxPadHeight { get; set; } = 168;

    private (int Cols, double W, double H) Grid(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 4 * MinPadWidth + 3 * Gap;
        int cols = Math.Max(1, (int)Math.Floor((width + Gap) / (MinPadWidth + Gap)));
        double w = Math.Floor((width - Gap * (cols - 1)) / cols);
        double h = Math.Min(MaxPadHeight, Math.Round(w * Aspect));
        return (cols, w, h);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, w, h) = Grid(available.Width);
        foreach (UIElement child in InternalChildren) child.Measure(new Size(w, h));
        int rows = (InternalChildren.Count + cols - 1) / cols;
        double width = double.IsInfinity(available.Width) ? cols * w + (cols - 1) * Gap : available.Width;
        return new Size(width, rows == 0 ? 0 : rows * h + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, w, h) = Grid(final.Width);
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int r = i / cols, c = i % cols;
            InternalChildren[i].Arrange(new Rect(c * (w + Gap), r * (h + Gap), w, h));
        }
        return final;
    }
}
