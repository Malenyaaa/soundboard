using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soundboard.App.Views;

namespace Soundboard.App.Tests;

[TestClass]
public class SwitchMotionTests
{
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    /// <summary>A switch with the real style, laid out but not shown.</summary>
    private static (CheckBox Box, TranslateTransform Shift) NewSwitch(bool on)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var controls = (ResourceDictionary)Application.LoadComponent(new Uri("/Soundboard;component/Themes/Controls.xaml", UriKind.Relative));
        var box = new CheckBox { Style = (Style)controls["Switch"], IsChecked = on, Content = "Monitor" };
        box.Measure(new Size(200, 40));
        box.Arrange(new Rect(0, 0, 200, 40));
        box.UpdateLayout();
        return (box, (TranslateTransform)box.Template.FindName("KnobMove", box));
    }

    private static double[] Layers(CheckBox box) =>
        new[] { "WasOff", "WasOn", "KnobWasOff", "KnobWasOn" }.Select(n => ((UIElement)box.Template.FindName(n, box)).Opacity).ToArray();

    [TestMethod]
    public void Style_has_the_moving_knob_and_rests_without_motion() => OnSta(() =>
    {
        var (box, shift) = NewSwitch(on: true);
        Assert.IsTrue(SwitchMotion.GetIsEnabled(box));
        Assert.AreEqual(0, shift.X);
        Assert.AreEqual(HorizontalAlignment.Right, ((FrameworkElement)box.Template.FindName("Knob", box)).HorizontalAlignment);
        CollectionAssert.AreEqual(new double[] { 0, 0, 0, 0 }, Layers(box));

        // Not shown yet (as while loading): the value changes without any animation.
        box.IsChecked = false;
        Assert.IsFalse(shift.HasAnimatedProperties);
        Assert.AreEqual(HorizontalAlignment.Left, ((FrameworkElement)box.Template.FindName("Knob", box)).HorizontalAlignment);
    });

    [TestMethod]
    public void Animations_off_sets_at_once() => OnSta(() =>
    {
        var (box, shift) = NewSwitch(on: false);
        box.IsChecked = true;
        Assert.IsFalse(SwitchMotion.Play(box, on: true, move: false));
        Assert.IsFalse(shift.HasAnimatedProperties);
        Assert.AreEqual(0, shift.X);
        CollectionAssert.AreEqual(new double[] { 0, 0, 0, 0 }, Layers(box));
    });

    [TestMethod]
    public void Animations_on_slide_the_knob_and_fade_the_old_colours() => OnSta(() =>
    {
        var (box, shift) = NewSwitch(on: false);
        box.IsChecked = true;
        Assert.IsTrue(SwitchMotion.Play(box, on: true, move: true));
        Assert.IsTrue(shift.HasAnimatedProperties);
        Assert.IsTrue(((UIElement)box.Template.FindName("WasOff", box)).HasAnimatedProperties);
        Assert.IsTrue(((UIElement)box.Template.FindName("KnobWasOff", box)).HasAnimatedProperties);
        Assert.IsFalse(((UIElement)box.Template.FindName("WasOn", box)).HasAnimatedProperties);

        // Animations switched off mid-way: everything stops at the resting look.
        Assert.IsFalse(SwitchMotion.Play(box, on: true, move: false));
        Assert.AreEqual(0, shift.X);
        CollectionAssert.AreEqual(new double[] { 0, 0, 0, 0 }, Layers(box));
    });
}
