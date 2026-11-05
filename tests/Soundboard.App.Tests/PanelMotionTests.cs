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
    public void Gear_turns_two_teeth_and_rests_at_zero() => OnSta(() =>
    {
        var turn = new RotateTransform();
        var button = new Button { Content = new Border { RenderTransform = turn } };
        Assert.IsFalse(GearMotion.Play(button, move: false));
        Assert.IsFalse(turn.HasAnimatedProperties);
        Assert.IsTrue(GearMotion.Play(button, move: true));
        Assert.IsTrue(turn.HasAnimatedProperties);
        Assert.IsTrue(GearMotion.IsTurning(button));
        Assert.IsTrue(GearMotion.Play(button, move: true)); // a running turn finishes, no restart
        Assert.IsFalse(GearMotion.Play(button, move: false));
        Assert.IsFalse(GearMotion.IsTurning(button));
        Assert.IsFalse(turn.HasAnimatedProperties);
        Assert.AreEqual(0, turn.Angle);
        // Eight teeth: a whole number of teeth further looks the same as at rest.
        Assert.AreEqual(0, GearMotion.Angle % GearMotion.Tooth);
        Assert.IsTrue(GearMotion.Angle >= 90);

        // Without a turnable symbol nothing happens.
        Assert.IsFalse(GearMotion.Play(new Button { Content = "x" }, move: true));
    });

    /// <summary>
    /// The turn really moves the symbol, ends at rest and is taken off, so the next mouse-over turns
    /// again (before, a finished turn still counted as running and the gear turned only once).
    /// Shown in an invisible window: the animation clock only ticks for something rendered.
    /// </summary>
    [TestMethod]
    public void Gear_turns_through_and_again_on_the_next_mouse_over() => OnSta(() =>
    {
        var turn = new RotateTransform();
        var button = new Button { Content = new Border { Width = 20, Height = 20, RenderTransform = turn } };
        var window = new Window
        {
            Content = button,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000,
            Top = -30000,
            Width = 60,
            Height = 60,
        };
        window.Show();
        try
        {
            for (int round = 0; round < 2; round++)
            {
                var angles = new List<double>();
                EventHandler sample = (_, _) => angles.Add(turn.Angle);
                CompositionTarget.Rendering += sample;
                Assert.IsTrue(GearMotion.Play(button, move: true));
                Pump(TimeSpan.FromMilliseconds(900), () => !GearMotion.IsTurning(button));
                CompositionTarget.Rendering -= sample;

                Assert.IsFalse(GearMotion.IsTurning(button), $"round {round}: the turn has ended");
                Assert.IsFalse(turn.HasAnimatedProperties, $"round {round}: taken off");
                Assert.AreEqual(0, turn.Angle);
                Assert.IsTrue(angles.Any(a => a > 10 && a < GearMotion.Angle - 10), $"round {round}: seen in between ({string.Join(" ", angles)})");
            }
        }
        finally
        {
            window.Close();
        }
    });

    private static void Pump(TimeSpan max, Func<bool> done)
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
