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
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _vm.AddFiles(files);
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

    /// <summary>While a tile waits for its hotkey, the next key press goes to it.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.CapturingTile is null) return;
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
