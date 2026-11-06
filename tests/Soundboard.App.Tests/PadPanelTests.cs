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
    /// <summary>A pad stand-in: a border whose data is a pad view model with only "favourite" set.</summary>
    private static Border Pad(bool favorite)
    {
        var vm = (TileViewModel)RuntimeHelpers.GetUninitializedObject(typeof(TileViewModel));
        typeof(TileViewModel).GetField("_isFavorite", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(vm, favorite);
        return new Border { DataContext = vm };
    }

    private static Point At(UIElement e) => e.TranslatePoint(new Point(0, 0), (UIElement)VisualTreeHelper.GetParent(e));

    [TestMethod]
    public void Favourites_get_a_row_of_their_own() => PanelMotionTests.OnSta(() =>
    {
        var panel = new PadPanel { MinPadWidth = 100, Gap = 10 };
        var pads = new[] { Pad(true), Pad(true), Pad(false), Pad(false), Pad(false) };
        foreach (var p in pads) panel.Children.Add(p);
        // 4 columns: (430 + 10) / (100 + 10) = 4
        panel.Measure(new Size(430, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));

        double h = pads[0].RenderSize.Height, head = PadPanel.HeadHeight + PadPanel.HeadGap;
        Assert.AreEqual(head, At(pads[0]).Y, 0.5, "the favourites' head line comes first");
        Assert.AreEqual(head, At(pads[1]).Y, 0.5, "favourites side by side in the first row");
        Assert.AreEqual(0, At(pads[2]).X, "the others start at the left of a new row");
        Assert.AreEqual(head + h + 16 + head, At(pads[2]).Y, 0.5, "below the favourites: group gap and the others' head line");
        Assert.AreEqual(At(pads[2]).Y, At(pads[4]).Y, "the three others fit one row");
        Assert.AreEqual(2 * h + 2 * head + 16, panel.DesiredSize.Height, 0.5);
        Assert.AreEqual((0.0, head + h + 16), panel.HeadTops);
    });

    [TestMethod]
    public void Without_favourites_nothing_changes() => PanelMotionTests.OnSta(() =>
    {
        var panel = new PadPanel { MinPadWidth = 100, Gap = 10 };
        var pads = new[] { Pad(false), Pad(false), Pad(false), Pad(false), Pad(false) };
        foreach (var p in pads) panel.Children.Add(p);
        panel.Measure(new Size(430, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        Assert.AreEqual(0, At(pads[3]).Y);
        Assert.AreEqual(pads[0].RenderSize.Height + 10, At(pads[4]).Y, 0.5, "fifth pad wraps normally, no group gap");
        Assert.IsTrue(double.IsNaN(panel.HeadTops.Favorites) && double.IsNaN(panel.HeadTops.Others), "no head lines without favourites");
    });
}
