using System.Windows;
using System.Windows.Controls;

namespace Soundboard.App.Views;

/// <summary>
/// Lays the pads out like the grid of a pad sampler: as many columns as fit with at least
/// <see cref="MinPadWidth"/>, all pads equally wide and filling the row, almost square.
/// Hidden pads (search) take no place. <see cref="Compact"/> makes them smaller and flatter.
/// Favourites come first (the view model keeps that order); after the last one the row breaks and a
/// little more space follows, so they form a group of their own.
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

    /// <summary>Extra space between the favourites and the other pads.</summary>
    private double GroupGap => Compact ? 8 : 12;

    private static bool IsFavorite(UIElement child) =>
        child is FrameworkElement { DataContext: ViewModels.TileViewModel { IsFavorite: true } };

    /// <summary>
    /// Slot (row, column) of every shown pad, with the row break after the last favourite.
    /// Returns the number of rows and whether there is a favourite group with pads after it.
    /// </summary>
    private (int Rows, bool Split) Slots(int cols, Action<UIElement, int, int>? place)
    {
        int row = 0, col = 0, rows = 0;
        bool anyFavorite = false, split = false;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            bool fav = IsFavorite(child);
            if (fav) anyFavorite = true;
            else if (anyFavorite && !split)
            {
                split = true;
                if (col != 0) { row++; col = 0; }
            }
            place?.Invoke(child, row, col);
            rows = row + 1;
            if (++col == cols) { row++; col = 0; }
        }
        return (rows, split);
    }

    /// <summary>First row after the favourites (for the extra space), or -1.</summary>
    private int FirstRowAfterFavorites(int cols)
    {
        int found = -1;
        bool sawFavorite = false;
        Slots(cols, (child, row, _) =>
        {
            if (IsFavorite(child)) sawFavorite = true;
            else if (found < 0 && sawFavorite) found = row;
        });
        return found;
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, w, h) = Grid(available.Width);
        foreach (UIElement child in InternalChildren) child.Measure(new Size(w, h));
        var (rows, split) = Slots(cols, null);
        double gap = CurrentGap;
        double width = double.IsInfinity(available.Width) ? cols * w + (cols - 1) * gap : available.Width;
        double height = rows == 0 ? 0 : rows * h + (rows - 1) * gap + (split ? GroupGap : 0);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, w, h) = Grid(final.Width);
        double gap = CurrentGap;
        int splitRow = FirstRowAfterFavorites(cols);
        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Arrange(new Rect(0, 0, 0, 0));
        Slots(cols, (child, r, c) =>
        {
            double extra = splitRow >= 0 && r >= splitRow ? GroupGap : 0;
            child.Arrange(new Rect(c * (w + gap), r * (h + gap) + extra, w, h));
        });
        return final;
    }
}
