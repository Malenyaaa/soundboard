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
            // Ctrl+Z: take back the newest step (removed or moved pad …), except inside a text field.
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
            {
                _vm.UndoLastCommand.Execute(null);
                e.Handled = true;
                return;
            }
            // Ctrl+Left/Right moves the focused pad one place.
            if (e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.Control && !_vm.SettingsOpen
                && Keyboard.FocusedElement is Button { DataContext: TileViewModel { IsRenaming: false } moving })
            {
                int at = _vm.Tiles.IndexOf(moving);
                _vm.MoveTile(moving, at + (e.Key == Key.Left ? -1 : 1));
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
        PadButton(_vm.Tiles[Math.Clamp(index, 0, _vm.Tiles.Count - 1)])?.Focus();
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

    /// <summary>Narrow window: the volume and the buttons move into a second header row.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => LayoutHeader(ActualWidth);

    public void LayoutHeader(double width)
    {
        // Measure what one row needs: monitor group + actions + stop button + gaps and padding.
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var monitor = (UIElement)HeaderGrid.Children[0];
        monitor.Measure(free);
        ActionGroup.Measure(free);
        StopButton.Measure(free);
        double need = monitor.DesiredSize.Width + ActionGroup.DesiredSize.Width + StopButton.DesiredSize.Width + 24 + 16 + 32 + 8;
        bool narrow = width < need;
        Grid.SetRow(ActionGroup, narrow ? 1 : 0);
        Grid.SetColumn(ActionGroup, narrow ? 0 : 1);
        Grid.SetColumnSpan(ActionGroup, narrow ? 3 : 1);
        ActionGroup.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(24, 0, 16, 0);

        // The notice puts its buttons under the text when the window is narrow.
        bool narrowNotice = width < 760;
        DockPanel.SetDock(NoticeButtons, narrowNotice ? Dock.Bottom : Dock.Right);
        NoticeButtons.Margin = narrowNotice ? new Thickness(28, 8, 0, 0) : new Thickness(12, 0, 0, 0);
        NoticeButtons.HorizontalAlignment = HorizontalAlignment.Left;
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
