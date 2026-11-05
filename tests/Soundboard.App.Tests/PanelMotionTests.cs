using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soundboard.App.Views;

namespace Soundboard.App.Tests;

[TestClass]
public class PanelMotionTests
{
    internal static void OnSta(Action action)
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
    public void Gear_turns_forward_and_back_and_without_motion_stands_at_zero() => OnSta(() =>
    {
        var turn = new RotateTransform();
        var button = new Button { Content = new Border { RenderTransform = turn } };
        Assert.IsFalse(GearMotion.Turn(button, forward: true, move: false));
        Assert.IsFalse(turn.HasAnimatedProperties);
        Assert.AreEqual(0, turn.Angle);

        Assert.IsTrue(GearMotion.Turn(button, forward: true, move: true));
        Assert.IsTrue(turn.HasAnimatedProperties);
        Assert.IsTrue(GearMotion.IsTurning(button));
        Assert.AreEqual(GearMotion.Angle, turn.GetAnimationBaseValue(RotateTransform.AngleProperty), "forward ends on two teeth");

        // Left before the turn was seen at all: nothing to turn back, it stands at 0.
        Assert.IsFalse(GearMotion.Turn(button, forward: false, move: true));
        Assert.IsFalse(turn.HasAnimatedProperties);
        Assert.AreEqual(0, turn.Angle);
        Assert.IsTrue(GearMotion.Turn(button, forward: true, move: true));

        // Animations switched off mid-way: at rest at once, nothing left on the transform.
        Assert.IsFalse(GearMotion.Turn(button, forward: true, move: false));
        Assert.IsFalse(GearMotion.IsTurning(button));
        Assert.IsFalse(turn.HasAnimatedProperties);
        Assert.AreEqual(0, turn.Angle);
        // Eight teeth: a whole number of teeth further looks the same as at rest.
        Assert.AreEqual(0, GearMotion.Angle % GearMotion.Tooth);
        Assert.IsTrue(GearMotion.Angle >= 90);

        // Without a turnable symbol nothing happens.
        Assert.IsFalse(GearMotion.Turn(new Button { Content = "x" }, forward: true, move: true));
    });

    private static Window Invisible(UIElement content, double width, double height) => new()
    {
        Content = content,
        WindowStyle = WindowStyle.None,
        ShowInTaskbar = false,
        ShowActivated = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -30000,
        Top = -30000,
        Width = width,
        Height = height,
    };

    /// <summary>
    /// The turn really moves the symbol: forward to two teeth and it stays there while the mouse is on it,
    /// back to 0 on leaving; each time taken off at the end, so the next mouse-over turns again.
    /// Shown in an invisible window: the animation clock only ticks for something rendered.
    /// </summary>
    [TestMethod]
    public void Gear_turns_forward_stays_and_turns_back_again_and_again() => OnSta(() =>
    {
        var turn = new RotateTransform();
        var button = new Button { Content = new Border { Width = 20, Height = 20, RenderTransform = turn } };
        var window = Invisible(button, 60, 60);
        window.Show();
        try
        {
            for (int round = 0; round < 2; round++)
            {
                foreach (bool forward in new[] { true, false })
                {
                    double to = forward ? GearMotion.Angle : 0;
                    var angles = new List<double>();
                    EventHandler sample = (_, _) => angles.Add(turn.Angle);
                    CompositionTarget.Rendering += sample;
                    Assert.IsTrue(GearMotion.Turn(button, forward, move: true));
                    Pump(TimeSpan.FromMilliseconds(900), () => !GearMotion.IsTurning(button));
                    CompositionTarget.Rendering -= sample;

                    string what = $"round {round}, {(forward ? "forward" : "back")}";
                    Assert.IsFalse(GearMotion.IsTurning(button), $"{what}: ended");
                    Assert.IsFalse(turn.HasAnimatedProperties, $"{what}: taken off");
                    Assert.AreEqual(to, turn.Angle, what);
                    Assert.IsTrue(angles.Any(a => a > 10 && a < GearMotion.Angle - 10), $"{what}: seen in between ({string.Join(" ", angles)})");
                    // Already there: nothing to turn.
                    Assert.IsFalse(GearMotion.Turn(button, forward, move: true), $"{what}: no second turn");
                }
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Leaving mid-way turns back from the angle seen then, not from the end; coming back turns forward from there.</summary>
    [TestMethod]
    public void Gear_turns_round_from_the_angle_seen_mid_way() => OnSta(() =>
    {
        var turn = new RotateTransform();
        var button = new Button { Content = new Border { Width = 20, Height = 20, RenderTransform = turn } };
        var window = Invisible(button, 60, 60);
        window.Show();
        try
        {
            GearMotion.Turn(button, forward: true, move: true);
            Pump(TimeSpan.FromMilliseconds(500), () => turn.Angle > 30);
            double mid = turn.Angle;
            Assert.IsTrue(mid > 0 && mid < GearMotion.Angle, $"mid-way ({mid})");

            var angles = new List<double>();
            EventHandler sample = (_, _) => angles.Add(turn.Angle);
            CompositionTarget.Rendering += sample;
            Assert.IsTrue(GearMotion.Turn(button, forward: false, move: true));
            Pump(TimeSpan.FromMilliseconds(400), () => turn.Angle < mid / 2);
            CompositionTarget.Rendering -= sample;
            string list = string.Join(" ", angles);
            Assert.IsTrue(angles.Count > 0 && angles[0] > mid - 10, $"first frame near where it was, no jump ({mid}: {list})");
            Assert.IsTrue(angles.All(a => a <= mid + 0.001), $"turns back from where it was ({mid}: {list})");

            double back = turn.Angle;
            Assert.IsTrue(back > 0, $"still mid-way back ({back})");
            Assert.IsTrue(GearMotion.Turn(button, forward: true, move: true));
            // Left again in the same frame, before the turn has ticked: it starts from there, not from its end.
            Assert.IsTrue(GearMotion.Turn(button, forward: false, move: true));
            Assert.IsTrue(GearMotion.Turn(button, forward: true, move: true));
            angles.Clear();
            CompositionTarget.Rendering += sample;
            Pump(TimeSpan.FromMilliseconds(60), () => angles.Count > 0);
            CompositionTarget.Rendering -= sample;
            Assert.IsTrue(angles.Count > 0 && Math.Abs(angles[0] - back) < 10, $"no jump when coming back ({back}: {string.Join(" ", angles)})");
            Pump(TimeSpan.FromMilliseconds(900), () => !GearMotion.IsTurning(button));
            Assert.AreEqual(GearMotion.Angle, turn.Angle);
            Assert.IsFalse(turn.HasAnimatedProperties);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void Pump(TimeSpan max, Func<bool> done)
    {
        var until = DateTime.UtcNow + max;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            if (done() || DateTime.UtcNow > until)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
