using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Soundboard.App.Views;

/// <summary>
/// The line under the open tab of a tab row: one line for the whole row (not one per tab), so on a change it
/// slides to the new tab and takes its width (200 ms, ease-out). It sits in a Canvas over the row and moves by
/// a TranslateTransform; its width is animated (a scaled line would stretch its round ends). A change mid-way
/// starts from where the line is seen now. When shown for the first time, after a resize or a language switch
/// (other tab widths), with the keyboard and with animations off it stands at the new place at once.
/// Every slide ends on the base values and is then taken off: an ended animation still counts as "animated" for WPF.
/// </summary>
public sealed class TabIndicator
{
    public static readonly Duration Slide = new(TimeSpan.FromMilliseconds(200));
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    private readonly Panel _tabs;
    private readonly FrameworkElement _line;
    private readonly TranslateTransform _shift;
    private bool _scheduled;
    private (double X, double Y, double Width)? _from;
    private int _run;

    /// <param name="tabs">The panel holding the tabs (toggle buttons; the checked one is the open tab).</param>
    /// <param name="line">The line, a child of a Canvas laid over <paramref name="tabs"/>.</param>
    public TabIndicator(Panel tabs, FrameworkElement line)
    {
        _tabs = tabs;
        _line = line;
        if (line.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            line.RenderTransform = shift;
        }
        _shift = shift;
        // Other tab widths (language, bold label of the open tab, text size) or another row break: follow at once.
        tabs.SizeChanged += (_, _) => Follow(move: false);
        foreach (var tab in tabs.Children.OfType<FrameworkElement>()) tab.SizeChanged += (_, _) => Follow(move: false);
    }

    private static IEasingFunction Frozen(EasingFunctionBase f)
    {
        f.Freeze();
        return f;
    }

    public TranslateTransform Shift => _shift;

    /// <summary>Whether the line is sliding right now.</summary>
    public bool IsMoving => _shift.HasAnimatedProperties || _line.HasAnimatedProperties;

    /// <summary>Where the line belongs under a tab with the given bounds: its bottom edge, its full width.</summary>
    public static (double X, double Y, double Width) Target(Rect tab, double lineHeight) => (tab.X, tab.Bottom - lineHeight, tab.Width);

    /// <summary>The open tab, or null.</summary>
    public FrameworkElement? OpenTab => _tabs.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true);

    /// <summary>
    /// Brings the line under the open tab once the row is laid out (several calls before that count once).
    /// <paramref name="move"/> = true: slide from where the line is seen now (call it when the tab changes,
    /// before the row is laid out again).
    /// </summary>
    public void Follow(bool move)
    {
        if (move && _from is null && _line.Visibility == Visibility.Visible) _from = Seen;
        if (_scheduled) return;
        _scheduled = true;
        _line.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _scheduled = false;
            var from = _from;
            _from = null;
            Place(from);
        });
    }

    private (double X, double Y, double Width) Seen => (_shift.X, _shift.Y, _line.Width is double w && !double.IsNaN(w) ? w : 0);

    private (double X, double Y, double Width) Base =>
        ((double)_shift.GetAnimationBaseValue(TranslateTransform.XProperty), (double)_shift.GetAnimationBaseValue(TranslateTransform.YProperty),
         (double)_line.GetAnimationBaseValue(FrameworkElement.WidthProperty));

    /// <summary>
    /// Puts the line under the open tab now: sliding from <paramref name="from"/> if given, else at once.
    /// Hidden while no tab is open or the row is not laid out. Returns whether it slides.
    /// </summary>
    public bool Place((double X, double Y, double Width)? from)
    {
        var to = Where();
        if (to is not { } t)
        {
            Stop();
            _line.Visibility = Visibility.Hidden;
            return false;
        }
        // Sliding already to the same place (a resize that did not change it): let it finish.
        if (from is null && IsMoving && Base == t) return true;
        var start = from ?? (IsMoving ? Seen : t);
        Stop();
        (_shift.X, _shift.Y, _line.Width) = t;
        _line.Visibility = Visibility.Visible;
        if (from is null || start == t) return false;

        int run = ++_run;
        var x = To(start.X, t.X);
        x.Completed += (_, _) =>
        {
            if (run == _run) Stop();
        };
        _shift.BeginAnimation(TranslateTransform.XProperty, x);
        _shift.BeginAnimation(TranslateTransform.YProperty, To(start.Y, t.Y));
        _line.BeginAnimation(FrameworkElement.WidthProperty, To(start.Width, t.Width));
        return true;
    }

    /// <summary>Base values of the line under the open tab, relative to the line's Canvas; null if not laid out.</summary>
    public (double X, double Y, double Width)? Where()
    {
        if (OpenTab is not { ActualWidth: > 0 } tab || VisualTreeHelper.GetParent(_line) is not Visual canvas) return null;
        try
        {
            var bounds = tab.TransformToVisual(canvas).TransformBounds(new Rect(0, 0, tab.ActualWidth, tab.ActualHeight));
            return Target(bounds, _line.Height is double h && !double.IsNaN(h) ? h : _line.ActualHeight);
        }
        catch (InvalidOperationException)
        {
            return null; // not in the same tree (yet)
        }
    }

    /// <summary>Takes a slide off; the base values hold.</summary>
    private void Stop()
    {
        _run++;
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.BeginAnimation(TranslateTransform.YProperty, null);
        _line.BeginAnimation(FrameworkElement.WidthProperty, null);
    }

    private static DoubleAnimation To(double from, double to) =>
        new(from, to, Slide) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
}
