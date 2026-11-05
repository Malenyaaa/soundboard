using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Soundboard.App.Views;
using static Soundboard.App.Tests.PanelMotionTests;

namespace Soundboard.App.Tests;

[TestClass]
public class TabIndicatorTests
{
    [TestMethod]
    public void The_line_belongs_at_the_bottom_edge_of_the_tab_over_its_full_width()
    {
        Assert.AreEqual((10.0, 38.0, 80.0), TabIndicator.Target(new Rect(10, 5, 80, 36), 3));
    }

    /// <summary>A tab row as in the settings: tabs of different widths in a WrapPanel, the line in a Canvas over it.</summary>
    private sealed class Row
    {
        public readonly WrapPanel Tabs = new();
        public readonly Border Line = new() { Height = 3, Width = 0, Visibility = Visibility.Hidden };
        public readonly RadioButton[] Tab;
        public readonly Window Window;
        public readonly TabIndicator Indicator;

        public Row(double width)
        {
            Tab = new[] { 120.0, 70, 90, 110, 100 }.Select(w => new RadioButton { Width = w, Height = 36, GroupName = "row" }).ToArray();
            foreach (var t in Tab) Tabs.Children.Add(t);
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(Line);
            var grid = new Grid { Margin = new Thickness(-12, 6, 0, 0) };
            grid.Children.Add(Tabs);
            grid.Children.Add(canvas);
            Window = new Window
            {
                Content = new Border { Padding = new Thickness(16, 12, 16, 0), Child = grid },
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                Width = width,
                Height = 200,
            };
            Indicator = new TabIndicator(Tabs, Line);
        }

        public (double X, double Y, double Width) Bounds(int i)
        {
            var t = Tab[i];
            var p = t.TransformToVisual(Tabs).Transform(new Point(0, 0));
            return (p.X, p.Y + t.ActualHeight - 3, t.ActualWidth);
        }

        public (double X, double Y, double Width) Now => (Indicator.Shift.X, Indicator.Shift.Y, Line.Width);
    }

    [TestMethod]
    public void The_line_stands_under_each_tab_and_is_hidden_without_one() => OnSta(() =>
    {
        var row = new Row(800);
        row.Window.Show();
        try
        {
            Pump(TimeSpan.FromMilliseconds(100), () => false);
            Assert.IsNull(row.Indicator.Where());
            Assert.IsFalse(row.Indicator.Place(null));
            Assert.AreEqual(Visibility.Hidden, row.Line.Visibility, "no tab open");

            double lastX = -1;
            for (int i = 0; i < row.Tab.Length; i++)
            {
                row.Tab[i].IsChecked = true;
                row.Window.UpdateLayout();
                Assert.IsFalse(row.Indicator.Place(null), $"tab {i}: at once");
                Assert.AreEqual(Visibility.Visible, row.Line.Visibility);
                Assert.AreEqual(row.Bounds(i), row.Now, $"tab {i}");
                Assert.IsTrue(row.Now.X > lastX, $"tab {i} right of the one before");
                lastX = row.Now.X;
            }
            Assert.AreEqual((0.0, 33.0, 120.0), (row.Bounds(0).X, row.Bounds(0).Y, row.Bounds(0).Width), "first tab at the left edge of the row");
        }
        finally
        {
            row.Window.Close();
        }
    });

    [TestMethod]
    public void A_new_tab_lets_the_line_slide_there_and_end_at_rest() => OnSta(() =>
    {
        var row = new Row(800);
        row.Window.Show();
        try
        {
            row.Tab[0].IsChecked = true;
            row.Indicator.Follow(move: false);
            Pump(TimeSpan.FromMilliseconds(300), () => row.Line.Visibility == Visibility.Visible);
            var from = row.Now;
            Assert.AreEqual(row.Bounds(0), from, "first shown: at once");
            Assert.IsFalse(row.Indicator.IsMoving);

            row.Tab[3].IsChecked = true;
            row.Indicator.Follow(move: true);
            var seen = new List<(double X, double Y, double Width)>();
            EventHandler sample = (_, _) => seen.Add(row.Now);
            CompositionTarget.Rendering += sample;
            Pump(TimeSpan.FromMilliseconds(80), () => row.Indicator.IsMoving);
            Assert.IsTrue(row.Indicator.IsMoving, "slides");
            Pump(TimeSpan.FromMilliseconds(800), () => !row.Indicator.IsMoving);
            CompositionTarget.Rendering -= sample;

            var to = row.Bounds(3);
            Assert.IsFalse(row.Indicator.IsMoving, "taken off at the end");
            Assert.IsFalse(row.Line.HasAnimatedProperties || row.Indicator.Shift.HasAnimatedProperties);
            Assert.AreEqual(to, row.Now);
            Assert.IsTrue(seen.Any(s => s.X > from.X + 5 && s.X < to.X - 5), $"seen in between ({string.Join(" ", seen.Select(s => s.X))})");
            Assert.IsTrue(seen.All(s => s.X >= from.X - 0.001 && s.X <= to.X + 0.001), "never past the target (ease-out)");

            // Without motion (keyboard, animations off): there at once.
            row.Tab[1].IsChecked = true;
            row.Indicator.Follow(move: false);
            Pump(TimeSpan.FromMilliseconds(300), () => row.Now == row.Bounds(1));
            Assert.AreEqual(row.Bounds(1), row.Now);
            Assert.IsFalse(row.Indicator.IsMoving);
        }
        finally
        {
            row.Window.Close();
        }
    });

    [TestMethod]
    public void A_narrow_row_wraps_and_the_line_follows_into_the_second_line() => OnSta(() =>
    {
        var row = new Row(800);
        row.Window.Show();
        try
        {
            row.Tab[4].IsChecked = true;
            row.Indicator.Follow(move: false);
            Pump(TimeSpan.FromMilliseconds(300), () => row.Line.Visibility == Visibility.Visible);
            Assert.AreEqual(row.Bounds(4), row.Now);
            double y = row.Now.Y;

            row.Window.Width = 330; // the row breaks: the last tabs go into a second line
            Pump(TimeSpan.FromMilliseconds(400), () => row.Now.Y > y);
            Assert.AreEqual(row.Bounds(4), row.Now);
            Assert.IsTrue(row.Now.Y > y, "second line");
            Assert.IsFalse(row.Indicator.IsMoving, "a resize does not slide");
        }
        finally
        {
            row.Window.Close();
        }
    });
}
