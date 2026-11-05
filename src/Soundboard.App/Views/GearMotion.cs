using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Soundboard.App.Themes;

namespace Soundboard.App.Views;

/// <summary>
/// The gear turns by two teeth (90°, eight teeth) each time the mouse comes over its button,
/// slow at both ends so the turn is seen and does not just flicker. Two teeth further look the same
/// as at rest, so the turn ends on the base value 0 without a jump; leaving the button lets a running
/// turn finish instead of snapping back.
/// A finished turn is taken off the transform: an animation that has ended still counts as
/// "animated" for WPF, and the next mouse-over would otherwise take it for a running turn.
/// The turn sits on the button's content (a RotateTransform about its centre). Keyboard focus does not
/// turn it (only the mouse raises MouseEnter); animations off (setting or Windows): no turn.
/// </summary>
public static class GearMotion
{
    public const double Tooth = 45;
    public const double Angle = 2 * Tooth;
    private static readonly Duration Turn = new(TimeSpan.FromMilliseconds(400));
    private static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

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
        if ((bool)e.NewValue) button.MouseEnter += OnMouseEnter;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) => Play((ContentControl)sender, ThemeManager.AnimationsOn);

    /// <summary>Whether a turn is running on the button's symbol.</summary>
    public static bool IsTurning(ContentControl button) =>
        button.Content is UIElement { RenderTransform: RotateTransform turn } && turn.GetValue(RunningProperty) is not null;

    /// <summary>
    /// Starts one turn (<paramref name="move"/> = true; a running turn is left to finish) or stops any turn
    /// so the gear stands at rest. Returns whether it turns.
    /// </summary>
    public static bool Play(ContentControl button, bool move)
    {
        if (button.Content is not UIElement { RenderTransform: RotateTransform turn }) return false;
        if (!move)
        {
            Rest(turn);
            return false;
        }
        if (turn.GetValue(RunningProperty) is not null) return true;
        var clock = new DoubleAnimation(0, Angle, Turn) { EasingFunction = EaseInOut, FillBehavior = FillBehavior.Stop }.CreateClock();
        clock.Completed += (_, _) =>
        {
            if (ReferenceEquals(turn.GetValue(RunningProperty), clock)) Rest(turn);
        };
        turn.SetValue(RunningProperty, clock);
        turn.ApplyAnimationClock(RotateTransform.AngleProperty, clock);
        return true;
    }

    /// <summary>Takes the turn off: back to the base value 0, and the next mouse-over turns again.</summary>
    private static void Rest(RotateTransform turn)
    {
        turn.ClearValue(RunningProperty);
        turn.ApplyAnimationClock(RotateTransform.AngleProperty, null);
    }
}
