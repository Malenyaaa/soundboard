using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Soundboard.App.ViewModels;

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
