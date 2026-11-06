using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Soundboard.App.Views;

/// <summary>
/// Lays the pads out like the grid of a pad sampler: as many columns as fit with at least
/// <see cref="MinPadWidth"/>, all pads equally wide and filling the row, almost square.
/// Hidden pads (search) take no place. <see cref="Compact"/> makes them smaller and flatter.
/// Favourites come first (the view model keeps that order). When a favourite is shown, each group
/// gets a quiet head line: its name in small text and a thin rule to the right edge
/// (<see cref="FavoritesHead"/>, <see cref="OthersHead"/>); without favourites there are no heads.
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

    public static readonly DependencyProperty FavoritesHeadProperty = DependencyProperty.Register(
        nameof(FavoritesHead), typeof(string), typeof(PadPanel),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OthersHeadProperty = DependencyProperty.Register(
        nameof(OthersHead), typeof(string), typeof(PadPanel),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HeadBrushProperty = DependencyProperty.Register(
        nameof(HeadBrush), typeof(Brush), typeof(PadPanel),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RuleBrushProperty = DependencyProperty.Register(
        nameof(RuleBrush), typeof(Brush), typeof(PadPanel),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

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

    /// <summary>Head of the favourites ("Favourites").</summary>
    public string FavoritesHead
    {
        get => (string)GetValue(FavoritesHeadProperty);
        set => SetValue(FavoritesHeadProperty, value);
    }

    /// <summary>Head of the pads after the favourites ("Other pads").</summary>
    public string OthersHead
    {
        get => (string)GetValue(OthersHeadProperty);
        set => SetValue(OthersHeadProperty, value);
    }

    /// <summary>Colour of the head text.</summary>
    public Brush HeadBrush
    {
        get => (Brush)GetValue(HeadBrushProperty);
        set => SetValue(HeadBrushProperty, value);
    }

    /// <summary>Colour of the thin rule after the head text.</summary>
    public Brush RuleBrush
    {
        get => (Brush)GetValue(RuleBrushProperty);
        set => SetValue(RuleBrushProperty, value);
    }

    /// <summary>Height = width × this, at most <see cref="MaxPadHeight"/>.</summary>
    public double Aspect { get; set; } = 0.86;

    public double MaxPadHeight { get; set; } = 168;

    /// <summary>Height of a head line (small text).</summary>
    public const double HeadHeight = 18;

    /// <summary>Space between a head line and its pads.</summary>
    public const double HeadGap = 8;

    private const double HeadFontSize = 12;

    /// <summary>Tops of the head lines of the last arrangement; NaN = not shown.</summary>
    private double _favoritesHeadTop = double.NaN, _othersHeadTop = double.NaN;

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

    /// <summary>Extra space before the head of the pads after the favourites.</summary>
    private double GroupGap => Compact ? 12 : 16;

    private static bool IsFavorite(UIElement child) =>
        child is FrameworkElement { DataContext: ViewModels.TileViewModel { IsFavorite: true } };

    /// <summary>
    /// Box of every shown pad (in order) and the tops of the head lines. Heads appear only when a
    /// favourite is shown: one above the favourites and one above the pads after them (if any).
    /// Returns the height used.
    /// </summary>
    private double Layout(double width, Action<UIElement, Rect>? place, out double favoritesHead, out double othersHead)
    {
        var (cols, w, h) = Grid(width);
        double gap = CurrentGap;
        favoritesHead = othersHead = double.NaN;
        bool heads = false;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed && IsFavorite(child))
            {
                heads = true;
                break;
            }
        }

        double y = 0, bottom = 0;
        int col = 0;
        bool started = false, others = false;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (heads && !started)
            {
                favoritesHead = 0;
                y = HeadHeight + HeadGap;
            }
            if (heads && !others && !IsFavorite(child))
            {
                // The favourites' last row ends here; then the head of the others.
                y = (started ? bottom + GroupGap : y);
                col = 0;
                othersHead = y;
                y += HeadHeight + HeadGap;
                others = true;
            }
            started = true;
            place?.Invoke(child, new Rect(col * (w + gap), y, w, h));
            bottom = y + h;
            if (++col == cols)
            {
                col = 0;
                y += h + gap;
            }
        }
        return started ? bottom : 0;
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, w, h) = Grid(available.Width);
        foreach (UIElement child in InternalChildren) child.Measure(new Size(w, h));
        double gap = CurrentGap;
        double width = double.IsInfinity(available.Width) ? cols * w + (cols - 1) * gap : available.Width;
        return new Size(width, Layout(width, null, out _, out _));
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Arrange(new Rect(0, 0, 0, 0));
        Layout(final.Width, (child, rect) => child.Arrange(rect), out double fav, out double others);
        if (!fav.Equals(_favoritesHeadTop) || !others.Equals(_othersHeadTop))
        {
            _favoritesHeadTop = fav;
            _othersHeadTop = others;
            InvalidateVisual();
        }
        return final;
    }

    /// <summary>Top of the favourites' head line and of the others' (NaN = not shown), after layout.</summary>
    public (double Favorites, double Others) HeadTops => (_favoritesHeadTop, _othersHeadTop);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawHead(dc, _favoritesHeadTop, FavoritesHead);
        DrawHead(dc, _othersHeadTop, OthersHead);
    }

    private void DrawHead(DrawingContext dc, double top, string text)
    {
        if (double.IsNaN(top) || string.IsNullOrEmpty(text) || ActualWidth <= 0) return;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), HeadFontSize, HeadBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            MaxTextWidth = Math.Max(1, ActualWidth - 24),
        };
        dc.DrawText(ft, new Point(0, top + (HeadHeight - ft.Height) / 2));
        double x = Math.Round(ft.WidthIncludingTrailingWhitespace + 10);
        if (x >= ActualWidth) return;
        // A hairline on whole pixels, level with the middle of the small letters.
        double lineY = Math.Round(top + HeadHeight / 2 + 1) + 0.5;
        var pen = new Pen(RuleBrush, 1);
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, lineY), new Point(ActualWidth, lineY));
    }
}
