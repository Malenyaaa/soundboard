using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soundboard.App.Views;

namespace Soundboard.App.Tests;

[TestClass]
public class PanelMotionTests
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

    private static void AssertAtRest(Border panel)
    {
        var (scale, shift) = PanelMotion.Parts(panel);
        Assert.IsFalse(scale.HasAnimatedProperties || shift.HasAnimatedProperties || panel.HasAnimatedProperties);
        Assert.AreEqual(1, scale.ScaleX);
        Assert.AreEqual(1, scale.ScaleY);
        Assert.AreEqual(0, shift.X);
        Assert.AreEqual(1, panel.Opacity);
        Assert.IsTrue(panel.IsHitTestVisible);
    }

    [TestMethod]
    public void Without_motion_the_panel_shows_and_hides_at_once() => OnSta(() =>
    {
        var panel = new Border { Visibility = Visibility.Collapsed };
        Assert.IsFalse(PanelMotion.Show(panel, open: true, move: false));
        Assert.AreEqual(Visibility.Visible, panel.Visibility);
        AssertAtRest(panel);
        Assert.AreEqual(new Point(1, 0), panel.RenderTransformOrigin);

        Assert.IsFalse(PanelMotion.Show(panel, open: false, move: false));
        Assert.AreEqual(Visibility.Collapsed, panel.Visibility);
        AssertAtRest(panel);
    });

    [TestMethod]
    public void Opening_moves_from_the_top_right_and_stopping_leaves_the_resting_look() => OnSta(() =>
    {
        var panel = new Border { Visibility = Visibility.Collapsed };
        Assert.IsTrue(PanelMotion.Show(panel, open: true, move: true));
        Assert.AreEqual(Visibility.Visible, panel.Visibility);
        var (scale, shift) = PanelMotion.Parts(panel);
        Assert.IsTrue(scale.HasAnimatedProperties && shift.HasAnimatedProperties && panel.HasAnimatedProperties);

        // Animations switched off mid-way: open at once, exactly at rest.
        Assert.IsFalse(PanelMotion.Show(panel, open: true, move: false));
        Assert.AreEqual(Visibility.Visible, panel.Visibility);
        AssertAtRest(panel);
    });

    [TestMethod]
    public void Closing_keeps_the_panel_until_the_end_and_takes_no_clicks() => OnSta(() =>
    {
        var panel = new Border { Visibility = Visibility.Visible };
        Assert.IsTrue(PanelMotion.Show(panel, open: false, move: true));
        Assert.AreEqual(Visibility.Visible, panel.Visibility);
        Assert.IsFalse(panel.IsHitTestVisible);

        // Opened again mid-way: visible, clickable, and it moves back to rest.
        Assert.IsTrue(PanelMotion.Show(panel, open: true, move: true));
        Assert.AreEqual(Visibility.Visible, panel.Visibility);
        Assert.IsTrue(panel.IsHitTestVisible);

        // Already closed: closing again changes nothing.
        Assert.IsFalse(PanelMotion.Show(panel, open: false, move: false));
        Assert.IsFalse(PanelMotion.Show(panel, open: false, move: true));
        Assert.AreEqual(Visibility.Collapsed, panel.Visibility);
        AssertAtRest(panel);
    });

    [TestMethod]
    public void Gear_turns_one_tooth_and_rests_at_zero() => OnSta(() =>
    {
        var turn = new RotateTransform();
        var button = new Button { Content = new Border { RenderTransform = turn } };
        Assert.IsFalse(GearMotion.Play(button, move: false));
        Assert.IsFalse(turn.HasAnimatedProperties);
        Assert.IsTrue(GearMotion.Play(button, move: true));
        Assert.IsTrue(turn.HasAnimatedProperties);
        Assert.IsTrue(GearMotion.Play(button, move: true)); // a running turn finishes, no restart
        Assert.IsFalse(GearMotion.Play(button, move: false));
        Assert.AreEqual(0, turn.Angle);
        Assert.AreEqual(45, GearMotion.Tooth); // eight teeth: one tooth further looks the same as at rest

        // Without a turnable symbol nothing happens.
        Assert.IsFalse(GearMotion.Play(new Button { Content = "x" }, move: true));
    });
}
