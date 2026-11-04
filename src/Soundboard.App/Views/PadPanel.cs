using System.Windows;
using System.Windows.Controls;

namespace Soundboard.App.Views;

/// <summary>
/// Lays the pads out like the grid of a pad sampler: as many columns as fit with at least
/// <see cref="MinPadWidth"/>, all pads equally wide and filling the row, almost square.
/// Hidden pads (search) take no place. <see cref="Compact"/> makes them smaller and flatter.
/// </summary>
public sealed class PadPanel : Panel
{
    public static readonly DependencyProperty MinPadWidthProperty = DependencyProperty.Register(
        nameof(MinPadWidth), typeof(double), typeof(PadPanel),
        new FrameworkPropertyMetadata(144.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(PadPanel),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(PadPanel),
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

    /// <summary>Compact pads: narrower minimum, flatter, lower.</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Height = width × this, at most <see cref="MaxPadHeight"/>.</summary>
    public double Aspect { get; set; } = 0.86;

    public double MaxPadHeight { get; set; } = 168;

    private (int Cols, double W, double H) Grid(double width)
    {
        double min = Compact ? 128 : MinPadWidth;
        double gap = Compact ? 8 : Gap;
        if (double.IsInfinity(width) || width <= 0) width = 4 * min + 3 * gap;
        int cols = Math.Max(1, (int)Math.Floor((width + gap) / (min + gap)));
        double w = Math.Floor((width - gap * (cols - 1)) / cols);
        double h = Compact ? Math.Min(100, Math.Max(88, Math.Round(w * 0.68))) : Math.Min(MaxPadHeight, Math.Round(w * Aspect));
        return (cols, w, h);
    }

    private double CurrentGap => Compact ? 8 : Gap;

    protected override Size MeasureOverride(Size available)
    {
        var (cols, w, h) = Grid(available.Width);
        int shown = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(w, h));
            if (child.Visibility != Visibility.Collapsed) shown++;
        }
        int rows = (shown + cols - 1) / cols;
        double gap = CurrentGap;
        double width = double.IsInfinity(available.Width) ? cols * w + (cols - 1) * gap : available.Width;
        return new Size(width, rows == 0 ? 0 : rows * h + (rows - 1) * gap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, w, h) = Grid(final.Width);
        double gap = CurrentGap;
        int i = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            int r = i / cols, c = i % cols;
            child.Arrange(new Rect(c * (w + gap), r * (h + gap), w, h));
            i++;
        }
        return final;
    }
}
