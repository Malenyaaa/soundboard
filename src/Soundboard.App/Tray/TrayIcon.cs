using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Soundboard.App.ViewModels;

namespace Soundboard.App.Tray;

/// <summary>
/// Symbol in the notification area. Closing the window keeps the app (and the microphone
/// path) running; quitting goes through the tray menu with a confirmation.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MainViewModel _vm;
    private readonly MenuItem _pauseItem;

    public TrayIcon(App app, MainViewModel vm)
    {
        _vm = vm;
        var background = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA8));
        background.Freeze();

        var menu = new ContextMenu();
        menu.Items.Add(Item("Fenster öffnen", app.ShowMain));
        menu.Items.Add(Item("Alles stoppen", () => vm.StopAllCommand.Execute(null)));
        _pauseItem = Item("Hotkeys pausieren", () => vm.HotkeysPaused = !vm.HotkeysPaused);
        _pauseItem.IsCheckable = true;
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Einrichtung", () => { app.ShowMain(); vm.OpenSetupCommand.Execute(null); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Beenden …", app.RequestExit));

        _icon = new TaskbarIcon
        {
            ToolTipText = Tooltip(),
            IconSource = new GeneratedIconSource
            {
                Text = "♪",
                Foreground = Brushes.White,
                Background = background,
                FontSize = 46,
                FontWeight = System.Windows.FontWeights.Bold,
            },
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(app.ShowMain),
            ContextMenu = menu,
        };
        // false: no Windows "efficiency mode" for this process — it would slow the audio threads.
        _icon.ForceCreate(false);

        vm.PropertyChanged += OnVmChanged;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private string Tooltip() => $"Soundboard – {_vm.StatusCable}";

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusCable)) _icon.ToolTipText = Tooltip();
        if (e.PropertyName == nameof(MainViewModel.HotkeysPaused)) _pauseItem.IsChecked = _vm.HotkeysPaused;
    }

    /// <summary>Short Windows notification (no sound).</summary>
    public void ShowHint(string title, string text) =>
        _icon.ShowNotification(title, text, sound: false);

    public void Dispose()
    {
        _vm.PropertyChanged -= OnVmChanged;
        _icon.Dispose();
    }
}
