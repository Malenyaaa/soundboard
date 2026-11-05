using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Soundboard.App.Themes;

namespace Soundboard.App.Views;

/// <summary>
/// Motion of the "Switch" style: when the value changes after the switch is shown, the knob slides
/// to its new side and track and knob blend from the old colours to the new ones.
/// The resting look stays with the template triggers; this only plays the way from the old look to
/// the new one (knob shifted back to where it was, layers with the old colours fading out), so every
/// animation ends at the base values and the switch looks the same with or without motion.
/// No motion while the switch is not shown yet, or when animations are off (setting or Windows).
/// </summary>
public static class SwitchMotion
{
    private static readonly Duration Slide = new(TimeSpan.FromMilliseconds(170));
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SwitchMotion), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static IEasingFunction Frozen(EasingFunctionBase f)
    {
        f.Freeze();
        return f;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton toggle) return;
        toggle.Checked -= OnToggled;
        toggle.Unchecked -= OnToggled;
        if (!(bool)e.NewValue) return;
        toggle.Checked += OnToggled;
        toggle.Unchecked += OnToggled;
    }

    private static void OnToggled(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        Play(toggle, toggle.IsChecked == true, toggle.IsLoaded && toggle.IsVisible && ThemeManager.AnimationsOn);
    }

    /// <summary>
    /// Plays the change to <paramref name="on"/> (<paramref name="move"/> = true) or stops any motion so
    /// the switch stands at its new value at once. Returns whether something moves.
    /// </summary>
    public static bool Play(ToggleButton toggle, bool on, bool move)
    {
        var template = toggle.Template;
        if (template?.FindName("KnobMove", toggle) is not TranslateTransform shift
            || template.FindName("Knob", toggle) is not FrameworkElement knob
            || template.FindName("Rail", toggle) is not FrameworkElement rail
            || template.FindName("WasOff", toggle) is not UIElement wasOff
            || template.FindName("WasOn", toggle) is not UIElement wasOn
            || template.FindName("KnobWasOff", toggle) is not UIElement knobWasOff
            || template.FindName("KnobWasOn", toggle) is not UIElement knobWasOn)
            return false;

        // The way the knob travels: rail minus knob minus the insets on both sides.
        double travel = rail.ActualWidth - knob.ActualWidth - knob.Margin.Left - knob.Margin.Right;
        if (!move || travel <= 0)
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            foreach (var layer in new[] { wasOff, wasOn, knobWasOff, knobWasOn }) layer.BeginAnimation(UIElement.OpacityProperty, null);
            return false;
        }

        // The triggers already show the new side; start from where the knob is seen now (also mid-way).
        double from = shift.X + (on ? -travel : travel);
        shift.BeginAnimation(TranslateTransform.XProperty, Ease(from));

        // The old colours lie on top and fade out. Mid-way through a change, start from the share still shown.
        var (oldTrack, oldKnob, otherTrack, otherKnob) = on ? (wasOff, knobWasOff, wasOn, knobWasOn) : (wasOn, knobWasOn, wasOff, knobWasOff);
        double shown = 1 - otherTrack.Opacity;
        otherTrack.BeginAnimation(UIElement.OpacityProperty, null);
        otherKnob.BeginAnimation(UIElement.OpacityProperty, null);
        oldTrack.BeginAnimation(UIElement.OpacityProperty, Ease(shown));
        oldKnob.BeginAnimation(UIElement.OpacityProperty, Ease(shown));
        return true;
    }

    /// <summary>From a value back to the base value 0; afterwards the base value holds again.</summary>
    private static DoubleAnimation Ease(double from) =>
        new(from, 0, Slide) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
}
