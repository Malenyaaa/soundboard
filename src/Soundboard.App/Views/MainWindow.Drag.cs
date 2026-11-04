using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Soundboard.App.ViewModels;
using Soundboard.Core.Config;

namespace Soundboard.App.Views;

/// <summary>
/// Reordering pads with the mouse. Pressing a pad and moving it a few pixels starts the drag (so a
/// click stays a click); a bar between the pads shows where it lands. This is no OLE drag &amp; drop,
/// so it never meets the file and picture drop of the window.
/// </summary>
public partial class MainWindow
{
    /// <summary>Distance the mouse must travel before a press becomes a drag.</summary>
    private const double DragStartDistance = 8;

    private TileViewModel? _pressTile;
    private Point _pressAt;
    private bool _dragging;
    private TileViewModel? _dropBefore;

    private void OnPadMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressTile = null;
        if (sender is not FrameworkElement { DataContext: TileViewModel tile } || tile.IsRenaming || tile.IsCapturingKey || tile.IsIssueOpen) return;
        if (IsInside<TextBox>(e.OriginalSource as DependencyObject)) return;
        _pressTile = tile;
        _pressAt = e.GetPosition(PadList);
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressTile is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(drop: false);
            return;
        }
        var p = e.GetPosition(PadList);
        if (!_dragging)
        {
            if ((p - _pressAt).Length < DragStartDistance) return;
            _dragging = true;
            // Taking the mouse from the pad button cancels its click.
            Mouse.Capture(PadList, CaptureMode.SubTree);
            if (Container(_pressTile) is { } c) c.Opacity = 0.45;
        }
        AutoScroll(e.GetPosition(PadScroll));
        ShowInsertMark(p);
        e.Handled = true;
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            _pressTile = null;
            return;
        }
        ShowInsertMark(e.GetPosition(PadList));
        EndDrag(drop: true);
        e.Handled = true;
    }

    private void EndDrag(bool drop)
    {
        var tile = _pressTile;
        var before = _dropBefore;
        bool was = _dragging;
        _pressTile = null;
        _dragging = false;
        _dropBefore = null;
        InsertMark.Visibility = Visibility.Collapsed;
        if (!was) return;
        if (tile is not null && Container(tile) is { } c) c.Opacity = 1;
        if (Mouse.Captured == PadList) PadList.ReleaseMouseCapture();
        if (drop && tile is not null)
        {
            _vm.MoveTileBefore(tile, before);
            FocusPad(tile);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging && Mouse.Captured != PadList) EndDrag(drop: false);
    }

    /// <summary>Visible pads in order with their boxes (relative to the pad list).</summary>
    private List<(TileViewModel Tile, PadBox Box)> VisiblePads()
    {
        var list = new List<(TileViewModel, PadBox)>();
        foreach (var tile in _vm.Tiles)
        {
            if (Container(tile) is not { IsVisible: true } c) continue;
            var at = c.TranslatePoint(new Point(0, 0), PadList);
            list.Add((tile, new PadBox(at.X, at.Y, c.ActualWidth, c.ActualHeight)));
        }
        return list;
    }

    private void ShowInsertMark(Point p)
    {
        var pads = VisiblePads();
        if (pads.Count == 0) return;
        int i = PadOrder.InsertIndex(pads.Select(x => x.Box).ToList(), p.X, p.Y);
        _dropBefore = i < pads.Count ? pads[i].Tile : null;
        // The bar sits in the gap left of the pad it goes in front of; behind the last pad of the
        // pointer's row it sits right of that pad instead (not at the start of the next row).
        double rowTop = pads.Select(x => x.Box.Y).Where(top => top <= p.Y).DefaultIfEmpty(pads[0].Box.Y).Max();
        bool before = i < pads.Count && pads[i].Box.Y == rowTop;
        var box = before ? pads[i].Box : pads[Math.Max(i - 1, 0)].Box;
        double x = before ? box.X - 8 : box.X + box.Width + 4;
        var origin = PadList.TranslatePoint(new Point(0, 0), DragLayer);
        Canvas.SetLeft(InsertMark, origin.X + x);
        Canvas.SetTop(InsertMark, origin.Y + box.Y);
        InsertMark.Height = box.Height;
        InsertMark.Visibility = Visibility.Visible;
    }

    /// <summary>The pad an arrow key leads to: left/right in order, up/down the nearest pad in the row above/below.</summary>
    private TileViewModel? Neighbour(TileViewModel from, Key key)
    {
        var pads = VisiblePads();
        int i = pads.FindIndex(p => ReferenceEquals(p.Tile, from));
        if (i < 0) return null;
        if (key == Key.Left) return i > 0 ? pads[i - 1].Tile : null;
        if (key == Key.Right) return i < pads.Count - 1 ? pads[i + 1].Tile : null;
        var box = pads[i].Box;
        var rows = pads.Select(p => p.Box.Y).Distinct().Order().ToList();
        int r = rows.IndexOf(box.Y) + (key == Key.Up ? -1 : 1);
        if (r < 0 || r >= rows.Count) return null;
        return pads.Where(p => p.Box.Y == rows[r]).OrderBy(p => Math.Abs(p.Box.CenterX - box.CenterX)).First().Tile;
    }

    /// <summary>Near the top or bottom edge the list scrolls along while dragging.</summary>
    private void AutoScroll(Point inScroll)
    {
        const double edge = 36, step = 14;
        if (inScroll.Y < edge) PadScroll.ScrollToVerticalOffset(PadScroll.VerticalOffset - step);
        else if (inScroll.Y > PadScroll.ActualHeight - edge) PadScroll.ScrollToVerticalOffset(PadScroll.VerticalOffset + step);
    }

    private FrameworkElement? Container(TileViewModel tile) => PadList.ItemContainerGenerator.ContainerFromItem(tile) as FrameworkElement;

    /// <summary>Keyboard focus to a pad once it is laid out (after a move).</summary>
    private void FocusPad(TileViewModel tile) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => PadButton(tile)?.Focus());

    private static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }
}
