using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Soundboard.App.Themes;

namespace Soundboard.App.Views;

/// <summary>
/// The gear turns forward by two teeth (90°, eight teeth) while the mouse comes over its button and turns
/// back to 0° when the mouse leaves, slow at both ends so the turn is seen and does not just flicker.
/// Each turn starts from the angle seen now, so leaving or coming back mid-way turns round without a jump
/// (it then takes the remaining share of the time and starts at speed). At the end the angle becomes the base
/// value (90° while the mouse is on the button, which looks the same as at rest; 0° after leaving) and the
/// finished turn is taken off the transform: an animation that has ended still counts as "animated" for WPF.
/// The turn sits on the button's content (a RotateTransform about its centre). Keyboard focus does not
/// turn it (only the mouse raises MouseEnter/MouseLeave); animations off (setting or Windows): no turn, 0°.
/// </summary>
public static class GearMotion
{
    public const double Tooth = 45;
    public const double Angle = 2 * Tooth;
    public static readonly TimeSpan FullTurn = TimeSpan.FromMilliseconds(400);
    private static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GearMotion), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>The running turn of a transform; null at rest.</summary>
    private static readonly DependencyProperty RunningProperty = DependencyProperty.RegisterAttached(
        "Running", typeof(AnimationClock), typeof(GearMotion), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static IEasingFunction Frozen(EasingFunctionBase f)
    {
        f.Freeze();
        return f;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl button) return;
        button.MouseEnter -= OnMouseEnter;
        button.MouseLeave -= OnMouseLeave;
        if (!(bool)e.NewValue) return;
        button.MouseEnter += OnMouseEnter;
        button.MouseLeave += OnMouseLeave;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) => Turn((ContentControl)sender, forward: true, ThemeManager.AnimationsOn);

    private static void OnMouseLeave(object sender, MouseEventArgs e) => Turn((ContentControl)sender, forward: false, ThemeManager.AnimationsOn);

    /// <summary>Whether a turn is running on the button's symbol.</summary>
    public static bool IsTurning(ContentControl button) =>
        button.Content is UIElement { RenderTransform: RotateTransform turn } && turn.GetValue(RunningProperty) is not null;

    /// <summary>
    /// Turns forward to <see cref="Angle"/> (<paramref name="forward"/> = true) or back to 0°, from the angle seen now.
    /// Without motion (<paramref name="move"/> = false) the gear stands at 0° at once. Returns whether it turns.
    /// </summary>
    public static bool Turn(ContentControl button, bool forward, bool move)
    {
        if (button.Content is not UIElement { RenderTransform: RotateTransform turn }) return false;
        // Seen now: the running turn's value; a turn started in this very frame has not ticked yet and is still at its start.
        double seen = turn.GetValue(RunningProperty) is AnimationClock { CurrentTime: null, Timeline: DoubleAnimation { From: double start } } ? start : turn.Angle;
        double to = move && forward ? Angle : 0;
        Rest(turn, to);
        if (!move || seen == to) return false;

        // From rest a whole turn, slow at both ends; from mid-way the rest of it, already at speed.
        bool fromRest = seen is 0 or Angle;
        var time = TimeSpan.FromTicks((long)(FullTurn.Ticks * Math.Min(1, Math.Abs(to - seen) / Angle)));
        var clock = new DoubleAnimation(seen, to, new Duration(time))
        {
            EasingFunction = fromRest ? EaseInOut : EaseOut,
            FillBehavior = FillBehavior.Stop,
        }.CreateClock();
        clock.Completed += (_, _) =>
        {
            if (ReferenceEquals(turn.GetValue(RunningProperty), clock)) Rest(turn, to);
        };
        turn.SetValue(RunningProperty, clock);
        turn.ApplyAnimationClock(RotateTransform.AngleProperty, clock);
        return true;
    }

    /// <summary>Takes any turn off and lets the gear stand at <paramref name="angle"/> (the base value).</summary>
    private static void Rest(RotateTransform turn, double angle)
    {
        turn.ClearValue(RunningProperty);
        turn.ApplyAnimationClock(RotateTransform.AngleProperty, null);
        turn.Angle = angle;
    }
}
