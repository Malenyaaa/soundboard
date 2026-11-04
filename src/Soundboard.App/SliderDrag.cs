using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Soundboard.App;

/// <summary>
/// Sliders that react like a fader: a click anywhere on the track puts the value there, and
/// without letting go the knob keeps following the mouse. (WPF's IsMoveToPointEnabled only
/// jumps; the drag would need a second click.) The keyboard works as before.
/// </summary>
public static class SliderDrag
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;
        slider.PreviewMouseLeftButtonDown -= OnDown;
        if (e.NewValue is true) slider.PreviewMouseLeftButtonDown += OnDown;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.Template?.FindName("PART_Track", slider) is not Track { Thumb: { } thumb } track) return;
        if (thumb.IsMouseOver) return; // on the knob: it drags by itself

        slider.Value = ValueAt(slider, track, e.GetPosition(track));
        slider.Focus();
        // Let the knob move under the mouse, then hand it the press: it captures the mouse and
        // drags from here on, exactly as if the knob had been grabbed.
        track.UpdateLayout();
        e.Handled = true;
        thumb.RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = thumb,
        });
    }

    /// <summary>The value under <paramref name="point"/>, rounded to the slider's small step.</summary>
    public static double ValueAt(Slider slider, Track track, Point point)
    {
        double v = track.ValueFromPoint(point);
        if (double.IsNaN(v) || double.IsInfinity(v)) v = slider.Value;
        double step = slider.SmallChange > 0 ? slider.SmallChange : 1;
        return Math.Clamp(Math.Round(v / step) * step, slider.Minimum, slider.Maximum);
    }
}
