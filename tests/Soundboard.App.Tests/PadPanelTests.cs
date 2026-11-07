using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soundboard.App.ViewModels;
using Soundboard.App.Views;

namespace Soundboard.App.Tests;

[TestClass]
public class PadPanelTests
{
    /// <summary>A pad stand-in: a border whose data is a (bare) pad view model.</summary>
    private static Border Pad() => new() { DataContext = RuntimeHelpers.GetUninitializedObject(typeof(TileViewModel)) };

    /// <summary>A group head stand-in.</summary>
    private static Border Head() => new() { DataContext = RuntimeHelpers.GetUninitializedObject(typeof(CategoryViewModel)) };

    /// <summary>Where a child is laid out (without a slide that may still run).</summary>
    private static Point At(UIElement e)
    {
        var p = e.TranslatePoint(new Point(0, 0), (UIElement)VisualTreeHelper.GetParent(e));
        return e.RenderTransform is TranslateTransform t ? new Point(p.X - t.X, p.Y - t.Y) : p;
    }

    private static PadPanel Panel(params UIElement[] children)
    {
        var panel = new PadPanel { MinPadWidth = 100, Gap = 10, AnimateOverride = false };
        foreach (var c in children) panel.Children.Add(c);
        Layout(panel);
        return panel;
    }

    // 4 columns: (430 + 10) / (100 + 10) = 4
    private static void Layout(PadPanel panel)
    {
        panel.Measure(new Size(430, double.PositiveInfinity));
        panel.Arrange(new Rect(new Size(430, panel.DesiredSize.Height)));
    }

    [TestMethod]
    public void A_head_takes_a_row_of_its_own_and_starts_a_new_row_of_pads() => PanelMotionTests.OnSta(() =>
    {
        var h1 = Head();
        var h2 = Head();
        var pads = new[] { Pad(), Pad(), Pad(), Pad(), Pad() };
        var panel = Panel(h1, pads[0], pads[1], h2, pads[2], pads[3], pads[4]);

        double h = pads[0].RenderSize.Height, head = PadPanel.HeadHeight + PadPanel.HeadGap;
        Assert.AreEqual(0, At(h1).Y);
        Assert.AreEqual(430, h1.RenderSize.Width, "a head spans the whole width");
        Assert.AreEqual(head, At(pads[0]).Y, 0.5, "the first group's pads under its head");
        Assert.AreEqual(At(pads[0]).Y, At(pads[1]).Y);
        Assert.AreEqual(head + h + 16, At(h2).Y, 0.5, "the next head after a group gap, not beside the pads");
        Assert.AreEqual(0, At(pads[2]).X, "the next group starts at the left");
        Assert.AreEqual(head + h + 16 + head, At(pads[2]).Y, 0.5);
        Assert.AreEqual(At(pads[2]).Y, At(pads[4]).Y, "three pads fit one row");
        Assert.AreEqual(2 * h + 2 * head + 16, panel.DesiredSize.Height, 0.5);
    });

    [TestMethod]
    public void Without_heads_the_pads_simply_wrap() => PanelMotionTests.OnSta(() =>
    {
        var pads = new[] { Pad(), Pad(), Pad(), Pad(), Pad() };
        Panel(pads);
        Assert.AreEqual(0, At(pads[3]).Y);
        Assert.AreEqual(pads[0].RenderSize.Height + 10, At(pads[4]).Y, 0.5, "fifth pad wraps normally, no group gap");
    });

    [TestMethod]
    public void Hidden_children_take_no_place() => PanelMotionTests.OnSta(() =>
    {
        var head = Head();
        head.Visibility = Visibility.Collapsed;
        var pads = new[] { Pad(), Pad(), Pad() };
        pads[1].Visibility = Visibility.Collapsed;
        Panel(head, pads[0], pads[1], pads[2]);
        Assert.AreEqual(0, At(pads[0]).Y, "no room for a hidden head");
        Assert.AreEqual(110, At(pads[2]).X, 0.5, "the hidden pad leaves no gap");
    });

    [TestMethod]
    public void A_dragged_pad_makes_room_where_it_would_land_and_follows_the_mouse() => PanelMotionTests.OnSta(() =>
    {
        var pads = new[] { Pad(), Pad(), Pad(), Pad() };
        var panel = Panel(pads);

        panel.BeginDrag(pads[0]);
        panel.SetDropBefore(pads[3]); // between the third and the fourth
        panel.DragTopLeft = new Point(200, 40);
        Layout(panel);

        Assert.AreEqual(0, At(pads[1]).X, 0.5, "the others moved up");
        Assert.AreEqual(110, At(pads[2]).X, 0.5);
        Assert.AreEqual(220, At(pads[0]).X, 0.5, "the dragged pad's place is the gap");
        Assert.AreEqual(330, At(pads[3]).X, 0.5);
        Assert.AreEqual(new Point(200, 40), pads[0].TranslatePoint(new Point(0, 0), panel), "drawn under the mouse");
        CollectionAssert.AreEqual(new UIElement[] { pads[1], pads[2], pads[0], pads[3] }, panel.Order());
    });

    [TestMethod]
    public void After_letting_go_the_pad_lands_in_its_place() => PanelMotionTests.OnSta(() =>
    {
        var pads = new[] { Pad(), Pad(), Pad() };
        var panel = Panel(pads);
        panel.BeginDrag(pads[2]);
        panel.SetDropBefore(pads[0]);
        panel.DragTopLeft = new Point(15, 60);
        Layout(panel);

        panel.EndDrag(); // not moved in the list: it goes back to the end
        Layout(panel);
        Assert.IsNull(panel.Dragged);
        Assert.AreEqual(220, At(pads[2]).X, 0.5);
        Assert.AreEqual(new Point(220, 0), pads[2].TranslatePoint(new Point(0, 0), panel), "without animation it stands there at once");
        Assert.AreEqual(0, System.Windows.Controls.Panel.GetZIndex(pads[2]), "no longer on top");
    });

    [TestMethod]
    public void With_animations_a_moved_pad_starts_where_it_was() => PanelMotionTests.OnSta(() =>
    {
        var pads = new[] { Pad(), Pad(), Pad() };
        var panel = Panel(pads);
        panel.AnimateOverride = true;
        Layout(panel);

        panel.Children.Remove(pads[0]);
        panel.Children.Add(pads[0]); // the first pad moved to the end
        Layout(panel);

        var shift = (TranslateTransform)pads[0].RenderTransform;
        Assert.AreEqual(220, pads[0].TranslatePoint(new Point(0, 0), panel).X - shift.X, 0.5, "laid out at its new place");
        Assert.IsTrue(shift.HasAnimatedProperties, "and gliding there from where it was");
        Assert.IsTrue(((TranslateTransform)pads[1].RenderTransform).HasAnimatedProperties, "the pad that moved up glides as well");

        // Picked up while (or after) gliding: the glide stops, and the pad follows the mouse at once, without a new layout.
        panel.BeginDrag(pads[1]);
        Assert.IsFalse(((TranslateTransform)pads[1].RenderTransform).HasAnimatedProperties, "a glide would hold the pad in place");
        panel.DragTopLeft = new Point(300, 50);
        Assert.AreEqual(new Point(300, 50), pads[1].TranslatePoint(new Point(0, 0), panel));
    });
}
