using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Soundboard.App.ViewModels;
using Soundboard.Core.Config;

namespace Soundboard.App.Views;

/// <summary>
/// Reordering pads with the mouse. Pressing a pad and moving it a few pixels starts the drag (so a click stays a
/// click). The pad hangs at the mouse, the other pads slide aside to make room where it would land, and on
/// release it glides into that gap (<see cref="PadPanel"/>). Dragged into another group – also onto a group head,
/// even a collapsed or empty one – it goes into that group. Esc puts it back. This is no OLE drag &amp; drop, so it
/// never meets the file and picture drop of the window.
/// Own categories are moved the same way by their head: while dragged, the group folds up (its pads take no
/// place), the other groups make room, and on release it is put among the own categories where it was dropped.
/// </summary>
public partial class MainWindow
{
    /// <summary>Distance the mouse must travel before a press becomes a drag.</summary>
    private const double DragStartDistance = 8;

    private TileViewModel? _pressTile;
    private Point _pressAt;
    private bool _dragging;
    private Vector _grab;               // mouse minus the pad's top left, in the panel
    private DropPlace? _drop;
    private TileViewModel? _dropBeforeTile;
    private CategoryViewModel? _dropHead;
    private CategoryViewModel? _pressHead;
    private int _headDropIndex = -1;

    private void OnHeadMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressHead = null;
        _pressTile = null;
        if (sender is not FrameworkElement { DataContext: CategoryViewModel { IsOwn: true, IsRenaming: false } head }) return;
        if (IsInside<TextBox>(e.OriginalSource as DependencyObject)) return;
        _pressHead = head;
        _pressAt = e.GetPosition(PadList);
    }

    /// <summary>Moving a category: its place among the own categories follows the mouse.</summary>
    private void MoveHead(MouseEventArgs e)
    {
        var panel = FindPadPanel(PadList);
        if (panel is null || _pressHead is not { } head) return;
        if (!_dragging)
        {
            if ((e.GetPosition(PadList) - _pressAt).Length < DragStartDistance) return;
            if (Container(head) is not { } c) return;
            _dragging = true;
            _headDropIndex = -1;
            Mouse.Capture(PadList, CaptureMode.SubTree);
            panel.Tuck(_vm.Tiles.Where(t => _vm.GroupOf(t) == head.Group).Select(Container).OfType<UIElement>());
            panel.UpdateLayout();
            _grab = new Vector(e.GetPosition(panel).X - c.TranslatePoint(new Point(0, 0), panel).X, PadPanel.HeadHeight / 2);
            panel.BeginDrag(c);
        }
        AutoScroll(e.GetPosition(PadScroll));
        var p = e.GetPosition(panel);
        panel.DragTopLeft = new Point(0, p.Y - _grab.Y);
        FollowHeadDrop(panel, p.Y);
        e.Handled = true;
    }

    private void FollowHeadDrop(PadPanel panel, double y)
    {
        if (panel.Dragged is not { } dragged) return;
        // Heads in layout order with their tops; each group reaches to the next head.
        var heads = panel.Boxes()
            .Where(b => !ReferenceEquals(b.Child, dragged) && b.Child is FrameworkElement { DataContext: CategoryViewModel })
            .Select(b => (El: b.Child, Head: (CategoryViewModel)((FrameworkElement)b.Child).DataContext, b.Box.Top))
            .ToList();
        double end = panel.ActualHeight;
        var own = new List<(UIElement El, double Top, double Bottom)>();
        for (int i = 0; i < heads.Count; i++)
            if (heads[i].Head.IsOwn) own.Add((heads[i].El, heads[i].Top, i + 1 < heads.Count ? heads[i + 1].Top : end));
        int index = PadDrop.CategoryIndex(own.Select(o => (o.Top, o.Bottom)).ToList(), y);
        if (index == _headDropIndex) return;
        _headDropIndex = index;
        // In front of the own head at that place; after the last one: in front of the head that follows it ("Other pads").
        UIElement? before = index < own.Count ? own[index].El
            : own.Count > 0 ? heads.SkipWhile(h => !ReferenceEquals(h.El, own[^1].El)).Skip(1).Select(h => h.El).FirstOrDefault()
            : heads.FirstOrDefault(h => h.Head.Group.Kind == GroupKind.Others).El;
        panel.SetDropBefore(before);
    }

    private void EndHeadDrag(bool drop)
    {
        var head = _pressHead;
        int index = _headDropIndex;
        _pressHead = null;
        _headDropIndex = -1;
        _dragging = false;
        if (Mouse.Captured == PadList) PadList.ReleaseMouseCapture();
        if (drop && head is not null && index >= 0)
        {
            int from = _vm.CategoryIndex(head);
            if (from >= 0 && index != from) _vm.MoveCategory(head, index - from);
        }
        if (FindPadPanel(PadList) is { } panel)
        {
            panel.EndDrag();
            panel.Untuck();
        }
    }

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
        if (_pressHead is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) EndDrag(drop: false);
            else MoveHead(e);
            return;
        }
        if (_pressTile is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(drop: false);
            return;
        }
        var panel = FindPadPanel(PadList);
        if (panel is null) return;
        if (!_dragging)
        {
            if ((e.GetPosition(PadList) - _pressAt).Length < DragStartDistance) return;
            if (Container(_pressTile) is not { } c) return;
            _dragging = true;
            _drop = null;
            // Taking the mouse from the pad button cancels its click.
            Mouse.Capture(PadList, CaptureMode.SubTree);
            // Where the pad is seen (also while it still slides): it keeps that offset to the mouse.
            _grab = e.GetPosition(panel) - c.TranslatePoint(new Point(0, 0), panel);
            panel.BeginDrag(c);
        }
        AutoScroll(e.GetPosition(PadScroll));
        var p = e.GetPosition(panel);
        panel.DragTopLeft = p - _grab;
        FollowDrop(panel, p);
        e.Handled = true;
    }

    /// <summary>A pad's slide that is still running (the place it is drawn minus the place it is laid out).</summary>
    private static Vector Shift(UIElement e) => e.RenderTransform is TranslateTransform t ? new Vector(t.X, t.Y) : default;

    /// <summary>Finds where the pad would land under the mouse and lets the others make room there.</summary>
    private void FollowDrop(PadPanel panel, Point p)
    {
        if (_pressTile is null || panel.Dragged is not { } dragged) return;
        var pads = new List<(FrameworkElement El, TileViewModel Tile, GroupedBox Box)>();
        var heads = new List<(FrameworkElement El, CategoryViewModel Head, HeadBox Box)>();
        foreach (var (child, r) in panel.Boxes())
        {
            if (ReferenceEquals(child, dragged) || child is not FrameworkElement fe) continue;
            var box = new PadBox(r.X, r.Y, r.Width, r.Height);
            if (fe.DataContext is TileViewModel t) pads.Add((fe, t, new GroupedBox(box, _vm.GroupOf(t))));
            else if (fe.DataContext is CategoryViewModel h) heads.Add((fe, h, new HeadBox(box, h.Group)));
        }
        var place = PadDrop.Find(pads.Select(x => x.Box).ToList(), heads.Select(x => x.Box).ToList(), p.X, p.Y);
        if (place is not { } d) return;
        _drop = d;
        bool inGroup = d.Before < pads.Count && pads[d.Before].Box.Group == d.Group;
        _dropBeforeTile = inGroup ? pads[d.Before].Tile : null;
        // In front of that pad; at the end of a group: in front of the next head (or at the very end).
        UIElement? before = inGroup ? pads[d.Before].El : NextHead(panel, d.Group, dragged);
        panel.SetDropBefore(before);
        // Into another group: its head says so.
        var head = d.Group != _vm.GroupOf(_pressTile) ? _vm.Head(d.Group) : null;
        if (!ReferenceEquals(head, _dropHead))
        {
            if (_dropHead is not null) _dropHead.IsDropTarget = false;
            _dropHead = head;
            if (head is not null) head.IsDropTarget = true;
        }
    }

    /// <summary>The head that follows <paramref name="group"/>'s head in the grid (shown or not), null after the last group.</summary>
    private static UIElement? NextHead(PadPanel panel, GroupKey group, UIElement dragged)
    {
        bool passed = false;
        foreach (var child in panel.Order())
        {
            if (ReferenceEquals(child, dragged) || child is not FrameworkElement { DataContext: CategoryViewModel h }) continue;
            if (passed) return child;
            if (h.Group == group) passed = true;
        }
        return null;
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            _pressTile = null;
            _pressHead = null;
            return;
        }
        if (_pressHead is not null)
        {
            if (FindPadPanel(PadList) is { } p) FollowHeadDrop(p, e.GetPosition(p).Y);
            EndHeadDrag(drop: true);
            e.Handled = true;
            return;
        }
        if (FindPadPanel(PadList) is { } panel) FollowDrop(panel, e.GetPosition(panel));
        EndDrag(drop: true);
        e.Handled = true;
    }

    private void EndDrag(bool drop)
    {
        if (_pressHead is not null)
        {
            if (_dragging) EndHeadDrag(drop);
            else _pressHead = null;
            return;
        }
        var tile = _pressTile;
        var place = _drop;
        var before = _dropBeforeTile;
        bool was = _dragging;
        _pressTile = null;
        _dragging = false;
        _drop = null;
        _dropBeforeTile = null;
        if (_dropHead is not null) _dropHead.IsDropTarget = false;
        _dropHead = null;
        if (!was) return;
        if (Mouse.Captured == PadList) PadList.ReleaseMouseCapture();
        if (drop && tile is not null && place is { } d) _vm.DropTile(tile, d.Group, before);
        // After the move (the pad is in its new place in the order): it glides there from under the mouse.
        FindPadPanel(PadList)?.EndDrag();
        if (tile is not null) FocusPad(tile);
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
            var at = c.TranslatePoint(new Point(0, 0), PadList) - Shift(c);
            list.Add((tile, new PadBox(at.X, at.Y, c.ActualWidth, c.ActualHeight)));
        }
        return list;
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

    private FrameworkElement? Container(object item) => PadList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;

    /// <summary>Keyboard focus to a pad once it is laid out (after a move).</summary>
    private void FocusPad(TileViewModel tile) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => PadButton(tile)?.Focus());

    private static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }
}
