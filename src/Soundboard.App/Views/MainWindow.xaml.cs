using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Soundboard.App.Themes;
using Soundboard.App.ViewModels;
using Soundboard.Core.Hotkeys;

namespace Soundboard.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _barTimer;
    private WindowState _restoreState;
    private WindowStyle _restoreStyle;
    private ResizeMode _restoreResize;
    private Point _lastMouse;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        Text.UiLanguage.Attach(this);
        _barTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _barTimer.Tick += (_, _) => HideBar();
        PreviewMouseMove += OnAnyMouseMove;
        // Tabbing into the faded bar brings it back.
        FullBar.IsKeyboardFocusWithinChanged += (_, _) => { if (_vm.IsFullScreen && FullBar.IsKeyboardFocusWithin) ShowBar(); };
        // Settings opened from elsewhere (tray menu, a notice) need the normal window.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SettingsOpen) && _vm.SettingsOpen) LeaveFullScreen();
        };
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

    // ---- full screen (F11)

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>Borderless over the whole screen of the current monitor (taskbar covered), or back to the window as it was.</summary>
    public void ToggleFullScreen()
    {
        if (_vm.IsFullScreen) LeaveFullScreen();
        else EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        _vm.PrepareFullScreen();
        _restoreState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        _restoreStyle = WindowStyle;
        _restoreResize = ResizeMode;
        // A maximized window keeps its frame size; go through normal so the borderless one covers the taskbar.
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        _vm.IsFullScreen = true;
        ShowBar();
    }

    public void LeaveFullScreen()
    {
        if (!_vm.IsFullScreen) return;
        _barTimer.Stop();
        _vm.IsFullScreen = false;
        WindowState = WindowState.Normal;
        WindowStyle = _restoreStyle;
        ResizeMode = _restoreResize;
        WindowState = _restoreState;
        FullBarContent.BeginAnimation(OpacityProperty, null);
        FullBarContent.Opacity = 1;
    }

    /// <summary>Mouse moved: the bar comes back and hides again after a while without movement.</summary>
    private void OnAnyMouseMove(object sender, MouseEventArgs e)
    {
        if (!_vm.IsFullScreen) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _lastMouse.X) < 2 && Math.Abs(p.Y - _lastMouse.Y) < 2) return; // WPF also reports moves without movement
        _lastMouse = p;
        ShowBar();
    }

    private void ShowBar()
    {
        FullBarContent.BeginAnimation(OpacityProperty, null);
        FullBarContent.Opacity = 1;
        _barTimer.Stop();
        _barTimer.Start();
    }

    private void HideBar()
    {
        _barTimer.Stop();
        if (!_vm.IsFullScreen) return;
        // Stays while the mouse is on it or a control in it has the keyboard focus.
        if (FullBar.IsMouseOver || FullBar.IsKeyboardFocusWithin)
        {
            _barTimer.Start();
            return;
        }
        if (ThemeManager.AnimationsOn)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            FullBarContent.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            FullBarContent.Opacity = 0;
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
            LeaveFullScreen(); // the window comes back as a window
            Hide();
            app.OnHiddenToTray();
        }
    }

    /// <summary>
    /// While a tile waits for its hotkey, the next key press goes to it. Esc closes the open box,
    /// the settings or the full screen. F11 switches full screen (unless a pad has F11 as its
    /// global key: then the key plays that pad everywhere, and the button in the header switches).
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.CapturingTile is null)
        {
            if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox { DataContext: TileViewModel { IsRenaming: true } }
                && !IsInOpenDropDown(e.OriginalSource as DependencyObject))
            {
                var open = _vm.Tiles.FirstOrDefault(t => t.IsIssueOpen);
                if (open is not null) open.IsIssueOpen = false;
                else if (_vm.SettingsOpen) _vm.CloseSettingsCommand.Execute(null);
                else if (_vm.IsFullScreen) LeaveFullScreen();
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
