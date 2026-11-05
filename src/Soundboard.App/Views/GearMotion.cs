using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Soundboard.App.Themes;

namespace Soundboard.App.Views;

/// <summary>
/// The gear turns once by one tooth (45°, eight teeth) when the mouse comes over its button.
/// One tooth further looks the same as at rest, so the turn ends on the base value 0 without a jump,
/// and leaving the button lets a running turn finish instead of snapping back.
/// The turn sits on the button's content (a RotateTransform about its centre). Keyboard focus does not
/// turn it (only the mouse raises MouseEnter); animations off (setting or Windows): no turn.
/// </summary>
public static class GearMotion
{
    public const double Tooth = 45;
    private static readonly Duration Turn = new(TimeSpan.FromMilliseconds(320));
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GearMotion), new PropertyMetadata(false, OnIsEnabledChanged));

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

    /// <summary>
    /// Starts one turn (<paramref name="move"/> = true; a running turn is left to finish) or stops any turn
    /// so the gear stands at rest. Returns whether it turns.
    /// </summary>
    public static bool Play(ContentControl button, bool move)
    {
        if (button.Content is not UIElement { RenderTransform: RotateTransform turn }) return false;
        if (!move)
        {
            turn.BeginAnimation(RotateTransform.AngleProperty, null);
            return false;
        }
        if (turn.HasAnimatedProperties) return true;
        turn.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, Tooth, Turn) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        return true;
    }
}
