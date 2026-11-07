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
        // A new category: its head comes into view, its name open for typing.
        _vm.CategoryAdded += head => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (Container(head) is { } c) c.BringIntoView(new Rect(-16, -16, c.ActualWidth + 32, c.ActualHeight + 48));
        });
        // Right-click -> "Trim...": the editor is a dialog owned by this window.
        _vm.TrimRequested += tile => TrimWindow.Show(this, _vm, tile);
        // Main screen without tooltips (header, pads, status line, right-click menus); the settings keep theirs.
        _current = this;
        if (!s_tooltipFilter)
        {
            s_tooltipFilter = true;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent,
                new ToolTipEventHandler(OnAnyToolTipOpening));
        }
        // Settings: shown from the start if already open, later opened from the gear and closed back to it.
        SettingsPanel.Visibility = _vm.SettingsOpen ? Visibility.Visible : Visibility.Collapsed;
        // The line under the open tab: slides to a tab chosen with the mouse while the settings are seen,
        // stands at once when they open and after a resize or a language switch (it follows the tab sizes).
        _tabLine = new TabIndicator(SettingsTabStrip, SettingsTabLine);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SettingsOpen))
            {
                PanelMotion.Show(SettingsPanel, _vm.SettingsOpen, SettingsPanel.IsLoaded && IsVisible && SettingsMotionWanted());
                _tabLine.Follow(move: false);
            }
            else if (e.PropertyName == nameof(MainViewModel.AudioWarn) && !_vm.AudioWarn)
            {
                AudioPopup.IsOpen = false;
            }
            else if (e.PropertyName == nameof(MainViewModel.SettingsTab))
            {
                _tabLine.Follow(move: _vm.SettingsOpen && SettingsPanel.IsVisible && SettingsMotionWanted());
            }
        };
    }

    private readonly TabIndicator _tabLine;

    private static PadPanel? FindPadPanel(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is PadPanel p) return p;
            if (FindPadPanel(child) is { } found) return found;
        }
        return null;
    }

    private static MainWindow? _current;
    private static bool s_tooltipFilter;

    /// <summary>
    /// Cancels a tooltip on the main screen: in this window outside the settings panel, or in a
    /// right-click menu (its own popup, not part of the window tree). Other windows are untouched.
    /// </summary>
    private static void OnAnyToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (_current is not { } main || sender is not FrameworkElement el) return;
        if (IsOnMainScreen(main, el)) e.Handled = true;
    }

    private static bool IsOnMainScreen(MainWindow main, FrameworkElement el)
    {
        for (DependencyObject? d = el; d is not null; d = LogicalTreeHelper.GetParent(d) ?? (d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null))
            if (d is ContextMenu) return true;
        if (Window.GetWindow(el) != main) return false;
        return !main.SettingsPanel.IsAncestorOf(el);
    }

    /// <summary>
    /// The settings move only when opened or closed with the mouse (gear, "Close"): Esc or Enter on the gear
    /// is used often and should not wait for an animation; and only when animations are on.
    /// </summary>
    private static bool SettingsMotionWanted() =>
        Themes.ThemeManager.AnimationsOn && InputManager.Current.MostRecentInputDevice is not KeyboardDevice;

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

    // ---- "! Audio" in the status line: a small box with the reason and the way back

    /// <summary>When the box last closed: the click on "! Audio" that closed it must not open it again.</summary>
    private DateTime _audioPopupClosedAt;

    private void OnAudioPopupClosed(object? sender, EventArgs e) => _audioPopupClosedAt = DateTime.UtcNow;

    private void OnAudioStatusClick(object sender, RoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _audioPopupClosedAt).TotalMilliseconds < 300) return; // this click closed it
        _vm.ClearRestoreResult();
        AudioPopup.IsOpen = true;
    }

    /// <summary>Opened with the keyboard: the focus goes into the box, so Tab and Esc work there.</summary>
    private void OnAudioPopupOpened(object? sender, EventArgs e)
    {
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice && AudioPopupButtons.IsVisible)
            AudioPopupButtons.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnAudioPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        AudioPopup.IsOpen = false;
        AudioStatusButton.Focus();
        e.Handled = true;
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
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox { DataContext: IInlineRenamable { IsRenaming: true } }
                && !(ReferenceEquals(Keyboard.FocusedElement, SearchBox) && _vm.SearchActive)
                && !IsInOpenDropDown(e.OriginalSource as DependencyObject))
            {
                var open = _vm.Tiles.FirstOrDefault(t => t.IsIssueOpen);
                if (AudioPopup.IsOpen)
                {
                    AudioPopup.IsOpen = false;
                    AudioStatusButton.Focus();
                }
                else if (open is not null) open.IsIssueOpen = false;
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
        var tile = _vm.Tiles.Skip(index).FirstOrDefault(t => !t.IsHidden)
            ?? _vm.Tiles.Take(index).LastOrDefault(t => !t.IsHidden);
        if (tile is not null) PadButton(tile)?.Focus();
    });

    /// <summary>The pad button of a tile, null while it is not generated.</summary>
    private Button? PadButton(TileViewModel tile) => Part(tile, "PadBtn") as Button;

    /// <summary>A named part of the template of a pad or head, null while it is not generated.</summary>
    private object? Part(object item, string name)
    {
        if (PadList.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter cp) return null;
        // The grid picks its templates with a selector: the chosen one is not in ContentTemplate.
        var template = cp.ContentTemplate ?? cp.ContentTemplateSelector?.SelectTemplate(item, cp) ?? PadList.ItemTemplateSelector?.SelectTemplate(item, cp);
        return template?.FindName(name, cp);
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

    /// <summary>Narrow window: the volume and the search move into a second header row.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => LayoutHeader(ActualWidth);

    public void LayoutHeader(double width)
    {
        if (width <= 0 || double.IsNaN(width)) return; // not laid out yet (e.g. started in the tray)
        // Measure the groups once, then pick the first arrangement that fits:
        // one row; monitor + search + gear over the volume; or monitor + gear over volume + search.
        // The gear always stays at the top right.
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        MonitorGroup.Measure(free);
        VolumeGroup.Measure(free);
        SettingsButton.Measure(free);
        const double gap = 16, searchMin = 140, searchMax = 280, searchTight = 120;
        double inner = width - 32 - 8; // header padding, a little air for rounding and the window frame
        // Without the volume's margin: it changes with the arrangement, the gaps below stand for it.
        double mon = MonitorGroup.DesiredSize.Width, vol = VolumeGroup.DesiredSize.Width - VolumeGroup.Margin.Left - VolumeGroup.Margin.Right;
        double end = SettingsButton.DesiredSize.Width; // the gear's margin is part of its desired width

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
        UIElement[] parts = [MonitorGroup, VolumeGroup, SearchGroup, SettingsButton];
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

    // ---- inline rename on a tile: Enter, Esc and leaving the box are handled by InlineRename

    /// <summary>A press anywhere in the window outside the open rename box (pad or group head) keeps the typed name (also where no focus goes).</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        foreach (var item in _vm.PadItems)
        {
            if (item is not IInlineRenamable { IsRenaming: true }) continue;
            InlineRename.ConfirmIfOutside(RenameBox(item), e.OriginalSource as DependencyObject);
            break;
        }
    }

    private TextBox? RenameBox(object item) => Part(item, item is CategoryViewModel ? "HeadRenameBox" : "RenameBox") as TextBox;
}
