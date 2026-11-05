using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Soundboard.App.Views;

/// <summary>
/// Opening and closing of the settings panel, starting from the gear at the top right: it grows from
/// 96 % at its top right corner, slides in a little from the right and fades in (200 ms, ease-out);
/// closing runs the way back in 140 ms and only then hides the panel.
/// Everything is a render transform and opacity, so the pads behind are not laid out again, and every
/// open animation ends on the base values (scale 1, shift 0, opacity 1): open, it looks exactly as without motion.
/// Without motion (not shown yet, keyboard, animations off) the panel is shown or hidden at once.
/// </summary>
public static class PanelMotion
{
    public const double StartScale = 0.96, StartShift = 12;
    private static readonly Duration Open = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration Close = new(TimeSpan.FromMilliseconds(140));
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    /// <summary>Counts the changes, so a close that is overtaken by a new open does not hide the panel.</summary>
    private static readonly DependencyProperty RunProperty = DependencyProperty.RegisterAttached(
        "Run", typeof(int), typeof(PanelMotion), new PropertyMetadata(0));

    private static IEasingFunction Frozen(EasingFunctionBase f)
    {
        f.Freeze();
        return f;
    }

    /// <summary>The scale and shift of the panel, set up once (anchor: top right corner).</summary>
    public static (ScaleTransform Scale, TranslateTransform Shift) Parts(UIElement panel)
    {
        if (panel.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] }) return (s, t);
        var scale = new ScaleTransform();
        var shift = new TranslateTransform();
        panel.RenderTransformOrigin = new Point(1, 0);
        panel.RenderTransform = new TransformGroup { Children = { scale, shift } };
        return (scale, shift);
    }

    /// <summary>
    /// Shows (<paramref name="open"/> = true) or hides the panel, moving when <paramref name="move"/> is true.
    /// A change mid-way starts from where the panel is seen now. Returns whether something moves.
    /// </summary>
    public static bool Show(UIElement panel, bool open, bool move)
    {
        var (scale, shift) = Parts(panel);
        int run = (int)panel.GetValue(RunProperty) + 1;
        panel.SetValue(RunProperty, run);
        bool shown = panel.Visibility == Visibility.Visible, moving = panel.HasAnimatedProperties;

        // Where the panel is seen now (mid-way through a change, or at rest).
        double s = scale.ScaleX, x = shift.X, o = panel.Opacity;
        if (!shown) (s, x, o) = (StartScale, StartShift, 0);
        Stop(panel, scale, shift);

        if (!move || (open ? shown && !moving : !shown))
        {
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            panel.IsHitTestVisible = true;
            return false;
        }

        if (open)
        {
            panel.Visibility = Visibility.Visible;
            panel.IsHitTestVisible = true;
            // From the seen values back to the base values; afterwards the base values hold again.
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, To(s, 1, Open, EaseOut, FillBehavior.Stop));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, To(s, 1, Open, EaseOut, FillBehavior.Stop));
            shift.BeginAnimation(TranslateTransform.XProperty, To(x, 0, Open, EaseOut, FillBehavior.Stop));
            panel.BeginAnimation(UIElement.OpacityProperty, To(o, 1, Open, EaseOut, FillBehavior.Stop));
            return true;
        }

        // Closing: no clicks into a panel on its way out; hide it at the end, then back to the base values.
        panel.IsHitTestVisible = false;
        var fade = To(o, 0, Close, EaseIn, FillBehavior.HoldEnd);
        fade.Completed += (_, _) =>
        {
            if ((int)panel.GetValue(RunProperty) != run) return;
            panel.Visibility = Visibility.Collapsed;
            panel.IsHitTestVisible = true;
            Stop(panel, scale, shift);
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, To(s, StartScale, Close, EaseIn, FillBehavior.HoldEnd));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, To(s, StartScale, Close, EaseIn, FillBehavior.HoldEnd));
        shift.BeginAnimation(TranslateTransform.XProperty, To(x, StartShift, Close, EaseIn, FillBehavior.HoldEnd));
        panel.BeginAnimation(UIElement.OpacityProperty, fade);
        return true;
    }

    private static void Stop(UIElement panel, ScaleTransform scale, TranslateTransform shift)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        panel.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private static DoubleAnimation To(double from, double to, Duration d, IEasingFunction ease, FillBehavior fill) =>
        new(from, to, d) { EasingFunction = ease, FillBehavior = fill };
}
