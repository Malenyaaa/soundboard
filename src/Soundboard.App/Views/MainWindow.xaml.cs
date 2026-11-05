using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Soundboard.App.ViewModels;
using Soundboard.Core.Hotkeys;

namespace Soundboard.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        // The header measures its groups: again once laid out and after a language switch (other text widths).
        Loaded += (_, _) => LayoutHeader(ActualWidth);
        Core.Text.L.Changed += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => LayoutHeader(ActualWidth));
        // New pads: scroll to the first one (its ring shows the rest).
        _vm.PadsAdded += tile => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (Container(tile) is { } c) c.BringIntoView(new Rect(-16, -16, c.ActualWidth + 32, c.ActualHeight + 32));
        });
        Text.UiLanguage.Attach(this);
    }

    // ---- hex field of the custom colour: the short form counts once the field is left

    private void OnHexLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _vm.CommitCustomAccentText();

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.CommitCustomAccentText();
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        // A picture over a pad: that pad shows it will take the picture.
        bool picture = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(Soundboard.Core.Images.ImageFiles.HasImageExtension);
        MarkDropTarget(picture ? FindTile(e.OriginalSource as DependencyObject) : null);
        e.Handled = true;
    }

    /// <summary>Leaving a child element also raises DragLeave; only clear the mark when the mouse leaves the window.</summary>
    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) MarkDropTarget(null);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        MarkDropTarget(null);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _vm.DropFiles(files, FindTile(e.OriginalSource as DependencyObject));
        e.Handled = true;
    }

    private void MarkDropTarget(TileViewModel? target)
    {
        foreach (var t in _vm.Tiles) t.IsImageDropTarget = ReferenceEquals(t, target);
    }

    /// <summary>The pad under the mouse (the pad grid only; the settings panel lies above it when open).</summary>
    private TileViewModel? FindTile(DependencyObject? source)
    {
        if (_vm.SettingsOpen) return null;
        for (var d = source; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                 : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: TileViewModel tile } fe && fe.Name == "Root") return tile;
        }
        return null;
    }

    private void OnClosed(object? sender, EventArgs e) => _vm.Dispose();

    /// <summary>Closing the window keeps the app running in the tray (the microphone path must go on).</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            _vm.CancelKeyCapture();
            Hide();
            app.OnHiddenToTray();
        }
    }

    /// <summary>
    /// While a tile waits for its hotkey, the next key press goes to it. Esc closes the open box
    /// or the settings.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // A numpad key in this session: the keyboard has a numpad, so it may be suggested.
        if (HotkeyGesture.IsNumpadKey(KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key))) _vm.NumpadSeen = true;
        if (_dragging && e.Key == Key.Escape)
        {
            EndDrag(drop: false);
            e.Handled = true;
            return;
        }
        if (_vm.Capturing is null)
        {
            // Ctrl+F: to the search.
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }
            // Ctrl+O: add sounds (the file dialog).
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.AddFilesDialogCommand.Execute(null);
                e.Handled = true;
                return;
            }
            // Ctrl+Z: take back the newest step (removed or moved pad …), except inside a text field.
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
            {
                _vm.UndoLastCommand.Execute(null);
                e.Handled = true;
                return;
            }
            // Arrow keys move the focus between the pads, like on a pad grid.
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.Modifiers == ModifierKeys.None && !_vm.SettingsOpen
                && Keyboard.FocusedElement is Button { DataContext: TileViewModel { IsRenaming: false } from } && Neighbour(from, e.Key) is { } to)
            {
                PadButton(to)?.Focus();
                e.Handled = true;
                return;
            }
            // Ctrl+Left/Right moves the focused pad one place.
            if (e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.Control && !_vm.SettingsOpen
                && Keyboard.FocusedElement is Button { DataContext: TileViewModel { IsRenaming: false } moving })
            {
                // One place among the pads on screen (a search may hide some in between).
                if (Neighbour(moving, e.Key) is { } next) _vm.MoveTile(moving, _vm.Tiles.IndexOf(next));
                FocusPad(moving);
                e.Handled = true;
                return;
            }
            // F2 renames, Delete removes the focused pad (with "Undo").
            if (e.Key is Key.F2 or Key.Delete && Keyboard.Modifiers == ModifierKeys.None && !_vm.SettingsOpen
                && Keyboard.FocusedElement is Button { DataContext: TileViewModel { IsRenaming: false } tile })
            {
                if (e.Key == Key.F2) tile.IsRenaming = true;
                else
                {
                    int index = _vm.Tiles.IndexOf(tile);
                    _vm.RemoveTile(tile);
                    FocusPadAt(index);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox { DataContext: TileViewModel { IsRenaming: true } }
                && !(ReferenceEquals(Keyboard.FocusedElement, SearchBox) && _vm.SearchActive)
                && !IsInOpenDropDown(e.OriginalSource as DependencyObject))
            {
                var open = _vm.Tiles.FirstOrDefault(t => t.IsIssueOpen);
                if (open is not null) open.IsIssueOpen = false;
                else if (_vm.SettingsOpen) _vm.CloseSettingsCommand.Execute(null);
                else return;
                e.Handled = true;
            }
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        var m = Keyboard.Modifiers;
        var mods = HotkeyModifiers.None;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        if (_vm.OnCapturedKey(vk, mods)) e.Handled = true;
    }

    /// <summary>Puts the keyboard focus on the pad at <paramref name="index"/> (or the last one), once it is laid out.</summary>
    public void FocusPadAt(int index) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
    {
        if (_vm.Tiles.Count == 0) return;
        index = Math.Clamp(index, 0, _vm.Tiles.Count - 1);
        // The nearest pad the search shows: forwards first, then backwards.
        var tile = _vm.Tiles.Skip(index).FirstOrDefault(t => !t.IsFilteredOut)
            ?? _vm.Tiles.Take(index).LastOrDefault(t => !t.IsFilteredOut);
        if (tile is not null) PadButton(tile)?.Focus();
    });

    /// <summary>The pad button of a tile, null while it is not generated.</summary>
    private Button? PadButton(TileViewModel tile) =>
        PadList.ItemContainerGenerator.ContainerFromItem(tile) is ContentPresenter { ContentTemplate: { } template } cp
            ? template.FindName("PadBtn", cp) as Button
            : null;

    /// <summary>Esc inside an open list belongs to the list (it closes the list, not the settings).</summary>
    private static bool IsInOpenDropDown(DependencyObject? source)
    {
        for (var d = source; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                 : LogicalTreeHelper.GetParent(d))
        {
            if (d is ComboBox { IsDropDownOpen: true } or ComboBoxItem or System.Windows.Controls.ContextMenu) return true;
        }
        return false;
    }

    /// <summary>Narrow window: the volume and the search move into a second header row.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => LayoutHeader(ActualWidth);

    public void LayoutHeader(double width)
    {
        if (width <= 0 || double.IsNaN(width)) return; // not laid out yet (e.g. started in the tray)
        // Measure the groups once, then pick the first arrangement that fits:
        // one row; monitor + search + Stop/gear over the volume; or monitor + Stop/gear over volume + search.
        // Stop and the gear always stay together at the top right.
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        MonitorGroup.Measure(free);
        VolumeGroup.Measure(free);
        StopButton.Measure(free);
        SettingsButton.Measure(free);
        const double gap = 16, searchMin = 140, searchMax = 280, searchTight = 120;
        double inner = width - 32 - 8; // header padding, a little air for rounding and the window frame
        // Without the volume's margin: it changes with the arrangement, the gaps below stand for it.
        double mon = MonitorGroup.DesiredSize.Width, vol = VolumeGroup.DesiredSize.Width - VolumeGroup.Margin.Left - VolumeGroup.Margin.Right;
        double end = StopButton.DesiredSize.Width + SettingsButton.DesiredSize.Width; // the gear's margin is part of its desired width

        if (inner >= mon + gap + vol + gap + searchMin + gap + end)
        {
            Place(VolumeGroup, 0, 1, 1, new Thickness(gap, 0, gap, 0));
            Place(SearchGroup, 0, 2, 1, new Thickness(0, 0, gap, 0));
            SearchGroup.Width = Math.Min(searchMax, inner - (mon + gap + vol + gap + gap + end));
        }
        else if (inner >= mon + gap + searchMin + gap + end && vol <= inner)
        {
            Place(SearchGroup, 0, 1, 2, new Thickness(gap, 0, gap, 0));
            Place(VolumeGroup, 1, 0, 5, new Thickness(0, 10, 0, 0));
            SearchGroup.Width = Math.Min(searchMax, inner - (mon + gap + gap + end));
        }
        else if (inner - vol - gap >= searchTight)
        {
            // Both span the whole row (the monitor sets the first column's width); the search sits on the right.
            Place(VolumeGroup, 1, 0, 5, new Thickness(0, 10, 0, 0));
            Place(SearchGroup, 1, 0, 5, new Thickness(gap, 10, 0, 0));
            SearchGroup.Width = Math.Min(searchMax, inner - vol - gap);
        }
        else
        {
            Place(VolumeGroup, 1, 0, 5, new Thickness(0, 10, 0, 0));
            Place(SearchGroup, 2, 0, 5, new Thickness(0, 10, 0, 0));
            SearchGroup.Width = Math.Max(searchTight, Math.Min(searchMax * 1.5, inner));
        }
        SearchGroup.HorizontalAlignment = Grid.GetRow(SearchGroup) == 2 ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        // Tab follows the reading order of the arrangement: row by row, left to right.
        UIElement[] parts = [MonitorGroup, VolumeGroup, SearchGroup, StopButton, SettingsButton];
        int tab = 0;
        foreach (var part in parts.OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn))
            KeyboardNavigation.SetTabIndex(part, tab++);

        // The notice puts its buttons under the text when the window is narrow.
        bool narrowNotice = width < 760;
        DockPanel.SetDock(NoticeButtons, narrowNotice ? Dock.Bottom : Dock.Right);
        NoticeButtons.Margin = narrowNotice ? new Thickness(28, 8, 0, 0) : new Thickness(12, 0, 0, 0);
        NoticeButtons.HorizontalAlignment = HorizontalAlignment.Left;
    }

    private static void Place(UIElement e, int row, int column, int span, Thickness margin)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
        if (e is FrameworkElement fe) fe.Margin = margin;
    }

    /// <summary>Esc in the search: empties it (a second Esc goes on to the pads).</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.SearchActive)
        {
            _vm.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape || e.Key == Key.Down)
        {
            FocusPadAt(0);
            e.Handled = true;
        }
    }

    /// <summary>Ctrl + mouse wheel over a pad: its volume in steps of 5 % (the plain wheel scrolls).</summary>
    private void OnPadWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && sender is FrameworkElement { DataContext: TileViewModel tile } && !tile.IsRenaming)
        {
            tile.NudgeVolume(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    /// <summary>Mute and volume raise no device event: look again when the window comes to the front.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _vm.RecheckAudio();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _vm.CancelKeyCapture();
    }

    // ---- inline rename on a tile

    private void OnRenameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
        {
            box.Text = (box.DataContext as TileViewModel)?.Name ?? box.Text;
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not TileViewModel tile) return;
        if (e.Key == Key.Enter)
        {
            Commit(box, tile);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tile.IsRenaming = false; // discard: the explicit binding is not updated
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext is TileViewModel tile && tile.IsRenaming)
            Commit(box, tile);
    }

    private static void Commit(TextBox box, TileViewModel tile)
    {
        if (!string.IsNullOrWhiteSpace(box.Text))
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        tile.IsRenaming = false;
    }
}
