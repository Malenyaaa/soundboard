using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Soundboard.App.Views;

/// <summary>
/// Lays the pads out like the grid of a pad sampler: as many columns as fit with at least
/// <see cref="MinPadWidth"/>, all pads equally wide and filling the row, almost square.
/// Hidden children (search, collapsed group) take no place. <see cref="Compact"/> makes the pads smaller and flatter.
/// A group head (a child whose data is a <see cref="ViewModels.CategoryViewModel"/>) takes a row of its own over
/// the full width and starts a new row of pads; before every head but the first there is a little more space.
///
/// Dragging: <see cref="BeginDrag"/> lifts one child out of the order and puts it where <see cref="SetDropBefore"/>
/// says; the others make room. The lifted child is drawn at <see cref="DragTopLeft"/> (under the mouse).
/// Whenever children change place – while dragging, after a drop, a move by key, a favourite – they glide from the
/// old place to the new one instead of jumping (only when animations are on and the grid kept its size).
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

    /// <summary>Height of a head row (arrow and name, 28 px to click).</summary>
    public const double HeadHeight = 28;

    /// <summary>Space between a head and its pads.</summary>
    public const double HeadGap = 4;

    /// <summary>How long a child glides to its new place.</summary>
    public static readonly Duration GlideTime = new(TimeSpan.FromMilliseconds(200));

    /// <summary>Glide even when Windows animations are off (tests); null = follow the setting.</summary>
    public bool? AnimateOverride { get; set; }

    private bool Animate => AnimateOverride ?? Themes.ThemeManager.AnimationsOn;

    private UIElement? _dragged, _dropBefore, _settling;
    private readonly HashSet<UIElement> _tucked = [];
    private Point _dragTopLeft;
    private readonly Dictionary<UIElement, Rect> _last = [];
    private (double Width, double PadW, double PadH) _lastGrid;

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

    /// <summary>Extra space before a head that follows other children.</summary>
    private double GroupGap => Compact ? 12 : 16;

    public static bool IsHead(UIElement child) => child is FrameworkElement { DataContext: ViewModels.CategoryViewModel };

    /// <summary>The children in the order they are laid out: the dragged one at its drop place.</summary>
    public List<UIElement> Order()
    {
        var list = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
            if (!ReferenceEquals(child, _dragged)) list.Add(child);
        if (_dragged is not null && InternalChildren.Contains(_dragged))
        {
            int at = _dropBefore is null ? -1 : list.IndexOf(_dropBefore);
            list.Insert(at < 0 ? list.Count : at, _dragged);
        }
        return list;
    }

    /// <summary>Box of every shown child in layout order. Returns the height used.</summary>
    private double Layout(double width, Action<UIElement, Rect> place)
    {
        var (cols, w, h) = Grid(width);
        double gap = CurrentGap;
        double y = 0, bottom = 0;
        int col = 0;
        bool started = false;
        foreach (var child in Order())
        {
            if (child.Visibility == Visibility.Collapsed || _tucked.Contains(child)) continue;
            if (IsHead(child))
            {
                if (started) y = bottom + GroupGap;
                place(child, new Rect(0, y, width, HeadHeight));
                bottom = y + HeadHeight;
                y = bottom + HeadGap;
                col = 0;
                started = true;
                continue;
            }
            if (col == 0 && started && bottom > y) y = bottom + gap; // a new row below the last one
            place(child, new Rect(col * (w + gap), y, w, h));
            bottom = y + h;
            started = true;
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
        double gap = CurrentGap;
        double width = double.IsInfinity(available.Width) ? cols * w + (cols - 1) * gap : available.Width;
        foreach (UIElement child in InternalChildren) child.Measure(IsHead(child) ? new Size(width, HeadHeight) : new Size(w, h));
        return new Size(width, Layout(width, (_, _) => { }));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (_, w, h) = Grid(final.Width);
        // Glide only when the grid kept its size (a resize or another pad size just lays out anew).
        bool glide = Animate && _lastGrid == (final.Width, w, h);
        _lastGrid = (final.Width, w, h);
        var shown = new HashSet<UIElement>();
        Layout(final.Width, (child, rect) =>
        {
            shown.Add(child);
            child.Arrange(rect);
            Place(child, rect, glide);
        });
        foreach (UIElement child in InternalChildren)
        {
            if (shown.Contains(child)) continue;
            child.Arrange(new Rect(0, 0, 0, 0));
            _last.Remove(child); // comes back where it is laid out, without gliding from an old place
            if (ReferenceEquals(child, _settling) || ReferenceEquals(child, _dragged))
            {
                // Dropped into a collapsed group: no drag offset may stay for when the group opens again.
                Still(Shift(child));
                child.ClearValue(ZIndexProperty);
            }
        }
        foreach (var gone in _last.Keys.Where(k => !InternalChildren.Contains(k)).ToList()) _last.Remove(gone);
        _settling = null;
        return final;
    }

    /// <summary>After a child is arranged at <paramref name="rect"/>: follow the mouse, or glide from where it was.</summary>
    private void Place(UIElement child, Rect rect, bool glide)
    {
        var shift = Shift(child);
        if (ReferenceEquals(child, _dragged))
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.X = _dragTopLeft.X - rect.X;
            shift.Y = _dragTopLeft.Y - rect.Y;
        }
        else if (_last.TryGetValue(child, out var old) && (old.TopLeft != rect.TopLeft || ReferenceEquals(child, _settling)))
        {
            // Where it is seen now (old place plus a glide still running), relative to the new place.
            double fromX = old.X - rect.X + shift.X, fromY = old.Y - rect.Y + shift.Y;
            if (glide || ReferenceEquals(child, _settling) && Animate) GlideFrom(child, shift, fromX, fromY);
            else
            {
                shift.BeginAnimation(TranslateTransform.XProperty, null);
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.X = shift.Y = 0;
            }
        }
        _last[child] = rect;
    }

    private void GlideFrom(UIElement child, TranslateTransform shift, double fromX, double fromY)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var x = new DoubleAnimation(fromX, 0, GlideTime) { EasingFunction = ease };
        var y = new DoubleAnimation(fromY, 0, GlideTime) { EasingFunction = ease };
        // A dropped pad stays on top until it has landed; every glide ends that (also one that replaced the first).
        y.Completed += (_, _) =>
        {
            if (!ReferenceEquals(child, _dragged)) child.ClearValue(ZIndexProperty);
        };
        shift.BeginAnimation(TranslateTransform.XProperty, x);
        shift.BeginAnimation(TranslateTransform.YProperty, y);
    }

    /// <summary>Stops a glide and holds the shift at where it is seen now (a finished glide would otherwise pin it at 0).</summary>
    private static void Hold(TranslateTransform shift)
    {
        double x = shift.X, y = shift.Y;
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.X = x;
        shift.Y = y;
    }

    private static void Still(TranslateTransform shift)
    {
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.X = shift.Y = 0;
    }

    private static TranslateTransform Shift(UIElement child)
    {
        if (child.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
        t = new TranslateTransform();
        child.RenderTransform = t;
        return t;
    }

    // ---------------------------------------------------------------- dragging

    /// <summary>The child being dragged, null while none is.</summary>
    public UIElement? Dragged => _dragged;

    /// <summary>Lifts <paramref name="child"/> out of the order: it stays where it is until <see cref="SetDropBefore"/>.</summary>
    public void BeginDrag(UIElement child)
    {
        var order = Order();
        int i = order.IndexOf(child);
        _dragged = child;
        _dropBefore = i >= 0 && i + 1 < order.Count ? order[i + 1] : null;
        // From where it is seen (also mid-glide); from now on only the mouse moves it.
        var shift = Shift(child);
        Hold(shift);
        _dragTopLeft = _last.TryGetValue(child, out var r) ? new Point(r.X + shift.X, r.Y + shift.Y) : new Point();
        SetZIndex(child, 100);
    }

    /// <summary>Where the dragged child would go: in front of <paramref name="before"/> (null = at the very end).</summary>
    public void SetDropBefore(UIElement? before)
    {
        if (_dragged is null || ReferenceEquals(before, _dropBefore) || ReferenceEquals(before, _dragged)) return;
        _dropBefore = before;
        InvalidateArrange();
    }

    /// <summary>Top left of the dragged child in this panel's coordinates (it follows the mouse).</summary>
    public Point DragTopLeft
    {
        get => _dragTopLeft;
        set
        {
            _dragTopLeft = value;
            if (_dragged is null || !_last.TryGetValue(_dragged, out var r)) return;
            var shift = Shift(_dragged);
            shift.X = value.X - r.X;
            shift.Y = value.Y - r.Y;
        }
    }

    /// <summary>
    /// Lets go of the dragged child: it is laid out in the order of the children again (after a drop the caller has
    /// moved it there already) and glides from under the mouse into its place.
    /// </summary>
    public void EndDrag()
    {
        if (_dragged is not { } child) return;
        _dragged = null;
        _dropBefore = null;
        _settling = child;
        if (!Animate) child.ClearValue(ZIndexProperty);
        InvalidateArrange();
    }

    /// <summary>While a category is dragged its pads take no place (as if the group were collapsed).</summary>
    public void Tuck(IEnumerable<UIElement> children)
    {
        _tucked.Clear();
        _tucked.UnionWith(children);
        InvalidateMeasure();
    }

    public void Untuck()
    {
        if (_tucked.Count == 0) return;
        _tucked.Clear();
        InvalidateMeasure();
    }

    /// <summary>Box of every shown child after the last layout (panel coordinates), in layout order.</summary>
    public IReadOnlyList<(UIElement Child, Rect Box)> Boxes() =>
        Order().Where(c => c.Visibility != Visibility.Collapsed && !_tucked.Contains(c) && _last.ContainsKey(c)).Select(c => (c, _last[c])).ToList();
}
